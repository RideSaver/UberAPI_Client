using System.Runtime.Serialization;
using System.Text;
using UberAPI.Client.Model;

namespace UberClient.Models
{
    [DataContract]
    public class EstimateInfo
    {
        [DataMember]
        public string? FareId { get; set; }
        [DataMember]
        public int Distance { get; set; }
        [DataMember]
        public double Price { get; set; }
        [DataMember]
        public string? Currency { get; set; }
        public static EstimateInfo FromEstimateResponse(RequestEstimateResponse estimateResponse)
        {
            if (estimateResponse.ActualInstance is EstimateWithSurge estimateWithSurge)
            {
                return new EstimateInfo
                {
                    FareId = string.Empty,
                    Distance = (int)estimateWithSurge.Trip.DistanceEstimate,
                    Price = estimateWithSurge.Estimate.HighEstimate,
                    Currency = estimateWithSurge.Estimate.CurrencyCode,
                };
            }
            else if (estimateResponse.ActualInstance is EstimateWithoutSurge estimateWithoutSurge)
            {
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

        public override string ToString()
        {
            var sb = new StringBuilder();
            sb.Append("class EstimateInfo {\n");
            sb.Append(" FareId: ").Append(FareId).Append("\n");
            sb.Append(" Distance: ").Append(Distance).Append("\n");
            sb.Append(" Price: ").Append(Price).Append("\n");
            sb.Append(" Currency: ").Append(Currency).Append("\n");
            sb.Append("}\n");
            return sb.ToString();

        }
    }
}
