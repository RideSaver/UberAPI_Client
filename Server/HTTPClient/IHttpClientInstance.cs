namespace UberClient.HTTPClient
{
    public interface IHttpClientInstance
    {

        HttpClient APIClientInstance { get; } // Static instance of HTTP Client -> Used to open ONE TCP port to handle all the communications to other APIs.
        void InitializeClient();
    }
}