using InternalAPI;
using Grpc.Net.Client;
using ByteString = Google.Protobuf.ByteString;
using UberClient.Services;

namespace Services.ServicesService
{
    public class ServicesService : InternalAPI.Services.ServicesClient , IServicesService
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
            _logger.LogInformation("[UberClient::ServicesService::RegisterServiceRequest] Registering services...");

            var requestUB = new RegisterServiceRequest
            {
                Id = ByteString.CopyFrom(Guid.Parse("d4abaae7-f4d6-4152-91cc-77523e8165a4").ToByteArray()),
                Name = "UberBLACK",
                ClientName = "Uber",
            };

            requestUB.Features.Add(ServiceFeatures.ProfessionalDriver);
            _logger.LogDebug("[UberClient::ServicesService::RegisterServiceRequest] Registering [UberBLACK] service...");
            _services.RegisterService(requestUB);

            var requestUP = new RegisterServiceRequest
            {
                Id = ByteString.CopyFrom(Guid.Parse("26546650-e557-4a7b-86e7-6a3942445247").ToByteArray()),
                Name = "UberPOOL",
                ClientName = "Uber",
            };

            requestUP.Features.Add(ServiceFeatures.Shared);
            _logger.LogDebug("[UberClient::ServicesService::RegisterServiceRequest] Registering [UberPOOL] service...");
            _services.RegisterService(requestUP);

            var requestUX = new RegisterServiceRequest
            {
                Id = ByteString.CopyFrom(Guid.Parse("2d1d002b-d4d0-4411-98e1-673b244878b2").ToByteArray()),
                Name = "UberX",
                ClientName = "Uber",
            };

            requestUX.Features.Add(ServiceFeatures.ProfessionalDriver);
            _logger.LogDebug("[UberClient::ServicesService::RegisterServiceRequest] Registering [UberX] service...");
            _services.RegisterService(requestUX);

            _logger.LogInformation("[UberClient::ServicesService::RegisterServiceRequest] Services Registeration complete.");
        }

        /*public static void Register(ILogger logger) {
            var channel = GrpcChannel.ForAddress($"https://services.api:443");
            var client = new InternalAPI.Services.ServicesClient(channel);
            var runner = new ServicesService(client, logger);
        }*/
    }
}
