using UberClient.Services;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using System.Security.Cryptography.X509Certificates;
using InternalAPI;
using UberClient.Interface;
using UberClient.Internal;
using StackExchange.Redis;
using Microsoft.AspNetCore.DataProtection;

var builder = WebApplication.CreateBuilder(args);

//---------------------------------------------------[REDIS CACHE CONFIG]-------------------------------------------------------------//
var redisConfig = new ConfigurationOptions()
{
    EndPoints = { { "uber-redis", 6379 } },
    Password = "a-very-complex-password-here",
    ConnectRetry = 3,
    KeepAlive = 180,
    SyncTimeout = 15000,
    ConnectTimeout = 15000,
    AbortOnConnectFail = false,
    AllowAdmin = true,
    Ssl = false,
    SslProtocols = System.Security.Authentication.SslProtocols.Tls12,
    ReconnectRetryPolicy = new ExponentialRetry(5000, 10000),
};

ConnectionMultiplexer CM = ConnectionMultiplexer.Connect(redisConfig);
builder.Services.AddSingleton<IConnectionMultiplexer>(CM);

builder.Services.AddStackExchangeRedisCache(options =>
{
    options.Configuration = builder.Configuration.GetConnectionString("RedisCache");
    options.InstanceName = "Redis_";
    options.ConfigurationOptions = redisConfig;
    options.ConfigurationOptions.TrustIssuer("/redis/ca.crt");
    options.ConfigurationOptions.CertificateSelection += delegate
    {
        var redisCert = new X509Certificate2(Path.Combine("/redis/ca.crt"), "");
        return redisCert;
    };

    options.ConnectionMultiplexerFactory = () =>
    {
        IConnectionMultiplexer connection = ConnectionMultiplexer.Connect(options.ConfigurationOptions);
        return Task.FromResult(connection);
    };
});

builder.Services.AddDataProtection().SetApplicationName("UberClient").PersistKeysToStackExchangeRedis(ConnectionMultiplexer.Connect(redisConfig), "DataProtection-Keys");

//------------------------------------------------------------------------------------------------------------------------------------//

builder.Services.AddMvc();
builder.Services.AddHttpClient();
builder.Services.AddHttpContextAccessor();
builder.Services.AddGrpc();
builder.Services.AddHealthChecks();

builder.Services.AddTransient<IAccessTokenService, AccessTokenService>();
builder.Services.AddSingleton<IServicesService, ServicesService>();
builder.Services.AddSingleton<ICacheProvider, CacheProvider>();

builder.Services.AddHostedService<ServicesService>();
builder.Services.AddHostedService<CertificateStatusService>();

builder.Services.AddGrpcClient<Services.ServicesClient>(o =>
{
    HttpClientHandler httpHandler = new()
    {
        ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator
    };
    o.Address = new Uri($"https://services.api:443");
});

builder.Services.AddGrpcClient<Users.UsersClient>(o =>
{
    o.Address = new Uri($"https://identity.api:443");
});

builder.Services.Configure<ListenOptions>(options =>
{
    options.UseHttps(new X509Certificate2(Path.Combine("/certs/tls.crt"), Path.Combine("/certs/tls.key")));
});

var app = builder.Build();

app.UseRouting();
app.UseHttpsRedirection();
app.MapControllers();
app.MapHealthChecks("/healthz");
app.UseEndpoints(endpoints =>
{
    endpoints.MapGrpcService<EstimatesService>();
    endpoints.MapGrpcService<RequestsService>();
});
app.Run();
