using UberClient.Services;
using UberClient.HTTPClient;
using Microsoft.EntityFrameworkCore.Internal;
using InternalAPI;
using Microsoft.Data.SqlClient;

var builder = WebApplication.CreateBuilder(args);


// Add services to the container.

builder.Services.AddMvc();  
builder.Services.AddDistributedRedisCache(options => {  
    options.Configuration = "localhost:6379";  
    options.InstanceName = "";  
});
builder.Services.AddSingleton<IHttpClientInstance, HttpClientInstance>();
builder.Services.AddGrpc();

var app = builder.Build();

app.UseHttpsRedirection();

app.UseAuthorization();

app.MapControllers();

HttpClientInstance.InitializeClient();

app.MapGrpcService<EstimatesService>();
app.MapGrpcService<RequestsService>();

var servicesClient = new Services.ServicesClient(GrpcChannel.ForAddress($"services.api"));
var request = new RegisterServiceRequest
{
    Id = "d4abaae7-f4d6-4152-91cc-77523e8165a4",
    Name = "uber BLACK",
    ClientName = "uber",
};
request.Features.Add(ServiceFeatures.ProfessionalDriver);
servicesClient.RegisterService(request);
request.Features.Clear();

var request = new RegisterServiceRequest
{
    Id = "26546650-e557-4a7b-86e7-6a3942445247",
    Name = "uber POOL",
    ClientName = "uber",
};
request.Features.Add(ServiceFeatures.Shared);
servicesClient.RegisterService(request);
request.Features.Clear();

var request = new RegisterServiceRequest
{
    Id = "2d1d002b-d4d0-4411-98e1-673b244878b2",
    Name = "uber X",
    ClientName = "uber",
};
request.Features.Add(ServiceFeatures.ProfessionalDriver);
servicesClient.RegisterService(request);
request.Features.Clear();

app.Run();
