using UberClient.Services;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using System.Security.Cryptography.X509Certificates;
using InternalAPI;
using UberClient.Interface;
using UberClient.Internal;
using UberClient.Filters;
using Microsoft.ApplicationInsights.Extensibility;
using Microsoft.AspNetCore.DataProtection;
using StackExchange.Redis;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddMvc();

builder.Services.AddDataProtection()
                .SetApplicationName("LyftClientSession")
                .PersistKeysToStackExchangeRedis(ConnectionMultiplexer.Connect(
                "uber-redis:6379,password=a-very-complex-password-here,ssl=True,abortConnect=False"),
                "DataProtection-Keys");

builder.Services.AddStackExchangeRedisCache(options =>
{
    options.Configuration = builder.Configuration.GetConnectionString("RedisCache");
    options.InstanceName = "Redis_";

    options.ConfigurationOptions = new ConfigurationOptions()
    {
        EndPoints = { "uber-redis:6379" },
        Password = "a-very-complex-password-here",
        SyncTimeout = 5000,
        ConnectTimeout = 5000,
        AbortOnConnectFail = false,
        Ssl = true,
        ConnectRetry = 3,
        ReconnectRetryPolicy = new LinearRetry(5000)
    };
});

builder.Services.AddHttpClient();
builder.Services.AddHttpContextAccessor();
builder.Services.AddGrpc();
builder.Services.AddHealthChecks();

builder.Services.AddTransient<IAccessTokenService, AccessTokenService>();
builder.Services.AddSingleton<IServicesService, ServicesService>();
builder.Services.AddSingleton<ITelemetryInitializer, FilterHealthchecksTelemetryInitializer>();
builder.Services.AddSingleton<ICacheProvider, CacheProvider>();


builder.Services.AddHostedService<ServicesService>();

builder.Services.Configure<ListenOptions>(options =>
{
    options.UseHttps(new X509Certificate2(Path.Combine("/certs/tls.crt"), Path.Combine("/certs/tls.key")));
});

builder.Services.AddGrpcClient<Services.ServicesClient>(o =>
{
    HttpClientHandler httpHandler = new();
    httpHandler.ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator;
    o.Address = new Uri($"https://services.api:443");
});

builder.Services.AddGrpcClient<Users.UsersClient>(o =>
{
    o.Address = new Uri($"https://identity.api:443");
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
