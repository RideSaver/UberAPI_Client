using UberAPI.Client.Model;
using InternalAPI;
using UberClient.Models;
namespace UberClient.Models
{
    //! \class EstimateCache
    /*!
     * This class creates new properties that uses the get method to return the 
     * value of the variable and uses the set method to assign a value to the
     * variable.
    */
    public class EstimateCache
    {
        public EstimateInfo EstimateInfo { get; set; }
        public GetEstimatesRequest GetEstimatesRequest { get; set; }
        public Guid ProductId { get; set; }
        public CurrencyModel CancellationCost { get; set; }
        public Guid RequestId { get; set; }
    }
}
