using Grpc.Core;
using InternalAPI;
using Microsoft.Extensions.Caching.Distributed;
using UberClient.Models;
using UberClient.Extensions;
using UberClient.Interface;

using RequestsApi = UberAPI.Client.Api.RequestsApi;
using ProductsApi = UberAPI.Client.Api.ProductsApi;
using Configuration = UberAPI.Client.Client.Configuration;
using RideRequest = UberAPI.Client.Model.CreateRequests;

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

        public override async Task<RideModel> PostRideRequest(PostRideRequestModel request, ServerCallContext context)
        {
            // Extract the JWT Token from the request-headers to be used for the UserAccessToken
            var SessionToken = "" + _httpContextAccessor.HttpContext!.Request.Headers["token"];
            var estimateId = request.EstimateId.ToString();

            // Retrieve the Estimate instance from the cache for the EstimateID 
            var cacheEstimate = await _cache.GetAsync<EstimateCache>(estimateId);
            if (cacheEstimate is null) { throw new ArgumentNullException($"[UberClient::RequestService::PostRideRequest] {nameof(cacheEstimate)}"); }
            var serviceID = cacheEstimate!.ProductId.ToString();

            DistributedCacheEntryOptions options = new DistributedCacheEntryOptions() { AbsoluteExpirationRelativeToNow = TimeSpan.FromHours(24)};
            _requestsApiClient.Configuration = new Configuration { AccessToken = await _accessTokenService.GetAccessTokenAsync(SessionToken, serviceID), };

            // Create a new instance of RideRequest to send to the MockAPI.
            RideRequest requestInstance = new(
            fareId: Guid.NewGuid().ToString(),
            productId: cacheEstimate.ProductId.ToString(),
            startLatitude: (float)cacheEstimate.GetEstimatesRequest!.StartPoint.Latitude,
            startLongitude: (float)cacheEstimate.GetEstimatesRequest.StartPoint.Longitude,
            endLatitude: (float)cacheEstimate.GetEstimatesRequest.EndPoint.Latitude,
            endLongitude: (float)cacheEstimate.GetEstimatesRequest.EndPoint.Longitude)
            {
                SurgeConfirmationId = Guid.NewGuid().ToString(),
                PaymentMethodId = Guid.NewGuid().ToString(),
                Seats = cacheEstimate.GetEstimatesRequest.Seats
            };

            // Make the request to the MockAPI to recieve back the RequestID instance.
            var responseInstance = await _requestsApiClient.CreateRequestsAsync(requestInstance);
            if (responseInstance is null) { throw new ArgumentNullException($"[UberClient::RequestService::PostRideRequest] {nameof(responseInstance)}"); }

            // Add the new Request ID to the Estimate instance & reinsert it back into the cache.
            cacheEstimate.RequestId = Guid.Parse(responseInstance._RequestId);
            await _cache.SetAsync(request.EstimateId, cacheEstimate, options);

            // Create a new RideModel instance with the data we recieved to send it back to the RequestsAPI.
            return new RideModel()
            {
                RideId = request.EstimateId.ToString(),
                EstimatedTimeOfArrival = Google.Protobuf.WellKnownTypes.Timestamp.FromDateTime((DateTime.Now.AddSeconds(responseInstance.Pickup.Eta)).ToUniversalTime()),
                RideStage = getStageFromStatus(responseInstance.Status),
                RiderOnBoard = false,
                Driver = new DriverModel()
                {
                    DisplayName = responseInstance.Drivers.Name,
                    LicensePlate = responseInstance.Vehicle.LicensePlate,
                    CarPicture = responseInstance.Vehicle.PictureUrl,
                    CarDescription = "RideShare Vehicle",
                    DriverPronounciation = responseInstance.Drivers.Name
                },
                DriverLocation = new LocationModel()
                {
                    Latitude = responseInstance.Location.Latitude,
                    Longitude = responseInstance.Location.Longitude,
                    Height = 0f,
                    Planet = "Earth"
                },
                Price = new CurrencyModel
                {
                    Price = (double)cacheEstimate.EstimateInfo!.Price,
                    Currency = cacheEstimate.EstimateInfo!.Currency
                }
            };
        }

        public override async Task<RideModel> GetRideRequest(GetRideRequestModel request, ServerCallContext context)
        {
            var SessionToken = "" + _httpContextAccessor.HttpContext!.Request.Headers["token"];
            var estimateCacheId = request.RideId.ToString();

            _logger.LogInformation($"[UberClient::RequestsService::GetRideRequest] HTTP Context session token: {SessionToken}");

            var cacheEstimate = await _cache.GetAsync<EstimateCache>(estimateCacheId);
            var serviceID = cacheEstimate!.ProductId.ToString();

            if (cacheEstimate is null) { _logger.LogError("[UberClient::RequestsService::GetRideRequest] Cache Estimate is NULL"); }

            _requestsApiClient.Configuration = new Configuration { AccessToken = await _accessTokenService.GetAccessTokenAsync(SessionToken!, serviceID) };

            _logger.LogInformation($"[UberClient::RequestsService::GetRideRequest] Sending (estimateID) to MockAPI... \n{estimateCacheId}");

            var responseInstance = await _requestsApiClient.RequestRequestIdAsync(estimateCacheId);

            if (responseInstance is null) { _logger.LogError("[UberClient::RequestsService::GetRideRequest] RequestId is NULL"); }

            _logger.LogInformation($"[UberClient::RequestsService::GetRideRequest] Receiving (RequestId) from MockAPI... \n{responseInstance}");

            // Write an InternalAPI model back
            return new RideModel()
            {
                RideId = estimateCacheId,
                EstimatedTimeOfArrival = Google.Protobuf.WellKnownTypes.Timestamp.FromDateTime(DateTime.UtcNow.AddSeconds(responseInstance.Pickup.Eta)),
                RideStage = getStageFromStatus(responseInstance.Status),
                RiderOnBoard = false,
                Price = new CurrencyModel
                {
                    Price = (double)cacheEstimate!.EstimateInfo!.Price,
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
