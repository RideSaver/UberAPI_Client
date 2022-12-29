using Grpc.Core;
using InternalAPI;
using Microsoft.Extensions.Caching.Distributed;

using UberClient.Server.Extensions.Cache;
using UberClient.Models;

using RequestsApi = UberAPI.Client.Api.RequestsApi;
using ProductsApi = UberAPI.Client.Api.ProductsApi;
using Configuration = UberAPI.Client.Client.Configuration;
using System.Net.Http;


namespace UberClient.Services
{
    // Summary: Handles all requests for estimates
    public class EstimatesService : Estimates.EstimatesBase
    {
        private readonly ILogger<EstimatesService> _logger;
        private readonly IHttpClientFactory _clientFactory;
        private readonly IDistributedCache _cache;
        private readonly IAccessTokenService _accessTokenService;

        private readonly RequestsApi _requestsApiClient;
        private readonly ProductsApi _productsApiClient;
        private readonly HttpClient _httpClient;

        public EstimatesService(ILogger<EstimatesService> logger, IDistributedCache cache, IHttpClientFactory clientFactory, IAccessTokenService accessTokenService)
        {
            _clientFactory= clientFactory;
            _httpClient = _clientFactory.CreateClient();
            _accessTokenService = accessTokenService;

            _logger = logger;
            _cache = cache;

            _requestsApiClient = new RequestsApi(_httpClient, new Configuration { });
            _productsApiClient = new ProductsApi(_httpClient, new Configuration {});
        }
        public override async Task GetEstimates(GetEstimatesRequest request, IServerStreamWriter<EstimateModel> responseStream, ServerCallContext context)
        {
            var SessionToken = context.AuthContext.FindPropertiesByName("token").Value;

            _logger.LogInformation($"[UberClient::EstimatesService::GetEstimates] HTTP Context session token: {SessionToken}");

            string clientId = "al0I63Gjwk3Wsmhq_EL8_HxB8qWlO7yY";
            DistributedCacheEntryOptions options = new DistributedCacheEntryOptions() { AbsoluteExpirationRelativeToNow = TimeSpan.FromHours(24)};
            // Loop through all the services in the request
            foreach (var service in request.Services)
            {
                _requestsApiClient.Configuration = new Configuration { AccessToken = await _accessTokenService.GetAccessTokenAsync(SessionToken, service) };

                // Get estimate with parameters
                var estimate = EstimateInfo.FromEstimateResponse(await _requestsApiClient.RequestsEstimateAsync(new UberAPI.Client.Model.RequestsEstimateRequest
                {
                    StartLatitude = (decimal)request.StartPoint.Latitude,
                    StartLongitude = (decimal)request.StartPoint.Longitude,
                    EndLatitude = (decimal)request.EndPoint.Latitude,
                    EndLongitude = (decimal)request.EndPoint.Longitude,
                    SeatCount = request.Seats,
                    ProductId = service
                }));

                var EstimateId = DataAccess.Services.ServiceID.CreateServiceID(service);

                _productsApiClient.Configuration = new Configuration { AccessToken = await _accessTokenService.GetAccessTokenAsync(SessionToken, service) };

                var product = await _productsApiClient.ProductProductIdAsync(service);

                // Write an InternalAPI model back
                var estimateModel = new EstimateModel()
                {
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

                await _cache.SetAsync(EstimateId.ToString(), new EstimateCache
                {
                    EstimateInfo = estimate,
                    GetEstimatesRequest = request,
                    ProductId = Guid.Parse(service)
                }, options);

                await responseStream.WriteAsync(estimateModel);
            }
        }

        public override async Task<EstimateModel> GetEstimateRefresh(GetEstimateRefreshRequest request, ServerCallContext context)
        {
            var SessionToken = context.AuthContext.FindPropertiesByName("token").ToString();

            _logger.LogInformation($"[UberClient::EstimatesService::GetEstimateRefresh] HTTP Context session token : {SessionToken}");

            string clientId = "al0I63Gjwk3Wsmhq_EL8_HxB8qWlO7yY";

            DistributedCacheEntryOptions options = new DistributedCacheEntryOptions() { AbsoluteExpirationRelativeToNow = TimeSpan.FromHours(24) };

            EstimateCache prevEstimate = await _cache.GetAsync<EstimateCache>(request.EstimateId);
            var oldRequest = prevEstimate.GetEstimatesRequest;
            string service = prevEstimate.ProductId.ToString();

            // Get estimate with parameters
            _requestsApiClient.Configuration = new Configuration { AccessToken = await _accessTokenService.GetAccessTokenAsync(SessionToken, service) };

            var estimate = EstimateInfo.FromEstimateResponse(await _requestsApiClient.RequestsEstimateAsync(new UberAPI.Client.Model.RequestsEstimateRequest()
            {
                StartLatitude = (decimal)oldRequest.StartPoint.Latitude,
                StartLongitude = (decimal)oldRequest.StartPoint.Longitude,
                EndLatitude = (decimal)oldRequest.EndPoint.Latitude,
                EndLongitude = (decimal)oldRequest.EndPoint.Longitude,
                SeatCount = oldRequest.Seats,
                ProductId = service
            }));

            var EstimateId = DataAccess.Services.ServiceID.CreateServiceID(service);

            _productsApiClient.Configuration = new UberAPI.Client.Client.Configuration { AccessToken = await _accessTokenService.GetAccessTokenAsync(SessionToken, service) };

            var product = await _productsApiClient.ProductProductIdAsync(service);

            // Write an InternalAPI model back
            var estimateModel = new EstimateModel()
            {
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

            await _cache.SetAsync(EstimateId.ToString(), new EstimateCache
            {
                EstimateInfo = estimate,
                GetEstimatesRequest = oldRequest,
                ProductId = prevEstimate.ProductId
            }, options);

            return estimateModel;
        }
    }
}
