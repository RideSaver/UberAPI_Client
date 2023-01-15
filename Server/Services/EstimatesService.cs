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
    // Summary: Handles all the ride-estimates related operations
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
            string clientId = "al0I63Gjwk3Wsmhq_EL8_HxB8qWlO7yY"; // Placeholder value to mimick the client-ID recieved from 3rd party APIs.
            var SessionToken = "" + _httpContextAccessor.HttpContext!.Request.Headers["token"]; // Extract the JWT token from the request-headers for the UserAccessToken requests.
            var servicesList = request.Services.ToList(); // List of service GUIDS. 

            DistributedCacheEntryOptions options = new() { AbsoluteExpirationRelativeToNow = TimeSpan.FromHours(24) , SlidingExpiration = TimeSpan.FromHours(5) };

            // Loop through the list of service-IDs recieved within the request & make an estimate-request to the MockAPI for each appropriaate service ID
            foreach (var service in servicesList)
            {
                ServiceLinker.ServiceIDs.TryGetValue(service, out string? serviceName); // Extract the service-name from the ServiceLinker Dictionary.
                if (serviceName is null) continue; // Skip the current loop-iteration if there is no valid service-name matching the service-ID

                _requestsApiClient.Configuration = new Configuration { AccessToken = await _accessTokenService.GetAccessTokenAsync(SessionToken, service.ToString()) };

                // Create a new instance of (RequestsEstimateRequest) to be sent to the MockAPI.
                RequestsEstimateRequest requestInstance = new()
                {
                    ProductId = service.ToString(),
                    StartLatitude = (decimal)request.StartPoint.Latitude,
                    StartLongitude = (decimal)request.StartPoint.Longitude,
                    StartPlaceId = "pickup-location",
                    EndLatitude = (decimal)request.EndPoint.Latitude,
                    EndLongitude = (decimal)request.EndPoint.Longitude,
                    EndPlaceId = "dropoff-location",
                    SeatCount = request.Seats
                };

                // Make an Estimate request to the MockAPI
                var estimateResponse = EstimateInfo.FromEstimateResponse(await _requestsApiClient.RequestsEstimateAsync(requestInstance));
                if(estimateResponse is null) { throw new ArgumentNullException($"[UberClient::EstimatesService::GetEstimates] {nameof(estimateResponse)}"); }
                var estimateResponseId = ServiceID.CreateServiceID(service).ToString(); // Extract the EstimateID from the response.

                _productsApiClient.Configuration = new Configuration { AccessToken = await _accessTokenService.GetAccessTokenAsync(SessionToken, service) };

                // Make a Product request to the MockAPI
                var product = await _productsApiClient.ProductProductIdAsync(requestInstance.ProductId);
                if (product is null) { throw new ArgumentNullException($"[UberClient::EstimatesService::GetEstimates] {nameof(product)}"); }

                // Write an InternalAPI model back
                var estimateModel = new EstimateModel()
                {
                    EstimateId = estimateResponseId,
                    CreatedTime = Google.Protobuf.WellKnownTypes.Timestamp.FromDateTime(DateTime.Now.ToUniversalTime()),
                    InvalidTime = Google.Protobuf.WellKnownTypes.Timestamp.FromDateTime(DateTime.Now.AddMinutes(5).ToUniversalTime()),
                    Distance = estimateResponse.Distance,
                    Seats = product!.Shared ? request.Seats : product.Capacity,
                    DisplayName = product.DisplayName,
                    RequestUrl = $"https://uber.mock/client_id={clientId}&action=setPickup&pickup[latitude]={request.StartPoint.Latitude}&pickup[longitude]={request.StartPoint.Longitude}&dropoff[latitude]={request.EndPoint.Latitude}&dropoff[longitude]={request.EndPoint.Longitude}&product_id={requestInstance.ProductId}",
                    WayPoints = { { request.StartPoint }, { request.EndPoint } },
                    PriceDetails = new CurrencyModel
                    {
                        Price = (double)estimateResponse.Price,
                        Currency = estimateResponse.Currency,
                    }
                };

                // Create a new EstimateCache instance to be stored in the RedisCache.
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

            // Extract the Estimate instance from the redis Cache.
            EstimateCache? estimateCache = await _cache.GetAsync<EstimateCache>(request.EstimateId.ToString());
            if (estimateCache is null) { throw new ArgumentNullException($"[UberClient::EstimatesService::GetEstimateRefresh] {nameof(estimateCache)}"); }
            var estimateInstance = estimateCache!.GetEstimatesRequest;
            var serviceID = estimateCache.ProductId.ToString();

            DistributedCacheEntryOptions options = new() { AbsoluteExpirationRelativeToNow = TimeSpan.FromHours(24), SlidingExpiration = TimeSpan.FromHours(5) };
            _requestsApiClient.Configuration = new Configuration { AccessToken = await _accessTokenService.GetAccessTokenAsync(SessionToken, serviceID) };

            // Create a new (RequestEstimateRquest) instance to be sent to the MockAPI
            var requestInstance = new RequestsEstimateRequest()
            {
                StartLatitude = (decimal)estimateInstance!.StartPoint.Latitude,
                StartLongitude = (decimal)estimateInstance.StartPoint.Longitude,
                StartPlaceId = "pickup-location",
                EndLatitude = (decimal)estimateInstance.EndPoint.Latitude,
                EndLongitude = (decimal)estimateInstance.EndPoint.Longitude,
                SeatCount = estimateInstance.Seats,
                EndPlaceId = "dropoff-location",
                ProductId = serviceID
            };

            // Make an Estimate request to the MockAPI.
            var estimateResponse = EstimateInfo.FromEstimateResponse(await _requestsApiClient.RequestsEstimateAsync(requestInstance));
            if (estimateResponse is null) { throw new ArgumentNullException($"[UberClient::EstimatesService::GetEstimates] {nameof(estimateResponse)}"); }
            var estimateResponseId = ServiceID.CreateServiceID(serviceID).ToString();
            estimateResponse.FareId = Guid.NewGuid().ToString();

            _productsApiClient.Configuration = new Configuration { AccessToken = await _accessTokenService.GetAccessTokenAsync(SessionToken, serviceID) };

            // Make a Product request to the MockAPI
            var product = await _productsApiClient.ProductProductIdAsync(serviceID);
            if (product is null) { throw new ArgumentNullException($"[UberClient::EstimatesService::GetEstimateRefresh] {nameof(product)}"); }

            // Create an EstimateModel to be sent back to the EstimatesAPI.
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

            // Create a new EstimateCache instance & store it within the redis-cache.
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
