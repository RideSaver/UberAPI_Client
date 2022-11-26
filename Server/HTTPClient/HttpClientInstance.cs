namespace UberClient.HTTPClient
{
    public class HttpClientInstance : IHttpClientInstance // Static class, gets initalized once to avoid opening multiple ports.
    {
        public HttpClient APIClientInstance { get; set; } // Static instance of HTTP Client -> Used to open ONE TCP port to handle all the communications to other APIs.
        public void InitializeClient() // Initalizes the APIClientInstance when invoked in Program.cs on application startup.
        {
            Console.WriteLine("Initalizing HTTP client instance...");
            APIClientInstance = new HttpClient(new HttpClientHandler
            {
                MaxConnectionsPerServer = 2,
            });

            APIClientInstance.DefaultRequestHeaders.Accept.Clear(); // Clears the headers.
            APIClientInstance.DefaultRequestHeaders.Accept.Add(new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("application/json")); // Allows the headers to accept JSON objects.
        }
    }
}