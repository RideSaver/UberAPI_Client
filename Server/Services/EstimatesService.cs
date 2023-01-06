using Grpc.Core;
using InternalAPI;
using Microsoft.Extensions.Caching.Distributed;
using UberClient.Models;
using UberClient.Extensions;
using UberClient.Interface;
using DataAccess.Services;
using UberAPI.Client.Model;
using System.Net.Http;

using RequestsApi = UberAPI.Client.Api.RequestsApi;
using ProductsApi = UberAPI.Client.Api.ProductsApi;
using Configuration = UberAPI.Client.Client.Configuration;
using Newtonsoft.Json;

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

            //----------------------------------------------------------[DEBUG]---------------------------------------------------------------//
            _logger.LogInformation($"[UberClient::EstimatesService::GetEstimates] HTTP Context session token: {SessionToken}");
            foreach (var service in request.Services) { _logger.LogInformation($"[UberClient::EstimatesService::GetEstimates] ServiceID: {service}"); }
            //--------------------------------------------------------------------------------------------------------------------------------//

            DistributedCacheEntryOptions options = new() { AbsoluteExpirationRelativeToNow = TimeSpan.FromHours(24)};

            foreach (var service in request.Services)
            {
                if (service is null) continue;

                _requestsApiClient.Configuration = new Configuration { AccessToken = await _accessTokenService.GetAccessTokenAsync(SessionToken, service.ToString()) };

                if (_requestsApiClient.Configuration.AccessToken is null)
                {
                    _logger.LogInformation("[UberClient::EstimatesService::GetEstimates] AccessToken is null.");
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

                _logger.LogInformation($"[UberClient::EstimatesService::GetEstimates] Instance being sent to MockAPI: {requestInstance}");

                var estimateResponse = EstimateInfo.FromEstimateResponse(await _requestsApiClient.RequestsEstimateAsync(requestInstance));

                _logger.LogInformation($"[UberClient::EstimatesService::GetEstimates] Instance receieved (EstimateInfo) from MockAPI: {estimateResponse}");

                var estimateResponseId = ServiceID.CreateServiceID(service).ToString();

                _logger.LogInformation($"[UberClient::EstimatesService::GetEstimates] Generated Estimate ID: {estimateResponseId}");

                _productsApiClient.Configuration = new Configuration { AccessToken = await _accessTokenService.GetAccessTokenAsync(SessionToken, service) };

                var product = await _productsApiClient.ProductProductIdAsync(requestInstance.ProductId);

                if(product is null) { _logger.LogInformation("[UberClient::EstimatesService::GetEstimates] Product Instance is null!"); }

                _logger.LogInformation($"[UberClient::EstimatesService::GetEstimates] Instance receieved (Product) from MockAPI:\n{product}");

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
                };

                estimateModel.WayPoints.Add(request.StartPoint);
                estimateModel.WayPoints.Add(request.EndPoint);

                await _cache.SetAsync(estimateResponseId, new EstimateCache
                {
                    EstimateInfo = estimateResponse,
                    GetEstimatesRequest = request,
                    ProductId = Guid.Parse(estimateResponseId)
                }, options);

                await responseStream.WriteAsync(estimateModel);
            }
        }

        public override async Task<EstimateModel> GetEstimateRefresh(GetEstimateRefreshRequest request, ServerCallContext context)
        {
            var SessionToken = "" + _httpContextAccessor.HttpContext!.Request.Headers["token"];
            string clientId = "al0I63Gjwk3Wsmhq_EL8_HxB8qWlO7yY";

            _logger.LogInformation($"[UberClient::EstimatesService::GetEstimateRefresh] HTTP Context session token : {SessionToken}");

            DistributedCacheEntryOptions options = new DistributedCacheEntryOptions() { AbsoluteExpirationRelativeToNow = TimeSpan.FromHours(24) };

            EstimateCache prevEstimate = await _cache.GetAsync<EstimateCache>(request.EstimateId);

            var oldRequest = prevEstimate.GetEstimatesRequest;
            string service = prevEstimate.ProductId.ToString();

            _requestsApiClient.Configuration = new Configuration { AccessToken = await _accessTokenService.GetAccessTokenAsync(SessionToken, service) };

            var requestInstance = new RequestsEstimateRequest()
            {
                StartLatitude = (decimal)oldRequest!.StartPoint.Latitude,
                StartLongitude = (decimal)oldRequest.StartPoint.Longitude,
                EndLatitude = (decimal)oldRequest.EndPoint.Latitude,
                EndLongitude = (decimal)oldRequest.EndPoint.Longitude,
                SeatCount = oldRequest.Seats,
                ProductId = service
            };

            var estimateResponse = EstimateInfo.FromEstimateResponse(await _requestsApiClient.RequestsEstimateAsync(requestInstance));
            var estimateResponseId = ServiceID.CreateServiceID(service).ToString();

            _productsApiClient.Configuration = new Configuration { AccessToken = await _accessTokenService.GetAccessTokenAsync(SessionToken, service) };

            var product = await _productsApiClient.ProductProductIdAsync(requestInstance.ProductId);

            if (product is null) { _logger.LogInformation("[UberClient::EstimatesService::GetEstimateRefresh] Product Instance is null!"); }

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
                Seats = product!.Shared ? oldRequest.Seats : product.Capacity,
                RequestUrl = $"https://uber.mock/client_id={clientId}&action=setPickup&pickup[latitude]={oldRequest.StartPoint.Latitude}&pickup[longitude]={oldRequest.StartPoint.Longitude}&dropoff[latitude]={oldRequest.EndPoint.Latitude}&dropoff[longitude]={oldRequest.EndPoint.Longitude}&product_id={requestInstance.ProductId}",
                DisplayName = product.DisplayName,
            };

            estimateModel.WayPoints.Add(oldRequest.StartPoint);
            estimateModel.WayPoints.Add(oldRequest.EndPoint);

            await _cache.SetAsync(estimateResponseId, new EstimateCache
            {
                EstimateInfo = estimateResponse,
                GetEstimatesRequest = oldRequest,
                ProductId = prevEstimate.ProductId
            }, options);

            return estimateModel;
        }
    }
}
