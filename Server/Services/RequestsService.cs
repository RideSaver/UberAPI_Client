using Grpc.Core;
using InternalAPI;
using Microsoft.Extensions.Caching.Distributed;
using UberClient.Models;
using UberClient.Extensions;
using UberClient.Interface;

using RequestsApi = UberAPI.Client.Api.RequestsApi;
using ProductsApi = UberAPI.Client.Api.ProductsApi;
using Configuration = UberAPI.Client.Client.Configuration;

namespace UberClient.Services
{
    public class RequestsService : Requests.RequestsBase
    {
        private readonly ILogger<RequestsService> _logger;
        private readonly IDistributedCache _cache;
        private readonly IAccessTokenService _accessTokenService;
        private readonly IHttpContextAccessor _httpContextAccessor;

        private readonly RequestsApi _requestsApiClient;
        private readonly ProductsApi _productsApiClient;

        public RequestsService(ILogger<RequestsService> logger, IDistributedCache cache, IAccessTokenService accessTokenService, IHttpContextAccessor httpContextAccessor)
        {
            _accessTokenService = accessTokenService;
            _httpContextAccessor = httpContextAccessor;
            _logger = logger;
            _cache = cache;

            _requestsApiClient = new RequestsApi();
            _productsApiClient = new ProductsApi();
        }

        public override async Task<RideModel> GetRideRequest(GetRideRequestModel request, ServerCallContext context)
        {
            var SessionToken = "" + _httpContextAccessor.HttpContext!.Request.Headers["token"];

            _logger.LogInformation($"[UberClient::RequestsService::GetRideRequest] HTTP Context session token: {SessionToken}");

            DistributedCacheEntryOptions options = new DistributedCacheEntryOptions() { AbsoluteExpirationRelativeToNow = TimeSpan.FromHours(24) };

            var cacheEstimate = await _cache.GetAsync<EstimateCache>(request.RideId);

            _requestsApiClient.Configuration = new Configuration { AccessToken = await _accessTokenService.GetAccessTokenAsync(SessionToken!, cacheEstimate.ProductId.ToString()) };

            var responseInstance = await _requestsApiClient.RequestRequestIdAsync(request.RideId);

            // Write an InternalAPI model back
            return new RideModel()
            {
                RideId = request.RideId,
                EstimatedTimeOfArrival = Google.Protobuf.WellKnownTypes.Timestamp.FromDateTime(DateTime.Now.AddSeconds(responseInstance.Pickup.Eta)),
                RideStage = getStageFromStatus(responseInstance.Status),
                RiderOnBoard = responseInstance.Status == "IN_PROGRESS",
                Price = new CurrencyModel
                {
                    Price = (double)cacheEstimate.EstimateInfo!.Price,
                    Currency = cacheEstimate.EstimateInfo.Currency,
                },
                Driver = new DriverModel
                {
                    DisplayName = responseInstance.Drivers.Name,
                    LicensePlate = responseInstance.Vehicle.LicensePlate,
                    CarPicture = responseInstance.Vehicle.PictureUrl,
                    CarDescription = $"{responseInstance.Vehicle.Make} {responseInstance.Vehicle.Model}",
                    DriverPronounciation = responseInstance.Drivers.Name
                },
                DriverLocation = new LocationModel
                {
                    Latitude = responseInstance.Location.Latitude,
                    Longitude = responseInstance.Location.Longitude
                },
            };
        }

        public override async Task<RideModel> PostRideRequest(PostRideRequestModel request, ServerCallContext context)
        {
            var SessionToken = "" + _httpContextAccessor.HttpContext!.Request.Headers["token"];

            _logger.LogInformation($"[UberClient::RequestsService::PostRideRequest] HTTP Context session token: {SessionToken}");

            DistributedCacheEntryOptions options = new DistributedCacheEntryOptions() { AbsoluteExpirationRelativeToNow = TimeSpan.FromHours(24)};

            var cacheEstimate = await _cache.GetAsync<EstimateCache> (request.EstimateId);

            _requestsApiClient.Configuration = new Configuration { AccessToken = await _accessTokenService.GetAccessTokenAsync(SessionToken, cacheEstimate.ProductId.ToString()), };

            UberAPI.Client.Model.CreateRequests requests = new(
                cacheEstimate.EstimateInfo!.FareId!,
                cacheEstimate.ProductId.ToString()
            )
            {
                FareId = cacheEstimate.EstimateInfo!.FareId!,
                ProductId = cacheEstimate.ProductId.ToString(),
                StartLatitude = (float)cacheEstimate.GetEstimatesRequest!.StartPoint.Latitude,
                StartLongitude = (float)cacheEstimate.GetEstimatesRequest.StartPoint.Longitude,
                EndLatitude = (float)cacheEstimate.GetEstimatesRequest.EndPoint.Latitude,
                EndLongitude = (float)cacheEstimate.GetEstimatesRequest.EndPoint.Longitude,
            };

            var responseInstance = await _requestsApiClient.CreateRequestsAsync(requests);

            cacheEstimate.RequestId = Guid.Parse(responseInstance._RequestId);

            await _cache.SetAsync(request.EstimateId, cacheEstimate, options);

            return new RideModel()
            {
                RideId = request.EstimateId,
                EstimatedTimeOfArrival = Google.Protobuf.WellKnownTypes.Timestamp.FromDateTime(DateTime.Now.AddSeconds(responseInstance.Pickup.Eta)),
                RideStage = getStageFromStatus(responseInstance.Status),
                RiderOnBoard = responseInstance.Status == "IN_PROGRESS",
                Driver = null,
                DriverLocation = null,
                Price = new CurrencyModel
                {
                    Price = (double)cacheEstimate.EstimateInfo.Price,
                    Currency = cacheEstimate.EstimateInfo.Currency
                }
            };
        }

        public override async Task<CurrencyModel> DeleteRideRequest(DeleteRideRequestModel request, ServerCallContext context)
        {
            var SessionToken = "" + _httpContextAccessor.HttpContext!.Request.Headers["token"];

            _logger.LogInformation($"[UberClient::RequestsService::DeleteRideRequest] HTTP Context session token: {SessionToken}");

            var cacheEstimate = await _cache.GetAsync<EstimateCache> (request.RideId);
            string accessToken = await _accessTokenService.GetAccessTokenAsync(SessionToken, cacheEstimate.ProductId.ToString());

            _requestsApiClient.Configuration = new Configuration { AccessToken = accessToken, };

            await _requestsApiClient.DeleteRequestsAsync(cacheEstimate.RequestId.ToString());
            _productsApiClient.Configuration = new Configuration { AccessToken = accessToken, };

            var product = await _productsApiClient.ProductProductIdAsync(cacheEstimate.ProductId.ToString());

            return new CurrencyModel
            {
                Price = (double)product.PriceDetails.CancellationFee,
                Currency = product.PriceDetails.CurrencyCode
            };
        }

        private Stage getStageFromStatus(string status)
        {
            switch (status)
            {
                case "processing": return Stage.Pending;
                case "accepted": return Stage.Accepted;
                case "no drivers available": return Stage.Cancelled;
                case "completed": return Stage.Completed;
                default: return Stage.Unknown;
            }
        }
    }
}
