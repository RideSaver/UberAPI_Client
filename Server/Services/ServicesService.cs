using InternalAPI;
using ByteString = Google.Protobuf.ByteString;

namespace Services.ServicesService
{
    public class ServicesService : InternalAPI.Services.ServicesClient
    {
        private readonly InternalAPI.Services.ServicesClient _services;
        private readonly ILogger<ServicesService> _logger;

        public ServicesService(InternalAPI.Services.ServicesClient services, ILogger<ServicesService> logger)
        {
            _services = services;
            _logger = logger;
            RegisterServiceRequest();
        }

        public void RegisterServiceRequest()
        {
            _logger.LogInformation("Registering Services");
            var request = new RegisterServiceRequest
            {
                Id = ByteString.CopyFrom(Guid.Parse("d4abaae7-f4d6-4152-91cc-77523e8165a4").ToByteArray()),
                Name = "UberBLACK",
                ClientName = "uber",
            };
            request.Features.Add(ServiceFeatures.ProfessionalDriver);
            _logger.LogDebug("Registering UberBLACK");
            _services.RegisterService(request);
            request.Features.Clear();

            request = new RegisterServiceRequest
            {
                Id = ByteString.CopyFrom(Guid.Parse("26546650-e557-4a7b-86e7-6a3942445247").ToByteArray()),
                Name = "UberPOOL",
                ClientName = "uber",
            };
            request.Features.Add(ServiceFeatures.Shared);
            _logger.LogDebug("Registering UberPOOL");
            _services.RegisterService(request);
            request.Features.Clear();

            request = new RegisterServiceRequest
            {
                Id = ByteString.CopyFrom(Guid.Parse("2d1d002b-d4d0-4411-98e1-673b244878b2").ToByteArray()),
                Name = "UberX",
                ClientName = "uber",
            };
            request.Features.Add(ServiceFeatures.ProfessionalDriver);
            _logger.LogDebug("Registering UberX");
            _services.RegisterService(request);
            request.Features.Clear();
        }

        public static void Register() {
            var channel = GrpcChannel.ForAddress($"https://services.api:443");
            var client = new Services.ServicesClient(channel);
            var runner = new ServicesService(client);
        }
    }
}
