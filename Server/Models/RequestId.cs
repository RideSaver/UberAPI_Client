using Newtonsoft.Json;
using System.ComponentModel.DataAnnotations;
using System.Runtime.Serialization;
using System.Xml.Linq;

namespace UberClient.Models
{
    [DataContract(Name = "request_id")]
    public partial class RequestId 
    {
        [JsonConstructor]
        public RequestId() { }
    }
}
