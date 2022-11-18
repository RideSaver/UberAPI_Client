using Grpc.Core;

namespace UberClient.Services
{
    public class EstimatesService : Estimates.EstimatesBase
    {
        private readonly ILogger<EstimatesService> _logger;

        public EstimatesService(ILogger<EstimatesService> logger)
        {
            _logger = logger;
        }
        public override async Task GetEstimates(GetEstimatesRequest request, IServerStreamWriter<EstimateModel> responseStream, ServerCallContext context)
        {
            var estimateModel = new EstimateModel();
            // TBA: Invoke the web-client API to get the information from the uber-api, then send it to the microservice.

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
