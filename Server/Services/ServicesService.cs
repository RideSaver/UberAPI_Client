using InternalAPI;
using Grpc.Net.Client;
using ByteString = Google.Protobuf.ByteString;
using UberClient.Services;

namespace Services.ServicesService
{
    public class ServicesService : InternalAPI.Services.ServicesClient , IServicesService , IHostedService
    {
        private readonly InternalAPI.Services.ServicesClient _services;
        private readonly ILogger<ServicesService> _logger;

        public ServicesService(InternalAPI.Services.ServicesClient services, ILogger<ServicesService> logger)
        {
            _services = services;
            _logger = logger;
        }

        public async Task RegisterServiceRequest()
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
            var replyUB = await _services.RegisterServiceAsync(requestUB);

            var requestUP = new RegisterServiceRequest
            {
                Id = ByteString.CopyFrom(Guid.Parse("26546650-e557-4a7b-86e7-6a3942445247").ToByteArray()),
                Name = "UberPOOL",
                ClientName = "Uber",
            };

            requestUP.Features.Add(ServiceFeatures.Shared);
            _logger.LogDebug("[UberClient::ServicesService::RegisterServiceRequest] Registering [UberPOOL] service...");
            var replyUP = await _services.RegisterServiceAsync(requestUP);

            var requestUX = new RegisterServiceRequest
            {
                Id = ByteString.CopyFrom(Guid.Parse("2d1d002b-d4d0-4411-98e1-673b244878b2").ToByteArray()),
                Name = "UberX",
                ClientName = "Uber",
            };

            requestUX.Features.Add(ServiceFeatures.ProfessionalDriver);
            _logger.LogDebug("[UberClient::ServicesService::RegisterServiceRequest] Registering [UberX] service...");
            var replyUX = await _services.RegisterServiceAsync(requestUX);

            _logger.LogInformation("[UberClient::ServicesService::RegisterServiceRequest] Services Registeration complete.");
        }

        public async Task StartAsync(CancellationToken cancellationToken)
        {
            await RegisterServiceRequest();
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }
    }
}
