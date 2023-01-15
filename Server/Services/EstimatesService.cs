using Grpc.Core;
using InternalAPI;
using Microsoft.Extensions.Caching.Distributed;
using UberClient.Models;
using UberClient.Extensions;
using UberClient.Interface;
using DataAccess.Services;
using UberAPI.Client.Model;

using RequestsApi = UberAPI.Client.Api.RequestsApi;
using ProductsApi = UberAPI.Client.Api.ProductsApi;
using Configuration = UberAPI.Client.Client.Configuration;

namespace UberClient.Services
{
    // Summary: Handles all requests for estimates
    public class EstimatesService : Estimates.EstimatesBase
    {
        private readonly ILogger<EstimatesService> _logger;
        private readonly IDistributedCache _cache;
        private readonly IAccessTokenService _accessTokenService;
        private readonly IHttpContextAccessor _httpContextAccessor;

        private readonly RequestsApi _requestsApiClient;
        private readonly ProductsApi _productsApiClient;

        public EstimatesService(ILogger<EstimatesService> logger, IDistributedCache cache, IAccessTokenService accessTokenService, IHttpContextAccessor httpContextAccessor)
        {
            _accessTokenService = accessTokenService;
            _httpContextAccessor = httpContextAccessor;
            _logger = logger;
            _cache = cache;

            _requestsApiClient = new RequestsApi();
            _productsApiClient = new ProductsApi();
        }
        public override async Task GetEstimates(GetEstimatesRequest request, IServerStreamWriter<EstimateModel> responseStream, ServerCallContext context)
        {
            string clientId = "al0I63Gjwk3Wsmhq_EL8_HxB8qWlO7yY";
            var SessionToken = "" + _httpContextAccessor.HttpContext!.Request.Headers["token"];
            var servicesList = request.Services.ToList();

            DistributedCacheEntryOptions options = new() { AbsoluteExpirationRelativeToNow = TimeSpan.FromHours(24) , SlidingExpiration = TimeSpan.FromHours(5) };

            foreach (var service in servicesList)
            {
                ServiceLinker.ServiceIDs.TryGetValue(service, out string? serviceName);
                if (serviceName is null) continue;

                _requestsApiClient.Configuration = new Configuration { AccessToken = await _accessTokenService.GetAccessTokenAsync(SessionToken, service.ToString()) };

                if (_requestsApiClient.Configuration.AccessToken is null)
                {
                    _logger.LogError("[UberClient::EstimatesService::GetEstimates] AccessToken is NULL.");
                    continue;
                }

                RequestsEstimateRequest requestInstance = new()
                {
                    ProductId = service.ToString(),
                    StartLatitude = (decimal)request.StartPoint.Latitude,
                    StartLongitude = (decimal)request.StartPoint.Longitude,
                    StartPlaceId = "START",
                    EndLatitude = (decimal)request.EndPoint.Latitude,
                    EndLongitude = (decimal)request.EndPoint.Longitude,
                    EndPlaceId = "END",
                    SeatCount = request.Seats
                };

                var estimateResponse = EstimateInfo.FromEstimateResponse(await _requestsApiClient.RequestsEstimateAsync(requestInstance));
                var estimateResponseId = ServiceID.CreateServiceID(service).ToString();

                _productsApiClient.Configuration = new Configuration { AccessToken = await _accessTokenService.GetAccessTokenAsync(SessionToken, service) };

                var product = await _productsApiClient.ProductProductIdAsync(requestInstance.ProductId);

                if (product is null) { throw new ArgumentNullException($"[UberClient::EstimatesService::GetEstimates] {nameof(product)}"); }

                // Write an InternalAPI model back
                var estimateModel = new EstimateModel()
                {
                    EstimateId = estimateResponseId,
                    CreatedTime = Google.Protobuf.WellKnownTypes.Timestamp.FromDateTime(DateTime.Now.ToUniversalTime()),
                    InvalidTime = Google.Protobuf.WellKnownTypes.Timestamp.FromDateTime(DateTime.Now.AddMinutes(5).ToUniversalTime()),
                    PriceDetails = new CurrencyModel
                    {
                        Price = (double)estimateResponse.Price,
                        Currency = estimateResponse.Currency,
                    },
                    Distance = estimateResponse.Distance,
                    Seats = product!.Shared ? request.Seats : product.Capacity,
                    RequestUrl = $"https://uber.mock/client_id={clientId}&action=setPickup&pickup[latitude]={request.StartPoint.Latitude}&pickup[longitude]={request.StartPoint.Longitude}&dropoff[latitude]={request.EndPoint.Latitude}&dropoff[longitude]={request.EndPoint.Longitude}&product_id={requestInstance.ProductId}",
                    DisplayName = product.DisplayName,
                    WayPoints = { { request.StartPoint }, { request.EndPoint } }
                };

                var cacheInstance = new EstimateCache()
                {
                    EstimateInfo = estimateResponse,
                    GetEstimatesRequest = request,
                    ProductId = Guid.Parse(estimateResponseId)
                };

                await _cache.SetAsync(estimateResponseId, cacheInstance, options);
                await responseStream.WriteAsync(estimateModel);
                await Task.Delay(TimeSpan.FromSeconds(1));
            }
        }

