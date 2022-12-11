using Grpc.Core;
using InternalAPI;
using UberClient.HTTPClient;
using Microsoft.Extensions.Caching.Distributed;

using UberClient.Server.Extensions.Cache;
using UberClient.Models;
using UberClient.Repository;

using RequestsApi = UberAPI.Client.Api.RequestsApi;
using ProductsApi = UberAPI.Client.Api.ProductsApi;
using Configuration = UberAPI.Client.Client.Configuration;


//! A Requests Service class. 
/*!
 * Uber Client that sends a request to the Request Service via TCP port protocol, then retrieves and converts the information in gRPC.
 */
namespace UberClient.Services
{
    public class RequestsService : Requests.RequestsBase
    {
        private readonly ILogger<RequestsService> _logger;
        // Summary: our API client, so we only open up some ports, rather than swamping the system.
        private readonly IHttpClientInstance _httpClient;
        // Summary: our API client, so we only open up some ports, rather than swamping the system.
        private RequestsApi _apiClient;
        // Summary: our API client, so we only open up some ports, rather than swamping the system.
        private ProductsApi _productsApiClient;
        // Summary: Our cache object
        private readonly IDistributedCache _cache;
        // Summary: Our Access Token Controller
        private readonly IAccessTokenController _accessController;

        public RequestsService(ILogger<RequestsService> logger, IDistributedCache cache, IHttpClientInstance httpClient, IAccessTokenController accessContoller)
        {
            _httpClient = httpClient;
            _logger = logger;
            _cache = cache;
            _apiClient = new RequestsApi(httpClient.APIClientInstance, new Configuration {});
            _productsApiClient = new ProductsApi(httpClient.APIClientInstance, new Configuration {});
            _accessController = accessContoller;
        }
        //! public override async member that takes two arguments and returns an Task<RideModel> value.
        /*!
         \param request an GetRideRequestModel argument.
         \param context an ServerCallContext argument.
        */
        public override async Task<RideModel> GetRideRequest(GetRideRequestModel request, ServerCallContext context)
        {
            var SessionToken = context.AuthContext.PeerIdentityPropertyName; /*< \var string SessionToken */
            _logger.LogInformation("HTTP Context User: {User}", SessionToken);

            DistributedCacheEntryOptions options = new DistributedCacheEntryOptions() { AbsoluteExpirationRelativeToNow = TimeSpan.FromHours(24) };
            //! Creating cacheEstimate with parameters.
            /*!
             \var EstimateCache cacheEstimate
             \param request.RideId parameter used to retrieve service.
            */
            var cacheEstimate = await _cache.GetAsync<EstimateCache> (request.RideId);
            // Create new API client (since it doesn't seem to allow dynamic loading of credentials)
            _apiClient.Configuration = new Configuration
            {
                AccessToken = await _accessController.GetAccessToken(SessionToken, cacheEstimate.ProductId.ToString()),
            };
            //! Creating variable ride with parameters.
            var ride = await _apiClient.RequestRequestIdAsync(request.RideId);
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
            var SessionToken = context.AuthContext.PeerIdentityPropertyName;
            _logger.LogInformation("HTTP Context User: {User}", SessionToken);

            DistributedCacheEntryOptions options = new DistributedCacheEntryOptions() { AbsoluteExpirationRelativeToNow = TimeSpan.FromHours(24)};

            var cacheEstimate = await _cache.GetAsync<EstimateCache> (request.EstimateId);
            // Create new API client (since it doesn't seem to allow dynamic loading of credentials)
            _apiClient.Configuration = new Configuration {
                AccessToken = await _accessController.GetAccessToken(SessionToken, cacheEstimate.ProductId.ToString()),
            };
            UberAPI.Client.Model.CreateRequests requests = new UberAPI.Client.Model.CreateRequests() {
                FareId = cacheEstimate.EstimateInfo.FareId,
                ProductId = cacheEstimate.ProductId.ToString(),
                StartLatitude = (float)cacheEstimate.GetEstimatesRequest.StartPoint.Latitude,
                StartLongitude = (float)cacheEstimate.GetEstimatesRequest.StartPoint.Longitude,
                EndLatitude = (float)cacheEstimate.GetEstimatesRequest.EndPoint.Latitude,
                EndLongitude = (float)cacheEstimate.GetEstimatesRequest.EndPoint.Longitude,
            };
            
            var ride = await _apiClient.CreateRequestsAsync(requests);
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
            var SessionToken = context.AuthContext.PeerIdentityPropertyName;
            _logger.LogInformation("HTTP Context User: {User}", SessionToken);

            var cacheEstimate = await _cache.GetAsync<EstimateCache> (request.RideId);
            string accessToken = await _accessController.GetAccessToken(SessionToken, cacheEstimate.ProductId.ToString());

            // Create new API client (since it doesn't seem to allow dynamic loading of credentials)
            _apiClient.Configuration = new Configuration {
                AccessToken = accessToken,
            };
            // Get ride with parameters
            await _apiClient.DeleteRequestsAsync(cacheEstimate.RequestId.ToString());
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
