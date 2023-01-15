using Grpc.Core;
using InternalAPI;
using Microsoft.Extensions.Caching.Distributed;
using UberClient.Models;
using UberClient.Extensions;
using UberClient.Interface;

using RequestsApi = UberAPI.Client.Api.RequestsApi;
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

        public RequestsService(ILogger<RequestsService> logger, IDistributedCache cache, IAccessTokenService accessTokenService, IHttpContextAccessor httpContextAccessor)
        {
            _accessTokenService = accessTokenService;
            _httpContextAccessor = httpContextAccessor;
            _logger = logger;
            _cache = cache;

            _requestsApiClient = new RequestsApi();
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

            // Create the RedisCacheEntry configuration options.
            var redisOptions = new DistributedCacheEntryOptions() { AbsoluteExpirationRelativeToNow = TimeSpan.FromHours(24)};
            // Retrieve the user-access token from IdentityService for the current user.
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
            await _cache.SetAsync(estimateId, cacheEstimate, redisOptions);

            // Create a new RideModel instance with the data we recieved to send it back to the RequestsAPI.
            return new RideModel()
            {
                RideId = estimateId,
                EstimatedTimeOfArrival = Google.Protobuf.WellKnownTypes.Timestamp.FromDateTime((DateTime.Now.AddSeconds(responseInstance.Pickup.Eta)).ToUniversalTime()),
                RideStage = getStageFromStatus(responseInstance.Status),
                RiderOnBoard = false,
                Driver = new DriverModel()
                {
                    DisplayName = responseInstance.Drivers.Name,
                    LicensePlate = responseInstance.Vehicle.LicensePlate,
                    CarPicture = responseInstance.Vehicle.PictureUrl,
                    CarDescription = $"{ responseInstance.Vehicle.Make } { responseInstance.Vehicle.Model }",
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
            // Extract JWT token from the requst headers.
            string SessionToken = "" + _httpContextAccessor.HttpContext!.Request.Headers["token"];
            if(SessionToken is null) { throw new ArgumentNullException(nameof(SessionToken)); }

            // RideID used as the cache-instance key.
            string estimateCacheId = request.RideId.ToString();
            if(estimateCacheId is null) { throw new ArgumentNullException(nameof(estimateCacheId)); }

            // Get the cache instance for the ride-request.
            var cacheEstimate = await _cache.GetAsync<EstimateCache>(estimateCacheId);
            if (cacheEstimate is null) { throw new ArgumentNullException(nameof(cacheEstimate)); }
            string serviceID = cacheEstimate.ProductId.ToString();
            string requestID = cacheEstimate.RequestId.ToString();

            // Retrieve the user-access-token from IdentityService for the current user.
            _requestsApiClient.Configuration = new Configuration { AccessToken = await _accessTokenService.GetAccessTokenAsync(SessionToken, serviceID) };

            // Make the request to the MockAPI.
            var responseInstance = await _requestsApiClient.RequestRequestIdAsync(requestID);
            if (responseInstance is null) { throw new ArgumentNullException(nameof(responseInstance)); }

            // Write an InternalAPI model back
            return new RideModel()
            {
                RideId = estimateCacheId,
                EstimatedTimeOfArrival = Google.Protobuf.WellKnownTypes.Timestamp.FromDateTime((DateTime.UtcNow.AddSeconds(responseInstance.Pickup.Eta)).ToUniversalTime()),
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
                    Longitude = responseInstance.Location.Longitude,
                    Height = 0f,
                    Planet = "Earth"
                },
            };
        }
        public override async Task<CurrencyModel> DeleteRideRequest(DeleteRideRequestModel request, ServerCallContext context)
        {
            // Extract the JWT token from the request headers.
            var SessionToken = "" + _httpContextAccessor.HttpContext!.Request.Headers["token"];
            if (SessionToken is null) { throw new ArgumentNullException(nameof(SessionToken)); }

            // RideID used as the cache-instance key
            var estimateCacheId = request.RideId.ToString();
            if (estimateCacheId is null) { throw new ArgumentNullException(nameof(estimateCacheId)); }

            // Get the cache-instance for the ride-request
            var cacheEstimate = await _cache.GetAsync<EstimateCache> (estimateCacheId);
            if (cacheEstimate is null) { throw new ArgumentNullException(nameof(cacheEstimate)); }
            var serviceID = cacheEstimate.ProductId.ToString();
            var requestID = cacheEstimate.RequestId.ToString();

            // Retrieve the user-access-token from IdentityService for the current user.
            _requestsApiClient.Configuration = new Configuration { AccessToken = await _accessTokenService.GetAccessTokenAsync(SessionToken!, serviceID) };

            // Make the Delete request to the MockAPI
            await _requestsApiClient.DeleteRequestsAsync(requestID);

            // Return the cancellation-fee price breakdown saved in the cache.
            if(cacheEstimate.CancellationCost is null) { throw new ArgumentNullException(nameof(cacheEstimate.CancellationCost)); }
            return cacheEstimate.CancellationCost;
        }

        private Stage getStageFromStatus(string status)
        {
            return status switch
            {
                "processing" => Stage.Pending,
                "accepted" => Stage.Accepted,
                "no drivers available" => Stage.Cancelled,
                "completed" => Stage.Completed,
                _ => Stage.Unknown,
            };
        }
    }
}
