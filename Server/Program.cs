using Grpc.Core;
using Grpc.Net.Client;
using UberClient.Services;
using UberClient.Repository;
using UberClient.HTTPClient;
using Microsoft.EntityFrameworkCore.Internal;
using InternalAPI;
using Microsoft.Data.SqlClient;
using ByteString = Google.Protobuf.ByteString;

var builder = WebApplication.CreateBuilder(args);


// Add services to the container.

builder.Services.AddMvc();  
builder.Services.AddDistributedRedisCache(options => {  
    options.Configuration = "uber-redis:6379";  
    options.InstanceName = "";  
});
builder.Services.AddSingleton<IHttpClientInstance, HttpClientInstance>();
builder.Services.AddSingleton<IAccessTokenController, AccessTokenController>();
builder.Services.AddGrpc();

builder.Services.AddGrpcClient<InternalAPI.Services.ServicesClient>(o =>
{
    o.Address = new Uri($"https://services.api:7042");
});

var app = builder.Build();

app.UseHttpsRedirection();

app.UseAuthorization();

app.MapControllers();

app.MapGrpcService<EstimatesService>();
app.MapGrpcService<RequestsService>();

app.Run();
