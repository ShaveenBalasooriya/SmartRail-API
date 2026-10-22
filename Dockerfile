# Build the API, then run it on the ASP.NET Core runtime image. The SDK stage is never published.
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /source

# Restore first, with only the files that affect it, so a code change does not re-restore.
COPY global.json Directory.Build.props Directory.Packages.props SmartRail.sln ./
COPY src/SmartRail.SharedKernel/SmartRail.SharedKernel.csproj src/SmartRail.SharedKernel/
COPY src/SmartRail.SharedKernel.Application/SmartRail.SharedKernel.Application.csproj src/SmartRail.SharedKernel.Application/
COPY src/SmartRail.SharedKernel.Api/SmartRail.SharedKernel.Api.csproj src/SmartRail.SharedKernel.Api/
COPY src/SmartRail.SharedKernel.Infrastructure/SmartRail.SharedKernel.Infrastructure.csproj src/SmartRail.SharedKernel.Infrastructure/
COPY src/SmartRail.Host/SmartRail.Host.csproj src/SmartRail.Host/
COPY src/Contracts/ src/Contracts/
COPY src/Modules/ src/Modules/
RUN dotnet restore src/SmartRail.Host/SmartRail.Host.csproj

COPY . .
RUN dotnet publish src/SmartRail.Host/SmartRail.Host.csproj \
    --configuration Release \
    --no-restore \
    --output /app

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

# curl is needed by the container health check; the runtime image does not ship it.
RUN apt-get update \
    && apt-get install -y --no-install-recommends curl \
    && rm -rf /var/lib/apt/lists/*

COPY --from=build /app ./

# Run as the image's non-root user. Nothing the API does needs to write to the filesystem.
USER $APP_UID

ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080

HEALTHCHECK --interval=10s --timeout=5s --start-period=30s --retries=6 \
    CMD ["/bin/sh", "-c", "curl -fsS http://localhost:8080/health/ready || exit 1"]

ENTRYPOINT ["dotnet", "SmartRail.Host.dll"]
