using Grpc.Core;
using InternalAPI;
using UberAPI.Client.Model;

namespace UberClient.Services
{
    public class EstimatesService : Estimates.EstimatesBase
    {
        private readonly ILogger<EstimatesService> _logger;
        private UberAPI.Client.Api.RequestsApi apiClient;

        public EstimatesService(ILogger<EstimatesService> logger)
        {
            _logger = logger;
            apiClient = new UberAPI.Client.Api.RequestsApi(/** TODO: Initialize correctly */);
        }
        public override async Task GetEstimates(GetEstimatesRequest request, IServerStreamWriter<EstimateModel> responseStream, ServerCallContext context)
        {
            foreach (var service in request.Services)
            {
                // Uber returns estimates for all products
                var estimate = await this.apiClient.RequestsEstimatePostAsync(new RequestsEstimatePostRequest
                {
                    StartLatitude = (decimal)request.StartPoint.Latitude,
                    StartLongitude = (decimal)request.StartPoint.Longitude,
                    EndLatitude = (decimal)request.EndPoint.Latitude,
                    EndLongitude = (decimal)request.EndPoint.Longitude,
                    SeatCount = request.Seats,
                    ProductId = service // TODO: Get proper product id from map
                });
                await responseStream.WriteAsync(new EstimateModel
                {
                    EstimateId = "NEW ID GENERATOR",
                    PriceDetails = new CurrencyModel
                    {
                        Price = (double)estimate.HighEstimate,
                        Currency = estimate.CurrencyCode
                    },
                    Distance = (int)estimate.Distance
                });
            }
            var estimateModel = new EstimateModel();

            await responseStream.WriteAsync(estimateModel);
        }

        public override Task<EstimateModel> GetEstimateRefresh(GetEstimateRefreshRequest request, ServerCallContext context)
        {
            var estimateRefresh = new EstimateModel();
            // TBA: Invoke the web-client API to get the information from the uber-api, then send it to the microservice.

            return Task.FromResult(estimateRefresh);
        }
    }
}
