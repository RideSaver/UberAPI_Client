using Grpc.Core;
using InternalAPI;
using Microsoft.AspNetCore.Components.Routing;
using System.ComponentModel;
using UberAPI.Client.Model;
using UberClient.HTTPClient;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.AspNetCore.Authorization;
using System.Text;
using UberClient.Server.Extensions.Cache;
using UberClient.Models;
using DataAccess;

namespace UberClient.Services
{
    // Summary: Handles all requests for estimates
    public class EstimatesService : Estimates.EstimatesBase
    {
        // Summary: our logging object, used for diagnostic logs.
        private readonly ILogger<EstimatesService> _logger;
        // Summary: our API client, so we only open up some ports, rather than swamping the system.
        private readonly IHttpClientInstance _httpClient;

        // Summary: our API client, so we only open up some ports, rather than swamping the system.
        private UberAPI.Client.Api.RequestsApi _apiClient;

        // Summary: our API client, so we only open up some ports, rather than swamping the system.
        private UberAPI.Client.Api.ProductsApi _productsApiClient;

        // Summary: Our cache object
        private readonly IDistributedCache _cache;

        public EstimatesService(ILogger<EstimatesService> logger, IDistributedCache cache, IHttpClientInstance httpClient)
        {
            _httpClient = httpClient;
            _logger = logger;
            _cache = cache;
            _apiClient = new UberAPI.Client.Api.RequestsApi(httpClient.APIClientInstance, new UberAPI.Client.Client.Configuration {});
            _productsApiClient = new UberAPI.Client.Api.ProductsApi(httpClient.APIClientInstance, new UberAPI.Client.Client.Configuration {});
        }
        public override async Task GetEstimates(GetEstimatesRequest request, IServerStreamWriter<EstimateModel> responseStream, ServerCallContext context)
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

            string clientId = "";
            DistributedCacheEntryOptions options = new DistributedCacheEntryOptions() { AbsoluteExpirationRelativeToNow = TimeSpan.FromHours(24)};
            // Loop through all the services in the request
            foreach (var service in request.Services)
            {
                _apiClient.Configuration = new UberAPI.Client.Client.Configuration {
                    AccessToken = AccessToken
                };
                // Get estimate with parameters
                var estimate = EstimateInfo.FromEstimateResponse(await _apiClient.RequestsEstimateAsync(new RequestsEstimateRequest
                {
                    StartLatitude = (decimal)request.StartPoint.Latitude,
                    StartLongitude = (decimal)request.StartPoint.Longitude,
                    EndLatitude = (decimal)request.EndPoint.Latitude,
                    EndLongitude = (decimal)request.EndPoint.Longitude,
                    SeatCount = request.Seats,
                    ProductId = service
                }));
                var EstimateId = DataAccess.Services.ServiceID.CreateServiceID(service); // TODO: Use ID generator function
                _productsApiClient.Configuration = new UberAPI.Client.Client.Configuration {
                    AccessToken = AccessToken
                };
                var product = await _productsApiClient.ProductProductIdAsync(service);
                // Write an InternalAPI model back
                var estimateModel = new EstimateModel() {
                    EstimateId = EstimateId.ToString(),
                    CreatedTime = Google.Protobuf.WellKnownTypes.Timestamp.FromDateTime(DateTime.Now), 
                    PriceDetails = new CurrencyModel
                    {
                        Price = (double)estimate.Price,
                        Currency = estimate.Currency,
                    },
                    Distance = (int)estimate.Distance,
                    Seats = product.Shared ? request.Seats : product.Capacity,
                    RequestUrl = $"https://m.uber.com/ul/?client_id={clientId}&action=setPickup&pickup[latitude]={request.StartPoint.Latitude}&pickup[longitude]={request.StartPoint.Longitude}&dropoff[latitude]={request.EndPoint.Latitude}&dropoff[longitude]={request.EndPoint.Longitude}&product_id={service}",
                    DisplayName = product.DisplayName,
                };

                estimateModel.WayPoints.Add(request.StartPoint);
                estimateModel.WayPoints.Add(request.EndPoint);

                _=_cache.SetAsync<EstimateCache>(EstimateId.ToString(), new EstimateCache { 
                    EstimateInfo = estimate,
                    GetEstimatesRequest = request,
                    ProductId = Guid.Parse(service)
                }, options);

                await responseStream.WriteAsync(estimateModel);
            }
        }

        public override Task<EstimateModel> GetEstimateRefresh(GetEstimateRefreshRequest request, ServerCallContext context)
        {
            var estimateRefresh = new EstimateModel();
            // TBA: Invoke the web-client API to get the information from the uber-api, then send it to the microservice.

            return Task.FromResult(estimateRefresh);
        }
    }
}
