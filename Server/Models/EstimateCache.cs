using InternalAPI;
namespace UberClient.Models
{
    public class EstimateCache
    {
        public EstimateInfo? EstimateInfo { get; set; }
        public GetEstimatesRequest? GetEstimatesRequest { get; set; }
        public CurrencyModel? CancellationCost { get; set; }
        public Guid ProductId { get; set; }      
        public Guid RequestId { get; set; }
    }
}
