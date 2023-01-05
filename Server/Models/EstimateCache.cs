using UberAPI.Client.Model;
using InternalAPI;
using UberClient.Models;
namespace UberClient.Models
{
    public class EstimateCache
    {
        public EstimateInfo? EstimateInfo { get; set; }
        public GetEstimatesRequest? GetEstimatesRequest { get; set; }
        public Guid ProductId { get; set; }
        public CurrencyModel? CancellationCost { get; set; }
        public Guid RequestId { get; set; }
    }
}
