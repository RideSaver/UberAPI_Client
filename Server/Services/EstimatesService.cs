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
using UberClient.Repository;
/// @startuml
/// participant Services as S
/// participant UberClient as UC
/// collections UberAPI as API
/// database    Cache    as C
/// S -> UC : Access Token
/// Activate UC
/// UC -> C : Request EstimateId
/// Activate C
/// UC <-- C : Return EstimateId
/// Deactivate C
/// UC -> API : GET/POST/DELETE Request
/// Activate API
/// UC <-- API : Estimate/Ride Object Response
/// Deactivate API
/// UC -> C : Update/New EstimateId
/// Activate C
/// S <- UC : Data
/// Deactivate UC
/// @enduml

//! A Estimates Service class. 
/*!
 * This class handles all requests for estimates. It contains call two methods: GetEstimates and GetEstimateRefresh.
 * The GetEstimates method requests the estimate resources from the Uber API. Firstly, it receives the user's access token 
 * and the protocol buffer data to be deserialized into the standard models. Then, it is serialized into uber models and 
 * an authentication token is added. The Uber Client makes a GET request to the Uber API, which returns an response object 
 * that contains the request estimate data. A loop is used for each instance, added to the EstimateId, then stored in the cache. 
 * Finally, the Uber Client returns the data to the services that requested it. The GetEstimateRefresh requests new estimate 
 * resources from the Uber API to replace the old resources. It requests the EstimateId held in the cache, then functions like 
 * the GetEstimates method except replaces the previous EstimateId with a newly created one.
 */
namespace UberClient.Services
{
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

        // Summary: Our Access Token Controller
        private readonly IAccessTokenController _accessController;

        public EstimatesService(ILogger<EstimatesService> logger, IDistributedCache cache, IHttpClientInstance httpClient, IAccessTokenController accessContoller)
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
        /// Cl : Estimate Values
        /// state "Uber Client receives protocol buffer data" as RD
        /// state "Add Authentication Token to Uber Client" as AuthT
        /// state "Make Get Estimate request to Uber API" as GE
        /// GE : Request Object as Parameter
        /// state "Uber sends back data of requested estimate list" as EO
        /// state "Uber Client receives response object" as RO
        /// RO : loops through each instance and adds to EstimateId
        /// state "EstimateId to cache" as C
        /// state "Uber Client sends the data to the service" as S
        ///
        /// [*] -d-> AT
        /// AT -d-> Cl
        /// Cl -d-> RD
        /// RD -d-> AuthT : Deserializes to standard model
        /// AuthT -d-> GE : Serializes to uber model
        /// GE -d-> EO
        /// EO -d-> RO
        /// RO -d-> C : Serialization to protocol buffer data
        /// C ------> AT
        /// C -d-> S
        /// S -d-> [*]
        ///@enduml
        public override async Task GetEstimates(GetEstimatesRequest request, IServerStreamWriter<EstimateModel> responseStream, ServerCallContext context)
        {
            var SessionToken = context.AuthContext.PeerIdentityPropertyName;
            _logger.LogInformation("HTTP Context User: {User}", SessionToken);

            string clientId = "al0I63Gjwk3Wsmhq_EL8_HxB8qWlO7yY";
            DistributedCacheEntryOptions options = new DistributedCacheEntryOptions() { AbsoluteExpirationRelativeToNow = TimeSpan.FromHours(24)};
            // Loop through all the services in the request
            foreach (var service in request.Services)
            {
                _apiClient.Configuration = new UberAPI.Client.Client.Configuration {
                    AccessToken = await _accessController.GetAccessToken(SessionToken, service),
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
        /// @startuml
        /// state "Get Access Token" as AT
        /// state "gRPC call to Uber Client" as Cl
        /// Cl : Previous EstimateId
        /// state "Get Previous EstimateId from cache" as GEC
        /// state "Uber Client receives protocol buffer data" as RD
        /// state "Add Authentication Token to Uber Client" as AuthT
        /// state "Make Get Estimate request to Uber API" as GE
        /// GE : Request Object as Parameter
        /// state "Uber sends back data of requested estimate list" as EO
        /// state "Uber Client receives response object" as RO
        /// RO : iterates once through instances and adds to EstimateId
        /// state "Create new EstimateId to cache" as UC
        /// state "Uber Client sends the data to the service" as S
        /// 
        /// [*] -d-> AT
        /// AT -d-> Cl
        /// Cl -d-> GEC
        /// GEC -d-> RD
        /// RD -d-> AuthT : Deserializes to standard model
        /// AuthT -d-> GE : Serializes to uber model
        /// GE -d-> EO
        /// EO -d-> RO
        /// RO -d-> UC : Serialization to protocol buffer data
        /// UC -d-> S
        /// S -d-> [*]
        /// @enduml
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
            _apiClient.Configuration = new UberAPI.Client.Client.Configuration {
                AccessToken = await _accessController.GetAccessToken(SessionToken, service),
            };
            var estimate = EstimateInfo.FromEstimateResponse(await _apiClient.RequestsEstimateAsync(new RequestsEstimateRequest
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
