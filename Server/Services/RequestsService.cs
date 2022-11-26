using Grpc.Core;
using InternalAPI;
using Microsoft.AspNetCore.Components.Routing;
using System.ComponentModel;
using UberClient.HTTPClient;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.AspNetCore.Authorization;
using System.Text;

namespace UberClient.Services
{
    public class RequestsService : Requests.RequestsBase // TBA
    {
        private readonly ILogger<RequestsService> _logger;
        // Summary: our API client, so we only open up some ports, rather than swamping the system.
        private readonly IHttpClientInstance _httpClient;

        // Summary: our API client, so we only open up some ports, rather than swamping the system.
        private UberAPI.Client.Api.RequestsApi _apiClient;

        // Summary: Our cache object
        private readonly IDistributedCache _cache;

        public RequestsService(ILogger<RequestsService> logger, IDistributedCache cache, IHttpClientInstance httpClient)
        {
            _httpClient = httpClient;
            _logger = logger;
            _cache = cache;
            _apiClient = new UberAPI.Client.Api.RequestsApi(httpClient.APIClientInstance, new UberAPI.Client.Client.Configuration {});
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

            // Create new API client (since it doesn't seem to allow dynamic loading of credentials)
            _apiClient.Configuration = new UberAPI.Client.Client.Configuration {
                AccessToken = AccessToken
            };
            // Get ride with parameters
            var ride = await _apiClient.RequestRequestIdGetAsync(request.RideId);
            // Write an InternalAPI model back
            return new RideModel
            {
                // TODO: populate most of this data with data from the estimate.
                RideId = "NEW ID GENERATOR",
                //EstimatedTimeOfArrival,
                RiderOnBoard = ride.Status == "in_progress",
                /*Price = new CurrencyModel
                {
                    Currency = ride.CurrencyCode
                },*/
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

        public override Task<RideModel> PostRideRequest(PostRideRequestModel request, ServerCallContext context)
        {
            var postRide = new RideModel();
            // TBA: Invoke the web-client API to get the information from the uber-api, then send it to the microservice.

            return Task.FromResult(postRide);
        }

        public override Task<CurrencyModel> DeleteRideRequest(DeleteRideRequestModel request, ServerCallContext context) 
        {
            var deleteRide = new CurrencyModel();
            // TBA: Invoke the web-client API to get the information from the uber-api, then send it to the microservice.

            return Task.FromResult(deleteRide);
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
