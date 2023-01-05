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
        private readonly IHttpClientFactory _clientFactory;
        private readonly IDistributedCache _cache;
        private readonly IAccessTokenService _accessTokenService;
        private readonly IHttpContextAccessor _httpContextAccessor;

        private readonly RequestsApi _requestsApiClient;
        private readonly ProductsApi _productsApiClient;
        private readonly HttpClient _httpClient;

        public EstimatesService(ILogger<EstimatesService> logger, IDistributedCache cache, IHttpClientFactory clientFactory, IAccessTokenService accessTokenService, IHttpContextAccessor httpContextAccessor)
        {
            _clientFactory= clientFactory;
            _httpClient = _clientFactory.CreateClient();
            _accessTokenService = accessTokenService;
            _httpContextAccessor = httpContextAccessor;

            _logger = logger;
            _cache = cache;

            _requestsApiClient = new RequestsApi();
            _productsApiClient = new ProductsApi();
        }
        public override async Task GetEstimates(GetEstimatesRequest request, IServerStreamWriter<EstimateModel> responseStream, ServerCallContext context)
        {
            var SessionToken = "" + _httpContextAccessor.HttpContext!.Request.Headers["token"];

            //----------------------------------------------------------[DEBUG]---------------------------------------------------------------//
            _logger.LogInformation($"[UberClient::EstimatesService::GetEstimates] HTTP Context session token: {SessionToken}");
            _logger.LogInformation($"[UberClient::EstimatesService::GetEstimates] Request: START: {request.StartPoint} END: {request.EndPoint}");

            foreach (var service in request.Services)
            {
                _logger.LogInformation($"[UberClient::EstimatesService::GetEstimates] Request: SERVICE ID: {service}");
                _logger.LogInformation($"[UberClient::EstimatesService::GetEstimates] Request: SERVICE ID (ToString): {service.ToString().Replace("-", string.Empty)}");
            }
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
                    ProductId = service.ToString().Replace("-", string.Empty),
                    StartLatitude = (decimal)request.StartPoint.Latitude,
                    StartLongitude = (decimal)request.StartPoint.Longitude,
                    StartPlaceId = "START",
                    EndLatitude = (decimal)request.EndPoint.Latitude,
                    EndLongitude = (decimal)request.EndPoint.Longitude,
                    EndPlaceId = "END",
                    SeatCount = request.Seats
                };

                var estimateResponse = EstimateInfo.FromEstimateResponse(await _requestsApiClient.RequestsEstimateAsync(requestInstance));

                _logger.LogInformation("[UberClient::EstimatesService::GetEstimates] RequestsEstimate API call successfuully finished.");

                var estimateResponseId = ServiceID.CreateServiceID(service).ToString();

                _logger.LogInformation($"[UberClient::EstimatesService::GetEstimates] Generated Estimate ID: {estimateResponseId}");

                _productsApiClient.Configuration = new Configuration { AccessToken = await _accessTokenService.GetAccessTokenAsync(SessionToken, service) };

                _logger.LogInformation($"[UberClient::EstimatesService::GetEstimates] Invoking GetProduct API endpoint...");

                var product = await _productsApiClient.ProductProductIdAsync(requestInstance.ProductId);

                _logger.LogInformation("[UberClient::EstimatesService::GetEstimates] GetProduct API call successfuully finished.");

                // Write an InternalAPI model back
                var estimateModel = new EstimateModel()
                {
                    EstimateId = estimateResponseId,
                    CreatedTime = Google.Protobuf.WellKnownTypes.Timestamp.FromDateTime(DateTime.Now),
                    PriceDetails = new CurrencyModel
                    {
                        Price = (double)estimateResponse.Price,
                        Currency = estimateResponse.Currency,
                    },
                    Distance = estimateResponse.Distance,
                    Seats = product.Shared ? request.Seats : product.Capacity,
                    //RequestUrl = $"https://m.uber.com/ul/?client_id={clientId}&action=setPickup&pickup[latitude]={request.StartPoint.Latitude}&pickup[longitude]={request.StartPoint.Longitude}&dropoff[latitude]={request.EndPoint.Latitude}&dropoff[longitude]={request.EndPoint.Longitude}&product_id={service}",
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

            _logger.LogInformation($"[UberClient::EstimatesService::GetEstimateRefresh] HTTP Context session token : {SessionToken}");

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

            var EstimateId = ServiceID.CreateServiceID(service);

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
                Seats = product.Shared ? oldRequest.Seats : product.Capacity,
                //RequestUrl = $"https://m.uber.com/ul/?client_id={clientId}&action=setPickup&pickup[latitude]={oldRequest.StartPoint.Latitude}&pickup[longitude]={oldRequest.StartPoint.Longitude}&dropoff[latitude]={oldRequest.EndPoint.Latitude}&dropoff[longitude]={oldRequest.EndPoint.Longitude}&product_id={service}",
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
