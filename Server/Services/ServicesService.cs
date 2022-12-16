using InternalAPI;
using ByteString = Google.Protobuf.ByteString;

namespace Services.ServicesService
{
    public class ServicesService : InternalAPI.Services.ServicesClient
    {
        private readonly InternalAPI.Services.ServicesClient _services;

        public ServicesService(InternalAPI.Services.ServicesClient services)
        {
            _services = services;
            RegisterServiceRequest();
        }

        public void RegisterServiceRequest()
        {
            var request = new RegisterServiceRequest
            {
                Id = ByteString.CopyFrom(Guid.Parse("d4abaae7-f4d6-4152-91cc-77523e8165a4").ToByteArray()),
                Name = "UberBLACK",
                ClientName = "Uber",
            };
            request.Features.Add(ServiceFeatures.ProfessionalDriver);
            _services.RegisterService(request);
            request.Features.Clear();

            request = new RegisterServiceRequest
            {
                Id = ByteString.CopyFrom(Guid.Parse("26546650-e557-4a7b-86e7-6a3942445247").ToByteArray()),
                Name = "UberPOOL",
                ClientName = "Uber",
            };
            request.Features.Add(ServiceFeatures.Shared);
            _services.RegisterService(request);
            request.Features.Clear();

            request = new RegisterServiceRequest
            {
                Id = ByteString.CopyFrom(Guid.Parse("2d1d002b-d4d0-4411-98e1-673b244878b2").ToByteArray()),
                Name = "UberX",
                ClientName = "Uber",
            };
            request.Features.Add(ServiceFeatures.ProfessionalDriver);
            _services.RegisterService(request);
            request.Features.Clear();
        }
    }
}
