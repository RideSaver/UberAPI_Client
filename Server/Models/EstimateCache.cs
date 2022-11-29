using UberAPI.Client.Model;
using InternalAPI;
namespace UberClient.Models
{
    public class EstimateCache
    {
        public PriceEstimate PriceEstimate{ get; set; }
        public GetEstimatesRequest GetEstimatesRequest { get; set; }
        public Guid ProductId { get; set; }
        public CurrencyModel CancellationCost { get; set; }
        public Guid RequestId { get; set; }
    }
}
