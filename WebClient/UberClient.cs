using Microsoft.Bot.Schema;
using RideSaver.Server.Models;
using System.Net.Http.Headers;
using System.Net;
using System.Text;
using UberClient.Interface;
using UberClient.HTTPClient;

namespace UberClient.Service
{
    public class UberClient : IUberClient
    {
        private string _baseUri = "";
        private TokenResponse? _token;

        public UberClient()
        {
            InitializeProtocol();
        }

        private void InitializeProtocol()
        {
            ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12 | SecurityProtocolType.Tls11 | SecurityProtocolType.Tls;
        }

        private async Task SetTokenAsync()
        {
            if (_token is null || DateTime.Parse(_token.Expiration) > DateTime.Now)
            {
                HttpClientInstance.APIClientInstance.DefaultRequestHeaders.Clear();
                HttpClientInstance.APIClientInstance.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic");

                var contentType = new StringContent("grant_type=client_credentials", Encoding.UTF8, "application/x-www-form-urlencoded");

                using (HttpResponseMessage response = await HttpClientInstance.APIClientInstance.PostAsync(_baseUri + "/api-path/token", contentType)) // TBD: update path when mock-API is hosted.
                {
                    if (response.IsSuccessStatusCode)
                    {
                        _token = await response.Content.ReadAsAsync<TokenResponse>();
                    }
                    else
                    {
                        throw new Exception(response.ReasonPhrase);
                    }
                }

            }
        }

        public async Task<List<Estimate>> GetEstimates(Location startPoint, Location endPoint, List<Guid> services, int? seats)
        {
            await SetTokenAsync();

            throw new NotImplementedException();
        }

        public async Task<List<Estimate>> GetEstimatesRefresh(List<Guid> estimate_id)
        {
            await SetTokenAsync(); 

            throw new NotImplementedException(); 
        }

        public Task<Ride> GetRideRequest(Guid estimte_id)
        {
            throw new NotImplementedException();
        }

        public Task<Ride> GetRideRequestID(Guid ride_id)
        {
            throw new NotImplementedException();
        }

        public Task DeleteRideRequest(Guid ride_id)
        {
            throw new NotImplementedException();
        }
    }
}
