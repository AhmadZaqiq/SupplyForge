# NFR-061: container build target for CI (and the eventual NFR-060 compose
# app service). Multi-stage: SDK builds, aspnet runtime runs.

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY . .
RUN dotnet publish src/Host/SupplyForge.Host/SupplyForge.Host.csproj \
    -c Release -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build /app/publish .

# The aspnet base image listens on 8080 (ASPNETCORE_HTTP_PORTS default).
EXPOSE 8080

# Connection string, JWT settings, and rate limits arrive as environment
# variables (ConnectionStrings__SupplyForge, Auth__*, RateLimit__*) — never
# baked into the image (SEC-20).
ENTRYPOINT ["dotnet", "SupplyForge.Host.dll"]
