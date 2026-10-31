# syntax=docker/dockerfile:1
# Build context: repository root (the root .editorconfig drives analyzers, which run as errors).

FROM mcr.microsoft.com/dotnet/sdk:10.0.403 AS build
WORKDIR /src
ENV DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1

# Restore first for layer caching; lock files make the restore reproducible.
COPY .editorconfig ./
COPY backend/global.json backend/Directory.Build.props backend/Directory.Packages.props backend/
COPY backend/src/InventoryOps.Domain/InventoryOps.Domain.csproj backend/src/InventoryOps.Domain/packages.lock.json backend/src/InventoryOps.Domain/
COPY backend/src/InventoryOps.Application/InventoryOps.Application.csproj backend/src/InventoryOps.Application/packages.lock.json backend/src/InventoryOps.Application/
COPY backend/src/InventoryOps.Infrastructure/InventoryOps.Infrastructure.csproj backend/src/InventoryOps.Infrastructure/packages.lock.json backend/src/InventoryOps.Infrastructure/
COPY backend/src/InventoryOps.Api/InventoryOps.Api.csproj backend/src/InventoryOps.Api/packages.lock.json backend/src/InventoryOps.Api/
RUN dotnet restore backend/src/InventoryOps.Api/InventoryOps.Api.csproj --locked-mode

COPY backend/src backend/src
RUN dotnet publish backend/src/InventoryOps.Api/InventoryOps.Api.csproj -c Release -o /app --no-restore /p:UseAppHost=false

# Chiseled Ubuntu: no shell or package manager, runs as the non-root "app" user by default.
# The "extra" variant includes ICU/tzdata required by SqlClient.
FROM mcr.microsoft.com/dotnet/aspnet:10.0.14-noble-chiseled-extra AS runtime
WORKDIR /app
ENV ASPNETCORE_HTTP_PORTS=8080 \
    DOTNET_CLI_TELEMETRY_OPTOUT=1
COPY --from=build /app .
USER $APP_UID
EXPOSE 8080
ENTRYPOINT ["dotnet", "InventoryOps.Api.dll"]
