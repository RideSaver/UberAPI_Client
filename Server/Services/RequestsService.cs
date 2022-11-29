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

namespace UberClient.Services
{
    public class RequestsService : Requests.RequestsBase // TBA
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

        public RequestsService(ILogger<RequestsService> logger, IDistributedCache cache, IHttpClientInstance httpClient)
        {
            _httpClient = httpClient;
            _logger = logger;
            _cache = cache;
            _apiClient = new UberAPI.Client.Api.RequestsApi(httpClient.APIClientInstance, new UberAPI.Client.Client.Configuration {});
            _productsApiClient = new UberAPI.Client.Api.ProductsApi(httpClient.APIClientInstance, new UberAPI.Client.Client.Configuration {});
        }

        public override async Task<RideModel> GetRideRequest(GetRideRequestModel request, ServerCallContext context)
        {
            var SessionToken = context.AuthContext.PeerIdentityPropertyName;
            _logger.LogInformation("HTTP Context User: {User}", SessionToken);
            var encodedUserID = await _cache.GetAsync(SessionToken); // TODO: Figure out if this is the correct token

            if (encodedUserID == null)
            {
                throw new NotImplementedException();
            }
            var UserID = Encoding.UTF8.GetString(encodedUserID);

            var AccessToken = UserID; // TODO: Get Access Token From DB

            DistributedCacheEntryOptions options = new DistributedCacheEntryOptions() { AbsoluteExpirationRelativeToNow = TimeSpan.FromHours(24)};

            // Create new API client (since it doesn't seem to allow dynamic loading of credentials)
            _apiClient.Configuration = new UberAPI.Client.Client.Configuration {
                AccessToken = AccessToken
            };
            // Get ride with parameters
            var ride = await _apiClient.RequestRequestIdAsync(request.RideId);
            var cacheEstimate = await _cache.GetAsync<EstimateCache> (request.RideId);
            // Write an InternalAPI model back
            return new RideModel() {
                RideId = "NEW ID GENERATOR",
                EstimatedTimeOfArrival = Google.Protobuf.WellKnownTypes.Timestamp.FromDateTime(DateTime.Now.AddSeconds(ride.Pickup.Eta)),
                RiderOnBoard = ride.Status == "in_progress",
                Price = new CurrencyModel {
                    Price = (double)cacheEstimate.PriceEstimate.HighEstimate,
                    Currency = cacheEstimate.PriceEstimate.CurrencyCode
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
            var encodedUserID = await _cache.GetAsync(SessionToken); // TODO: Figure out if this is the correct token

            if (encodedUserID == null)
            {
                throw new NotImplementedException();
            }
            var UserID = Encoding.UTF8.GetString(encodedUserID);

            var AccessToken = UserID; // TODO: Get Access Token From DB

            DistributedCacheEntryOptions options = new DistributedCacheEntryOptions() { AbsoluteExpirationRelativeToNow = TimeSpan.FromHours(24)};

            // Create new API client (since it doesn't seem to allow dynamic loading of credentials)
            _apiClient.Configuration = new UberAPI.Client.Client.Configuration {
                AccessToken = AccessToken
            };
            var cacheEstimate = await _cache.GetAsync<EstimateCache> (request.EstimateId);
            UberAPI.Client.Model.Requests requests = new UberAPI.Client.Model.Requests(cacheEstimate.PriceEstimate.Fare.FareId) {
                ProductId = cacheEstimate.ProductId.ToString(),
                StartLatitude = (float)cacheEstimate.GetEstimatesRequest.StartPoint.Latitude,
                StartLongitude = (float)cacheEstimate.GetEstimatesRequest.StartPoint.Longitude,
                EndLatitude = (float)cacheEstimate.GetEstimatesRequest.EndPoint.Latitude,
                EndLongitude = (float)cacheEstimate.GetEstimatesRequest.EndPoint.Longitude
            };
            
            var ride = await _apiClient.CreateRequestsAsync(requests);
            cacheEstimate.RequestId = Guid.Parse(ride._RequestId);
            _=_cache.SetAsync<EstimateCache>(request.EstimateId, cacheEstimate, options);
            return new RideModel() {
                RideId = "NEW ID GENERATOR",
                EstimatedTimeOfArrival = Google.Protobuf.WellKnownTypes.Timestamp.FromDateTime(DateTime.Now.AddSeconds(ride.Pickup.Eta)),
                RiderOnBoard = ride.Status == "in_progress",
                Price = new CurrencyModel {
                    Price = (double)cacheEstimate.PriceEstimate.HighEstimate,
                    Currency = cacheEstimate.PriceEstimate.CurrencyCode
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
            var encodedUserID = await _cache.GetAsync(SessionToken); // TODO: Figure out if this is the correct token

            if (encodedUserID == null)
            {
                throw new NotImplementedException();
            }
            var UserID = Encoding.UTF8.GetString(encodedUserID);

            var AccessToken = UserID; // TODO: Get Access Token From DB

            var cacheEstimate = await _cache.GetAsync<EstimateCache> (request.RideId);

            // Create new API client (since it doesn't seem to allow dynamic loading of credentials)
            _apiClient.Configuration = new UberAPI.Client.Client.Configuration {
                AccessToken = AccessToken
            };

            // Get ride with parameters
            await _apiClient.DeleteRequestsAsync(cacheEstimate.RequestId.ToString());

            _productsApiClient.Configuration = new UberAPI.Client.Client.Configuration {
                AccessToken = AccessToken
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
