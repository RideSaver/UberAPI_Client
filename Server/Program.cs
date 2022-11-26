// using UberClient.HTTPClient;
using UberClient.Services;
using UberClient.HTTPClient;

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

// HttpClientInstance.InitializeClient();

app.MapGrpcService<EstimatesService>();
app.MapGrpcService<RequestsService>();

app.Run();
