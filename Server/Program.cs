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
    options.Configuration = "localhost:6379";  
    options.InstanceName = "";  
});
builder.Services.AddSingleton<IHttpClientInstance, HttpClientInstance>();
builder.Services.AddSingleton<IAccessTokenController, AccessTokenController>();
builder.Services.AddGrpc();

var app = builder.Build();

app.UseHttpsRedirection();

app.UseAuthorization();

app.MapControllers();

(new HttpClientInstance()).InitializeClient();

app.MapGrpcService<EstimatesService>();
app.MapGrpcService<RequestsService>();

var servicesClient = new Services.ServicesClient(GrpcChannel.ForAddress($"https://services.api:7042"));
var request = new RegisterServiceRequest
{
    Id = ByteString.CopyFrom(Guid.Parse("d4abaae7-f4d6-4152-91cc-77523e8165a4").ToByteArray()),
    Name = "uber BLACK",
    ClientName = "uber",
};
request.Features.Add(ServiceFeatures.ProfessionalDriver);
servicesClient.RegisterService(request);
request.Features.Clear();

request = new RegisterServiceRequest
{
    Id = ByteString.CopyFrom(Guid.Parse("26546650-e557-4a7b-86e7-6a3942445247").ToByteArray()),
    Name = "uber POOL",
    ClientName = "uber",
};
request.Features.Add(ServiceFeatures.Shared);
servicesClient.RegisterService(request);
request.Features.Clear();

request = new RegisterServiceRequest
{
    Id = ByteString.CopyFrom(Guid.Parse("2d1d002b-d4d0-4411-98e1-673b244878b2").ToByteArray()),
    Name = "uber X",
    ClientName = "uber",
};
request.Features.Add(ServiceFeatures.ProfessionalDriver);
servicesClient.RegisterService(request);
request.Features.Clear();

app.Run();
