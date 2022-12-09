using Grpc.Core;
using Grpc.Net.Client;
using UberAPI.Client.Model;
using InternalAPI;
using UberClient.Models;
namespace UberClient.Repository
{
    public class AccessTokenController : IAccessTokenController
    {
        public AccessTokenController()
        {
            _client = new Users.UsersClient(GrpcChannel.ForAddress($"https://users.api:7042"));
        }

        async public Task<string> GetAccessToken(string SessionToken, string ServiceId)
        {
            var headers = new Metadata();
            headers.Add("Authorization", $"Bearer {SessionToken}");
            var usersClient = new Users.UsersClient(GrpcChannel.ForAddress($"users.api"));
            var AccessToken = await _client.GetUserAccessTokenAsync(new GetUserAccessTokenRequest
            {
                ServiceId = ServiceId,
            }, headers);
            return AccessToken.AccessToken;
        }

        private Users.UsersClient _client { get; set; }
    }
}
