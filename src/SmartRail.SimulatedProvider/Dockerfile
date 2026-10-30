# Build the simulated provider, then run it on the ASP.NET Core runtime image. The SDK stage is never published.
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /source

# Restore first, with only the files that affect it, so a code change does not re-restore.
COPY global.json Directory.Build.props Directory.Packages.props ./ 
COPY src/SmartRail.SimulatedProvider/SmartRail.SimulatedProvider.csproj src/SmartRail.SimulatedProvider/
RUN dotnet restore src/SmartRail.SimulatedProvider/SmartRail.SimulatedProvider.csproj

COPY . .
RUN dotnet publish src/SmartRail.SimulatedProvider/SmartRail.SimulatedProvider.csproj \
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

# Run as the image's non-root user. The simulated provider keeps its state in memory, so it writes nothing.
USER $APP_UID

ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080

HEALTHCHECK --interval=10s --timeout=5s --start-period=20s --retries=6 \
    CMD ["/bin/sh", "-c", "curl -fsS http://localhost:8080/health || exit 1"]

ENTRYPOINT ["dotnet", "SmartRail.SimulatedProvider.dll"]
