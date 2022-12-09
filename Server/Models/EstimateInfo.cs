using UberAPI.Client.Model;

namespace UberClient.Models
{
    //! \class EstimateInfo
    /*!
     * This class creates new properties that uses the get method to return the 
     * value of the variable and uses the set method to assign a value to the
     * variable. Additionally, the EstimateInfo property has a parameter estimateResponse
     * to return the appropriate object based on surge and non-surge estimates.
    */
    public class EstimateInfo
    {
        public string FareId { get; set; }
        public int Distance { get; set; }
        public double Price { get; set; }
        public string Currency { get; set; }
        public static EstimateInfo FromEstimateResponse(RequestEstimateResponse estimateResponse)
        {
            if (estimateResponse.GetType() == typeof(EstimateWithSurge))
            {
                EstimateWithSurge estimateWithSurge = estimateResponse.ActualInstance as EstimateWithSurge;
                return new EstimateInfo {
                    FareId = null,
                    Distance = (int)estimateWithSurge.Trip.DistanceEstimate,
                    Price = estimateWithSurge.Estimate.HighEstimate,
                    Currency = estimateWithSurge.Estimate.CurrencyCode,
                };
            }
            else if (estimateResponse.GetType() == typeof(EstimateWithoutSurge)) {
                EstimateWithoutSurge estimateWithoutSurge = estimateResponse.ActualInstance as EstimateWithoutSurge;
                return new EstimateInfo
                {
                    FareId = estimateWithoutSurge.Fare.FareId,
                    Distance = (int)estimateWithoutSurge.Trip.DistanceEstimate,
                    Price = (double)estimateWithoutSurge.Fare.Value,
                    Currency = estimateWithoutSurge.Fare.CurrencyCode,
                };
            }
            throw new ArgumentException("Invalid instance found. Must be the following types: EstimateWithSurge, EstimateWithoutSurge");
        }
    }
}
