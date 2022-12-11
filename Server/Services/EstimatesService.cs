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
using DataAccess;
using UberClient.Repository;

using RequestsApi = UberAPI.Client.Api.RequestsApi;
using ProductsApi = UberAPI.Client.Api.ProductsApi;
using Configuration = UberAPI.Client.Client.Configuration;

//! A Estimates Service class. 
/*!
 * Uber Client that sends a request to the Estimate Service via TCP port protocol, then retrieves and converts the information in gRPC.
 */
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
        private RequestsApi _apiClient;

        // Summary: our API client, so we only open up some ports, rather than swamping the system.
        private ProductsApi _productsApiClient;

        // Summary: Our cache object
        private readonly IDistributedCache _cache;

        // Summary: Our Access Token Controller
        private readonly IAccessTokenController _accessController;

        public EstimatesService(ILogger<EstimatesService> logger, IDistributedCache cache, IHttpClientInstance httpClient, IAccessTokenController accessContoller)
        {
            _httpClient = httpClient;
            _logger = logger;
            _cache = cache;
            _apiClient = new RequestsApi(httpClient.APIClientInstance, new Configuration {});
            _productsApiClient = new ProductsApi(httpClient.APIClientInstance, new Configuration {});
            _accessController = accessContoller;
        }
        public override async Task GetEstimates(GetEstimatesRequest request, IServerStreamWriter<EstimateModel> responseStream, ServerCallContext context)
        {
            var SessionToken = context.AuthContext.PeerIdentityPropertyName;
            _logger.LogInformation("HTTP Context User: {User}", SessionToken);

            string clientId = "al0I63Gjwk3Wsmhq_EL8_HxB8qWlO7yY";
            DistributedCacheEntryOptions options = new DistributedCacheEntryOptions() { AbsoluteExpirationRelativeToNow = TimeSpan.FromHours(24)};
            // Loop through all the services in the request
            foreach (var service in request.Services)
            {
                _apiClient.Configuration = new Configuration {
                    AccessToken = await _accessController.GetAccessToken(SessionToken, service),
                };
                // Get estimate with parameters
                var estimate = EstimateInfo.FromEstimateResponse(await _apiClient.RequestsEstimateAsync(new UberAPI.Client.Model.RequestsEstimateRequest
                {
                    StartLatitude = (decimal)request.StartPoint.Latitude,
                    StartLongitude = (decimal)request.StartPoint.Longitude,
                    EndLatitude = (decimal)request.EndPoint.Latitude,
                    EndLongitude = (decimal)request.EndPoint.Longitude,
                    SeatCount = request.Seats,
                    ProductId = service
                }));
                var EstimateId = DataAccess.Services.ServiceID.CreateServiceID(service); 
                _productsApiClient.Configuration = new Configuration {
                    AccessToken = await _accessController.GetAccessToken(SessionToken, service),
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

        public override async Task<EstimateModel> GetEstimateRefresh(GetEstimateRefreshRequest request, ServerCallContext context)
        {
            var SessionToken = context.AuthContext.PeerIdentityPropertyName;
            _logger.LogInformation("HTTP Context User: {User}", SessionToken);

            string clientId = "al0I63Gjwk3Wsmhq_EL8_HxB8qWlO7yY";
            DistributedCacheEntryOptions options = new DistributedCacheEntryOptions() { AbsoluteExpirationRelativeToNow = TimeSpan.FromHours(24)};

            EstimateCache prevEstimate = await _cache.GetAsync<EstimateCache>(request.EstimateId);
            var oldRequest = prevEstimate.GetEstimatesRequest;
            string service = prevEstimate.ProductId.ToString();
            // Get estimate with parameters
            _apiClient.Configuration = new Configuration {
                AccessToken = await _accessController.GetAccessToken(SessionToken, service),
            };
            var estimate = EstimateInfo.FromEstimateResponse(await _apiClient.RequestsEstimateAsync(new UberAPI.Client.Model.RequestsEstimateRequest()
            {
                StartLatitude = (decimal)oldRequest.StartPoint.Latitude,
                StartLongitude = (decimal)oldRequest.StartPoint.Longitude,
                EndLatitude = (decimal)oldRequest.EndPoint.Latitude,
                EndLongitude = (decimal)oldRequest.EndPoint.Longitude,
                SeatCount = oldRequest.Seats,
                ProductId = service
            }));
            var EstimateId = DataAccess.Services.ServiceID.CreateServiceID(service); 
            _productsApiClient.Configuration = new UberAPI.Client.Client.Configuration {
                AccessToken = await _accessController.GetAccessToken(SessionToken, service),
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
                Seats = product.Shared ? oldRequest.Seats : product.Capacity,
                RequestUrl = $"https://m.uber.com/ul/?client_id={clientId}&action=setPickup&pickup[latitude]={oldRequest.StartPoint.Latitude}&pickup[longitude]={oldRequest.StartPoint.Longitude}&dropoff[latitude]={oldRequest.EndPoint.Latitude}&dropoff[longitude]={oldRequest.EndPoint.Longitude}&product_id={service}",
                DisplayName = product.DisplayName,
            };

            estimateModel.WayPoints.Add(oldRequest.StartPoint);
            estimateModel.WayPoints.Add(oldRequest.EndPoint);

            _=_cache.SetAsync<EstimateCache>(EstimateId.ToString(), new EstimateCache { 
                EstimateInfo = estimate,
                GetEstimatesRequest = oldRequest,
                ProductId = prevEstimate.ProductId
            }, options);

            return estimateModel;
        }
    }
}
