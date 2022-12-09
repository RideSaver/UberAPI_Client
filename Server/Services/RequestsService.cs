using Grpc.Core;
using InternalAPI;
using Microsoft.AspNetCore.Components.Routing;
using System.ComponentModel;
using UberClient.HTTPClient;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.AspNetCore.Authorization;
using System.Text;
using UberClient.Server.Extensions.Cache;
using UberClient.Models;
using UberClient.Repository;
using DataAccess;

//! A Requests Service class. 
/*!
 * This class handles all requests for rides. It contains call two methods: GetEstimates and GetEstimateRefresh.
 * The GetRideRequest method requests ride resources from the Uber API. Firstly, it receives the user's access token, 
 * requests the EstimateId from cache, and the protocol buffer data to be deserialized into the standard models. 
 * Then, it is serialized into uber models and an authentication token is added. The Uber Client makes a GET request 
 * to the Uber API, which returns an response object that contains the request estimate data. A loop is used for each instance
 * and is added to the EstimateId. Finally, the Uber Client returns the data to the services that requested it. 
 * The PostRideRequest method creates a new ride from the resources GetRideRequest had returned to the service. It functions 
 * like the GetRideRequest, except it updates the existing EstimateId since the ride is being created. The DeleteRideRequests 
 * method deletes the ride requested by the service by following the same process as the GetRideRequest method.
 */
namespace UberClient.Services
{
    public class RequestsService : Requests.RequestsBase
    {
        private readonly ILogger<RequestsService> _logger;
        // Summary: our API client, so we only open up some ports, rather than swamping the system.
        private readonly IHttpClientInstance _httpClient;
        // Summary: our API client, so we only open up some ports, rather than swamping the system.
        private UberAPI.Client.Api.RequestsApi _apiClient;
        // Summary: our API client, so we only open up some ports, rather than swamping the system.
        private UberAPI.Client.Api.ProductsApi _productsApiClient;
        // Summary: Our cache object
        private readonly IDistributedCache _cache;
        // Summary: Our Access Token Controller
        private readonly IAccessTokenController _accessController;

