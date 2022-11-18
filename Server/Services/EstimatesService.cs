using Grpc.Core;
using InternalAPI;
using UberAPI.Client.Model;

namespace UberClient.Services
{
    // Summary: Handles all requests for estimates
    public class EstimatesService : Estimates.EstimatesBase
    {
        // Summary: our logging object, used for diagnostic logs.
        private readonly ILogger<EstimatesService> _logger;
        // Summary: our API client, so we only open up some ports, rather than swamping the system.
        private HttpClient apiClient;

        public EstimatesService(ILogger<EstimatesService> logger)
        {
            _logger = logger;
            apiClient = new HttpClient(new HttpClientHandler {
                MaxConnectionsPerServer = 2 // Make sure we only open up a maximum of 2 connections per server (i.e. uber.com)
            });
        }
        public override async Task GetEstimates(GetEstimatesRequest request, IServerStreamWriter<EstimateModel> responseStream, ServerCallContext context)
        {
            // Create new API client (since it doesn't seem to allow dynamic loading of credentials)
            var apiClient = new UberAPI.Client.Api.RequestsApi(this.apiClient, new UberAPI.Client.Client.Configuration {
                AccessToken = "" // TODO: Get access token from distributed cache
            });
            // Loop through all the services in the request
            foreach (var service in request.Services)
            {
                // Get estimate with parameters
                var estimate = await apiClient.RequestsEstimatePostAsync(new RequestsEstimatePostRequest
                {
                    StartLatitude = (decimal)request.StartPoint.Latitude,
                    StartLongitude = (decimal)request.StartPoint.Longitude,
                    EndLatitude = (decimal)request.EndPoint.Latitude,
                    EndLongitude = (decimal)request.EndPoint.Longitude,
                    SeatCount = request.Seats,
                    ProductId = service // TODO: Get proper product id from map
                });
                // Write an InternalAPI model back
                await responseStream.WriteAsync(new EstimateModel
                {
                    // TODO: populate most of this data with data from the estimate.
                    EstimateId = "NEW ID GENERATOR",
                    PriceDetails = new CurrencyModel
                    {
                        Price = (double)estimate.HighEstimate,
                        Currency = estimate.CurrencyCode
                    },
                    Distance = (int)estimate.Distance
                });
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
