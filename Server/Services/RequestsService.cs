using Grpc.Core;
using Microsoft.AspNetCore.Http;
using InternalAPI;
using Microsoft.Extensions.Caching.Distributed;

using UberClient.Server.Extensions.Cache;
using UberClient.Models;

using RequestsApi = UberAPI.Client.Api.RequestsApi;
using ProductsApi = UberAPI.Client.Api.ProductsApi;
using Configuration = UberAPI.Client.Client.Configuration;


namespace UberClient.Services
{
    public class RequestsService : Requests.RequestsBase
    {
        private readonly ILogger<RequestsService> _logger;
        private readonly IHttpClientFactory _clientFactory;
        private readonly IDistributedCache _cache;
        private readonly IAccessTokenService _accessTokenService;
        private readonly IHttpContextAccessor _httpContextAccessor;

        private readonly RequestsApi _requestsApiClient;
        private readonly ProductsApi _productsApiClient;
        private readonly HttpClient _httpClient;

        public RequestsService(ILogger<RequestsService> logger, IDistributedCache cache, IHttpClientFactory clientFactory, IAccessTokenService accessTokenService, IHttpContextAccessor httpContextAccessor)
        {
            _clientFactory = clientFactory;
            _httpClient = _clientFactory.CreateClient();
            _accessTokenService = accessTokenService;
            _httpContextAccessor = httpContextAccessor;

            _logger = logger;
            _cache = cache;

            _requestsApiClient = new RequestsApi(_httpClient, new Configuration { });
            _productsApiClient = new ProductsApi(_httpClient, new Configuration { });
        }

        public override async Task<RideModel> GetRideRequest(GetRideRequestModel request, ServerCallContext context)
        {
            var SessionToken = "" + _httpContextAccessor.HttpContext.Request.Headers["token"];

            _logger.LogInformation($"[UberClient::RequestsService::GetRideRequest] HTTP Context session token: {SessionToken}");

            DistributedCacheEntryOptions options = new DistributedCacheEntryOptions() { AbsoluteExpirationRelativeToNow = TimeSpan.FromHours(24) };
            //! Creating cacheEstimate with parameters.
            /*!
             \var EstimateCache cacheEstimate
             \param request.RideId parameter used to retrieve service.
            */
            var cacheEstimate = await _cache.GetAsync<EstimateCache> (request.RideId);
            // Create new API client (since it doesn't seem to allow dynamic loading of credentials)
            _requestsApiClient.Configuration = new Configuration
            {
                AccessToken = await _accessTokenService.GetAccessTokenAsync(SessionToken!, cacheEstimate.ProductId.ToString()),
            };
            //! Creating variable ride with parameters.
            var ride = await _requestsApiClient.RequestRequestIdAsync(request.RideId);
            // Write an InternalAPI model back
            return new RideModel() {
                RideId = request.RideId,
                EstimatedTimeOfArrival = Google.Protobuf.WellKnownTypes.Timestamp.FromDateTime(DateTime.Now.AddSeconds(ride.Pickup.Eta)),
                RiderOnBoard = ride.Status == "in_progress",
                Price = new CurrencyModel {
                    Price = (double)cacheEstimate.EstimateInfo.Price,
                    Currency = cacheEstimate.EstimateInfo.Currency,
                },
                Driver = new DriverModel {
                    DisplayName = ride.Drivers.Name,
                    LicensePlate = ride.Vehicle.LicensePlate,
                    CarPicture = ride.Vehicle.PictureUrl,
                    CarDescription = $"{ride.Vehicle.Make} {ride.Vehicle.Model}",
                    DriverPronounciation = ride.Drivers.Name
                },
                RideStage = getStageFromStatus(ride.Status),
                DriverLocation = new LocationModel {
                    Latitude = ride.Location.Latitude,
                    Longitude = ride.Location.Longitude
                },
            };
        }