        public RequestsService(ILogger<RequestsService> logger, IDistributedCache cache, IHttpClientInstance httpClient, IAccessTokenController accessContoller)
        {
            _httpClient = httpClient;
            _logger = logger;
            _cache = cache;
            _apiClient = new UberAPI.Client.Api.RequestsApi(httpClient.APIClientInstance, new UberAPI.Client.Client.Configuration {});
            _productsApiClient = new UberAPI.Client.Api.ProductsApi(httpClient.APIClientInstance, new UberAPI.Client.Client.Configuration {});
            _accessController = accessContoller;
        }
        /// @startuml
        /// state "Get Access Token" as AT
        /// state "gRPC call to Uber Client" as Cl
        /// Cl : EstimateId/RequestId
        /// state "Get EstimateId from cache" as GEC
        /// state "Uber Client receives protocol buffer data" as RD
        /// state "Add Authentication Token to Uber Client" as AuthT
        /// state "Make Get Ride request to Uber API" as GR
        /// GR : Request Object as Parameter
        /// state "Uber sends back data of requested ride list" as ReqO
        /// state "Uber Client receives response object" as RO
        /// RO : iterates once through instances and adds to EstimateId
        /// state "Uber Client sends the data to the service" as S
        /// 
        /// [*] -d-> AT
        /// AT -d-> Cl
        /// Cl -d-> GEC
        /// GEC -d-> RD
        /// RD -d-> AuthT : Deserializes to standard model
        /// AuthT -d-> GR : Serializes to uber model
        /// GR -d-> ReqO
        /// ReqO -d-> RO
        /// RO -d-> S : Serialization to protocol buffer data
        /// S -d-> [*]
        /// @enduml
        public override async Task<RideModel> GetRideRequest(GetRideRequestModel request, ServerCallContext context)
        {
            var SessionToken = context.AuthContext.PeerIdentityPropertyName; /*< \var string SessionToken */
            _logger.LogInformation("HTTP Context User: {User}", SessionToken);

            DistributedCacheEntryOptions options = new DistributedCacheEntryOptions() { AbsoluteExpirationRelativeToNow = TimeSpan.FromHours(24) };
            //! Creating cacheEstimate with parameters.
            var cacheEstimate = await _cache.GetAsync<EstimateCache> (request.RideId);
            // Create new API client (since it doesn't seem to allow dynamic loading of credentials)
            _apiClient.Configuration = new UberAPI.Client.Client.Configuration
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
        /// @startuml
        /// state "Get Access Token" as AT
        /// state "gRPC call to Uber Client" as Cl
        /// Cl : EstimateId/RequestId
        /// state "Get EstimateId from cache" as GEC
        /// state "Uber Client receives protocol buffer data" as RD
        /// state "Add Authentication Token to Uber Client" as AuthT
        /// state "Make Post Ride request to Uber API" as PR
        /// PR : Request Object as Parameter
        /// state "Uber sends back data of requested ride list" as ReqO
        /// state "Uber Client receives response object" as RO
        /// RO : iterates once through instances and adds to EstimateId
        /// state "Update EstimateId to cache" as UC
        /// state "Uber Client sends the data to the service" as S
        /// 
        /// [*] -d-> AT
        /// AT -d-> Cl
        /// Cl -d-> GEC
        /// GEC -d-> RD
        /// RD -d-> AuthT : Deserializes to standard model
        /// AuthT -d-> PR : Serializes to uber model
        /// PR -d-> ReqO
        /// ReqO -d-> RO
        /// RO -d-> UC : Serialization to protocol buffer data
        /// UC -d-> S
        /// S -d-> [*]
        /// @enduml
        public override async Task<RideModel> PostRideRequest(PostRideRequestModel request, ServerCallContext context)
        {
            var SessionToken = context.AuthContext.PeerIdentityPropertyName;
            _logger.LogInformation("HTTP Context User: {User}", SessionToken);

            DistributedCacheEntryOptions options = new DistributedCacheEntryOptions() { AbsoluteExpirationRelativeToNow = TimeSpan.FromHours(24)};

            var cacheEstimate = await _cache.GetAsync<EstimateCache> (request.EstimateId);
            // Create new API client (since it doesn't seem to allow dynamic loading of credentials)
            _apiClient.Configuration = new UberAPI.Client.Client.Configuration {
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
        /// @startuml
        /// state "Get Access Token" as AT
        /// state "gRPC call to Uber Client" as Cl
        /// Cl : EstimateId/RequestId
        /// state "Get EstimateId from cache" as GEC
        /// state "Uber Client receives protocol buffer data" as RD
        /// state "Add Authentication Token to Uber Client" as AuthT
        /// state "Make Delete Ride request to Uber API" as DR
        /// DR : Request Object as Parameter
        /// state "Uber sends back data of requested ride list" as ReqO
        /// state "Uber Client receives response object" as RO
        /// RO : iterates once through instances and adds to EstimateId
        /// state "Uber Client sends the data to the service" as S
        /// 
        /// [*] -d-> AT
        /// AT -d-> Cl
        /// Cl -d-> GEC
        /// GEC -d-> RD
        /// RD -d-> AuthT : Deserializes to standard model
        /// AuthT -d-> DR : Serializes to uber model
        /// DR -d-> ReqO
        /// ReqO -d-> RO
        /// RO -d-> S : Serialization to protocol buffer data
        /// S -d-> [*]
        /// @enduml
        public override async Task<CurrencyModel> DeleteRideRequest(DeleteRideRequestModel request, ServerCallContext context) 
        {
            var SessionToken = context.AuthContext.PeerIdentityPropertyName;
            _logger.LogInformation("HTTP Context User: {User}", SessionToken);

            var cacheEstimate = await _cache.GetAsync<EstimateCache> (request.RideId);
            string accessToken = await _accessController.GetAccessToken(SessionToken, cacheEstimate.ProductId.ToString());

            // Create new API client (since it doesn't seem to allow dynamic loading of credentials)
            _apiClient.Configuration = new UberAPI.Client.Client.Configuration {
                AccessToken = accessToken,
            };
            // Get ride with parameters
            await _apiClient.DeleteRequestsAsync(cacheEstimate.RequestId.ToString());
            // Create new API client (since it doesn't seem to allow dynamic loading of credentials)
            _productsApiClient.Configuration = new UberAPI.Client.Client.Configuration {
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
