using Grpc.Core;
using InternalAPI;
using UberClient.Interface;

namespace UberClient.Internal
{
    public class AccessTokenService : IAccessTokenService
    {
        private readonly Users.UsersClient _client;
        public AccessTokenService(Users.UsersClient client) => _client = client;
        public async Task<string> GetAccessTokenAsync(string SessionToken, string ServiceId)
        {
            var headers = new Metadata
            {
                { "Authorization", $"{SessionToken}" }
            };

            var AccessTokenResponse = await _client.GetUserAccessTokenAsync(new GetUserAccessTokenRequest { ServiceId = ServiceId }, headers);

            if (AccessTokenResponse is null) return string.Empty;

            return AccessTokenResponse.AccessToken;
        }
    }
}
