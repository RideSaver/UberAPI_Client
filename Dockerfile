FROM mcr.microsoft.com/devcontainers/dotnet:0-6.0 AS builder

# Install tools
RUN apt-get update && export DEBIAN_FRONTEND=noninteractive \
    && apt-get -y install --no-install-recommends default-jre
COPY .config /client/.config
WORKDIR /client
RUN dotnet tool restore

# Copy all files
COPY . .

ARG github_username
ARG github_token
RUN dotnet nuget add source --username $github_username --password $github_token --store-password-in-clear-text --name github "https://nuget.pkg.github.com/RideSaver/index.json"
RUN dotnet cake --target=Publish --runtime="linux-musl"

FROM alpine:3.16 AS runtime
# Add labels to add information to the image
LABEL org.opencontainers.image.source=https://github.com/RideSaver/UberAPIClient
LABEL org.opencontainers.image.description="Uber API Client for RideSaver"
LABEL org.opencontainers.image.licenses=MIT

# Add tags to define the api image

# Add some libs required by .NET runtime
RUN apk add --no-cache libstdc++ libintl openssl

EXPOSE 80
EXPOSE 443
EXPOSE 6379
EXPOSE 6380
ENV DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1

# Copy
WORKDIR /app
COPY --from=builder /client/publish ./

ENTRYPOINT ["./UberClient", "--urls", "http://0.0.0.0:80;https://0.0.0.0:443"]