        public override async Task<RideModel> PostRideRequest(PostRideRequestModel request, ServerCallContext context)
        {
            var SessionToken = "" + _httpContextAccessor.HttpContext.Request.Headers["token"];

            _logger.LogInformation($"[UberClient::RequestsService::PostRideRequest] HTTP Context session token: {SessionToken}");

            DistributedCacheEntryOptions options = new DistributedCacheEntryOptions() { AbsoluteExpirationRelativeToNow = TimeSpan.FromHours(24)};

            var cacheEstimate = await _cache.GetAsync<EstimateCache> (request.EstimateId);
            // Create new API client (since it doesn't seem to allow dynamic loading of credentials)
            _requestsApiClient.Configuration = new Configuration {
                AccessToken = await _accessTokenService.GetAccessTokenAsync(SessionToken, cacheEstimate.ProductId.ToString()),
            };
            UberAPI.Client.Model.CreateRequests requests = new UberAPI.Client.Model.CreateRequests() {
                FareId = cacheEstimate.EstimateInfo.FareId,
                ProductId = cacheEstimate.ProductId.ToString(),
                StartLatitude = (float)cacheEstimate.GetEstimatesRequest.StartPoint.Latitude,
                StartLongitude = (float)cacheEstimate.GetEstimatesRequest.StartPoint.Longitude,
                EndLatitude = (float)cacheEstimate.GetEstimatesRequest.EndPoint.Latitude,
                EndLongitude = (float)cacheEstimate.GetEstimatesRequest.EndPoint.Longitude,
            };

            var ride = await _requestsApiClient.CreateRequestsAsync(requests);
            cacheEstimate.RequestId = Guid.Parse(ride._RequestId);
            _=_cache.SetAsync<EstimateCache>(request.EstimateId, cacheEstimate, options);
            return new RideModel() {
                RideId = request.EstimateId,
                EstimatedTimeOfArrival = Google.Protobuf.WellKnownTypes.Timestamp.FromDateTime(DateTime.Now.AddSeconds(ride.Pickup.Eta)),
                RiderOnBoard = ride.Status == "in_progress",
                Price = new CurrencyModel {
                    Price = (double)cacheEstimate.EstimateInfo.Price,
                    Currency = cacheEstimate.EstimateInfo.Currency
                },
                Driver = null,
                RideStage = getStageFromStatus(ride.Status),
                DriverLocation = null,
            };
        }

        public override async Task<CurrencyModel> DeleteRideRequest(DeleteRideRequestModel request, ServerCallContext context)
        {
            var SessionToken = "" + _httpContextAccessor.HttpContext.Request.Headers["token"];

            _logger.LogInformation($"[UberClient::RequestsService::DeleteRideRequest] HTTP Context session token: {SessionToken}");

            var cacheEstimate = await _cache.GetAsync<EstimateCache> (request.RideId);
            string accessToken = await _accessTokenService.GetAccessTokenAsync(SessionToken, cacheEstimate.ProductId.ToString());

            // Create new API client (since it doesn't seem to allow dynamic loading of credentials)
            _requestsApiClient.Configuration = new Configuration {
                AccessToken = accessToken,
            };
            // Get ride with parameters
            await _requestsApiClient.DeleteRequestsAsync(cacheEstimate.RequestId.ToString());
            // Create new API client (since it doesn't seem to allow dynamic loading of credentials)
            _productsApiClient.Configuration = new Configuration {
                AccessToken = accessToken,
            };
            var product = await _productsApiClient.ProductProductIdAsync(cacheEstimate.ProductId.ToString());
            return new CurrencyModel {
                Price = (double)product.PriceDetails.CancellationFee,
                Currency = product.PriceDetails.CurrencyCode
            };
        }

        private Stage getStageFromStatus(string status) {
            switch (status) {
                case "processing":
                    return Stage.Pending;
                case "accepted":
                    return Stage.Accepted;
                case "no drivers available":
                    return Stage.Cancelled;
                case "completed":
                    return Stage.Completed;
                default:
                    return Stage.Unknown;
            }
        }
    }
}