        public override async Task<EstimateModel> GetEstimateRefresh(GetEstimateRefreshRequest request, ServerCallContext context)
        {
            string clientId = "al0I63Gjwk3Wsmhq_EL8_HxB8qWlO7yY";
            var SessionToken = "" + _httpContextAccessor.HttpContext!.Request.Headers["token"];
            EstimateCache? estimateCache = await _cache.GetAsync<EstimateCache>(request.EstimateId.ToString());
            var estimateInstance = estimateCache!.GetEstimatesRequest;
            var serviceID = estimateCache.ProductId.ToString();

            if (estimateCache is null) { throw new ArgumentNullException($"[UberClient::EstimatesService::GetEstimateRefresh] {nameof(estimateCache)}"); }

            DistributedCacheEntryOptions options = new() { AbsoluteExpirationRelativeToNow = TimeSpan.FromHours(24), SlidingExpiration = TimeSpan.FromHours(5) };
            _requestsApiClient.Configuration = new Configuration { AccessToken = await _accessTokenService.GetAccessTokenAsync(SessionToken, serviceID) };

            var requestInstance = new RequestsEstimateRequest()
            {
                StartLatitude = (decimal)estimateInstance!.StartPoint.Latitude,
                StartLongitude = (decimal)estimateInstance.StartPoint.Longitude,
                StartPlaceId = "START",
                EndLatitude = (decimal)estimateInstance.EndPoint.Latitude,
                EndLongitude = (decimal)estimateInstance.EndPoint.Longitude,
                SeatCount = estimateInstance.Seats,
                EndPlaceId = "END",
                ProductId = serviceID
            };

            var estimateResponse = EstimateInfo.FromEstimateResponse(await _requestsApiClient.RequestsEstimateAsync(requestInstance));
            var estimateResponseId = ServiceID.CreateServiceID(serviceID).ToString();
            estimateResponse.FareId = Guid.NewGuid().ToString();

            _productsApiClient.Configuration = new Configuration { AccessToken = await _accessTokenService.GetAccessTokenAsync(SessionToken, serviceID) };

            var product = await _productsApiClient.ProductProductIdAsync(serviceID);

            if (product is null) { throw new ArgumentNullException($"[UberClient::EstimatesService::GetEstimateRefresh] {nameof(product)}"); }

            var estimateModel = new EstimateModel()
            {
                EstimateId = estimateResponseId,
                CreatedTime = Google.Protobuf.WellKnownTypes.Timestamp.FromDateTime(DateTime.Now.ToUniversalTime()),
                InvalidTime = Google.Protobuf.WellKnownTypes.Timestamp.FromDateTime(DateTime.Now.AddMinutes(5).ToUniversalTime()),
                Distance = estimateResponse.Distance,
                Seats = product!.Shared ? estimateInstance.Seats : product.Capacity,
                DisplayName = product.DisplayName,
                WayPoints = { { estimateInstance.StartPoint }, { estimateInstance.EndPoint } },
                PriceDetails = new CurrencyModel
                {
                    Price = (double)estimateResponse.Price,
                    Currency = estimateResponse.Currency,
                },
                RequestUrl = $"https://uber.mock/client_id={clientId}&action=setPickup&pickup[latitude]={estimateInstance.StartPoint.Latitude}&pickup[longitude]={estimateInstance.StartPoint.Longitude}&dropoff[latitude]={estimateInstance.EndPoint.Latitude}&dropoff[longitude]={estimateInstance.EndPoint.Longitude}&product_id={serviceID}"
            };

            var cacheInstance = new EstimateCache()
            {
                EstimateInfo = estimateResponse,
                GetEstimatesRequest = estimateInstance,
                ProductId = Guid.Parse(serviceID)
            };

            await _cache.SetAsync(estimateResponseId, cacheInstance, options);
            return estimateModel;
        }
    }
}
