using UberAPI.Client.Model;

namespace UberClient.Models
{
    public class EstimateInfo
    {
        public string? FareId { get; set; }
        public int Distance { get; set; }
        public double Price { get; set; }
        public string? Currency { get; set; }
        public static EstimateInfo FromEstimateResponse(RequestEstimateResponse estimateResponse)
        {
            if (estimateResponse.ActualInstance is EstimateWithSurge)
            {
                EstimateWithSurge estimateWithSurge = (EstimateWithSurge)estimateResponse.ActualInstance;
                return new EstimateInfo
                {
                    FareId = null,
                    Distance = (int)estimateWithSurge.Trip.DistanceEstimate,
                    Price = estimateWithSurge.Estimate.HighEstimate,
                    Currency = estimateWithSurge.Estimate.CurrencyCode,
                };
            }
            else if (estimateResponse.ActualInstance is EstimateWithoutSurge)
            {
                EstimateWithoutSurge estimateWithoutSurge = (EstimateWithoutSurge)estimateResponse.ActualInstance;
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
