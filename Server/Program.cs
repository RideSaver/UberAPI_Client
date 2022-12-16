using UberClient.Services;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using System.Security.Cryptography.X509Certificates;
using InternalAPI;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddMvc();
builder.Services.AddDistributedRedisCache(options =>
{
    options.Configuration ="https//uber-redis:6379";
    options.InstanceName = "";
});

builder.Services.AddHttpClient();
builder.Services.AddGrpc();

builder.Services.AddTransient<IAccessTokenService, AccessTokenService>();

builder.Services.Configure<ListenOptions>(options =>
{
    options.UseHttps(new X509Certificate2(Path.Combine("/certs/tls.crt"), Path.Combine("/certs/tls.key")));
});

builder.Services.AddGrpcClient<InternalAPI.Services.ServicesClient>(o =>
{
    o.Address = new Uri($"https://services.api:443");
});

builder.Services.AddGrpcClient<Users.UsersClient>(o =>
{
    o.Address = new Uri($"https://identity-service.api:443");
});

var app = builder.Build();
app.UseRouting();

app.UseHttpsRedirection();
app.MapControllers();

app.UseEndpoints(endpoints =>
{
    endpoints.MapGrpcService<EstimatesService>();
    endpoints.MapGrpcService<RequestsService>();
});

app.Run();
