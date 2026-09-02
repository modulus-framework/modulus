# Multi-stage build for the TradeFlow API.
# Build context is the sample root (DockerfileContext = ../..) so it picks up
# NuGet.config, Directory.Build.props and the local Modulus package feed (nupkg/).

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS base
WORKDIR /app
EXPOSE 8080

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src

# Solution-wide build props + the local Modulus NuGet feed. Directory.Packages.props
# is required at restore time (it sets ManagePackageVersionsCentrally=false, stopping
# the SDK from walking up into a parent repo's CPM props).
COPY ["Directory.Build.props", "./"]
COPY ["Directory.Packages.props", "./"]
COPY ["NuGet.config", "./"]
COPY ["nupkg/", "nupkg/"]

COPY ["src/API/TradeFlow.Api/TradeFlow.Api.csproj", "API/TradeFlow.Api/"]

COPY ["src/Shared/TradeFlow.Shared.Application/TradeFlow.Shared.Application.csproj", "Shared/TradeFlow.Shared.Application/"]
COPY ["src/Shared/TradeFlow.Shared.Domain/TradeFlow.Shared.Domain.csproj", "Shared/TradeFlow.Shared.Domain/"]
COPY ["src/Shared/TradeFlow.Shared.Infrastructure/TradeFlow.Shared.Infrastructure.csproj", "Shared/TradeFlow.Shared.Infrastructure/"]
COPY ["src/Shared/TradeFlow.Shared.Presentation/TradeFlow.Shared.Presentation.csproj", "Shared/TradeFlow.Shared.Presentation/"]

COPY ["src/Modules/Budgeting/TradeFlow.Modules.Budgeting.Domain/TradeFlow.Modules.Budgeting.Domain.csproj", "Modules/Budgeting/TradeFlow.Modules.Budgeting.Domain/"]
COPY ["src/Modules/Budgeting/TradeFlow.Modules.Budgeting.Application/TradeFlow.Modules.Budgeting.Application.csproj", "Modules/Budgeting/TradeFlow.Modules.Budgeting.Application/"]
COPY ["src/Modules/Budgeting/TradeFlow.Modules.Budgeting.Infrastructure/TradeFlow.Modules.Budgeting.Infrastructure.csproj", "Modules/Budgeting/TradeFlow.Modules.Budgeting.Infrastructure/"]
COPY ["src/Modules/Budgeting/TradeFlow.Modules.Budgeting.Presentation/TradeFlow.Modules.Budgeting.Presentation.csproj", "Modules/Budgeting/TradeFlow.Modules.Budgeting.Presentation/"]

COPY ["src/Modules/Configuration/TradeFlow.Modules.Configuration.Domain/TradeFlow.Modules.Configuration.Domain.csproj", "Modules/Configuration/TradeFlow.Modules.Configuration.Domain/"]
COPY ["src/Modules/Configuration/TradeFlow.Modules.Configuration.Application/TradeFlow.Modules.Configuration.Application.csproj", "Modules/Configuration/TradeFlow.Modules.Configuration.Application/"]
COPY ["src/Modules/Configuration/TradeFlow.Modules.Configuration.Infrastructure/TradeFlow.Modules.Configuration.Infrastructure.csproj", "Modules/Configuration/TradeFlow.Modules.Configuration.Infrastructure/"]
COPY ["src/Modules/Configuration/TradeFlow.Modules.Configuration.Presentation/TradeFlow.Modules.Configuration.Presentation.csproj", "Modules/Configuration/TradeFlow.Modules.Configuration.Presentation/"]

COPY ["src/Modules/Costing/TradeFlow.Modules.Costing.Domain/TradeFlow.Modules.Costing.Domain.csproj", "Modules/Costing/TradeFlow.Modules.Costing.Domain/"]
COPY ["src/Modules/Costing/TradeFlow.Modules.Costing.Application/TradeFlow.Modules.Costing.Application.csproj", "Modules/Costing/TradeFlow.Modules.Costing.Application/"]
COPY ["src/Modules/Costing/TradeFlow.Modules.Costing.Infrastructure/TradeFlow.Modules.Costing.Infrastructure.csproj", "Modules/Costing/TradeFlow.Modules.Costing.Infrastructure/"]
COPY ["src/Modules/Costing/TradeFlow.Modules.Costing.Presentation/TradeFlow.Modules.Costing.Presentation.csproj", "Modules/Costing/TradeFlow.Modules.Costing.Presentation/"]

COPY ["src/Modules/Customs/TradeFlow.Modules.Customs.Domain/TradeFlow.Modules.Customs.Domain.csproj", "Modules/Customs/TradeFlow.Modules.Customs.Domain/"]
COPY ["src/Modules/Customs/TradeFlow.Modules.Customs.Application/TradeFlow.Modules.Customs.Application.csproj", "Modules/Customs/TradeFlow.Modules.Customs.Application/"]
COPY ["src/Modules/Customs/TradeFlow.Modules.Customs.Infrastructure/TradeFlow.Modules.Customs.Infrastructure.csproj", "Modules/Customs/TradeFlow.Modules.Customs.Infrastructure/"]
COPY ["src/Modules/Customs/TradeFlow.Modules.Customs.Presentation/TradeFlow.Modules.Customs.Presentation.csproj", "Modules/Customs/TradeFlow.Modules.Customs.Presentation/"]

COPY ["src/Modules/Finance/TradeFlow.Modules.Finance.Domain/TradeFlow.Modules.Finance.Domain.csproj", "Modules/Finance/TradeFlow.Modules.Finance.Domain/"]
COPY ["src/Modules/Finance/TradeFlow.Modules.Finance.Application/TradeFlow.Modules.Finance.Application.csproj", "Modules/Finance/TradeFlow.Modules.Finance.Application/"]
COPY ["src/Modules/Finance/TradeFlow.Modules.Finance.Infrastructure/TradeFlow.Modules.Finance.Infrastructure.csproj", "Modules/Finance/TradeFlow.Modules.Finance.Infrastructure/"]
COPY ["src/Modules/Finance/TradeFlow.Modules.Finance.Presentation/TradeFlow.Modules.Finance.Presentation.csproj", "Modules/Finance/TradeFlow.Modules.Finance.Presentation/"]

COPY ["src/Modules/Identity/TradeFlow.Modules.Identity.Domain/TradeFlow.Modules.Identity.Domain.csproj", "Modules/Identity/TradeFlow.Modules.Identity.Domain/"]
COPY ["src/Modules/Identity/TradeFlow.Modules.Identity.Application/TradeFlow.Modules.Identity.Application.csproj", "Modules/Identity/TradeFlow.Modules.Identity.Application/"]
COPY ["src/Modules/Identity/TradeFlow.Modules.Identity.Infrastructure/TradeFlow.Modules.Identity.Infrastructure.csproj", "Modules/Identity/TradeFlow.Modules.Identity.Infrastructure/"]
COPY ["src/Modules/Identity/TradeFlow.Modules.Identity.Presentation/TradeFlow.Modules.Identity.Presentation.csproj", "Modules/Identity/TradeFlow.Modules.Identity.Presentation/"]

COPY ["src/Modules/Import/TradeFlow.Modules.Import.Domain/TradeFlow.Modules.Import.Domain.csproj", "Modules/Import/TradeFlow.Modules.Import.Domain/"]
COPY ["src/Modules/Import/TradeFlow.Modules.Import.Application/TradeFlow.Modules.Import.Application.csproj", "Modules/Import/TradeFlow.Modules.Import.Application/"]
COPY ["src/Modules/Import/TradeFlow.Modules.Import.Infrastructure/TradeFlow.Modules.Import.Infrastructure.csproj", "Modules/Import/TradeFlow.Modules.Import.Infrastructure/"]
COPY ["src/Modules/Import/TradeFlow.Modules.Import.Presentation/TradeFlow.Modules.Import.Presentation.csproj", "Modules/Import/TradeFlow.Modules.Import.Presentation/"]

COPY ["src/Modules/Inventory/TradeFlow.Modules.Inventory.Domain/TradeFlow.Modules.Inventory.Domain.csproj", "Modules/Inventory/TradeFlow.Modules.Inventory.Domain/"]
COPY ["src/Modules/Inventory/TradeFlow.Modules.Inventory.Application/TradeFlow.Modules.Inventory.Application.csproj", "Modules/Inventory/TradeFlow.Modules.Inventory.Application/"]
COPY ["src/Modules/Inventory/TradeFlow.Modules.Inventory.Infrastructure/TradeFlow.Modules.Inventory.Infrastructure.csproj", "Modules/Inventory/TradeFlow.Modules.Inventory.Infrastructure/"]
COPY ["src/Modules/Inventory/TradeFlow.Modules.Inventory.Presentation/TradeFlow.Modules.Inventory.Presentation.csproj", "Modules/Inventory/TradeFlow.Modules.Inventory.Presentation/"]

COPY ["src/Modules/Notifications/TradeFlow.Modules.Notifications.Domain/TradeFlow.Modules.Notifications.Domain.csproj", "Modules/Notifications/TradeFlow.Modules.Notifications.Domain/"]
COPY ["src/Modules/Notifications/TradeFlow.Modules.Notifications.Application/TradeFlow.Modules.Notifications.Application.csproj", "Modules/Notifications/TradeFlow.Modules.Notifications.Application/"]
COPY ["src/Modules/Notifications/TradeFlow.Modules.Notifications.Infrastructure/TradeFlow.Modules.Notifications.Infrastructure.csproj", "Modules/Notifications/TradeFlow.Modules.Notifications.Infrastructure/"]
COPY ["src/Modules/Notifications/TradeFlow.Modules.Notifications.Presentation/TradeFlow.Modules.Notifications.Presentation.csproj", "Modules/Notifications/TradeFlow.Modules.Notifications.Presentation/"]

COPY ["src/Modules/OrgStructure/TradeFlow.Modules.OrgStructure.Domain/TradeFlow.Modules.OrgStructure.Domain.csproj", "Modules/OrgStructure/TradeFlow.Modules.OrgStructure.Domain/"]
COPY ["src/Modules/OrgStructure/TradeFlow.Modules.OrgStructure.Application/TradeFlow.Modules.OrgStructure.Application.csproj", "Modules/OrgStructure/TradeFlow.Modules.OrgStructure.Application/"]
COPY ["src/Modules/OrgStructure/TradeFlow.Modules.OrgStructure.Infrastructure/TradeFlow.Modules.OrgStructure.Infrastructure.csproj", "Modules/OrgStructure/TradeFlow.Modules.OrgStructure.Infrastructure/"]
COPY ["src/Modules/OrgStructure/TradeFlow.Modules.OrgStructure.Presentation/TradeFlow.Modules.OrgStructure.Presentation.csproj", "Modules/OrgStructure/TradeFlow.Modules.OrgStructure.Presentation/"]

COPY ["src/Modules/Procurement/TradeFlow.Modules.Procurement.Domain/TradeFlow.Modules.Procurement.Domain.csproj", "Modules/Procurement/TradeFlow.Modules.Procurement.Domain/"]
COPY ["src/Modules/Procurement/TradeFlow.Modules.Procurement.Application/TradeFlow.Modules.Procurement.Application.csproj", "Modules/Procurement/TradeFlow.Modules.Procurement.Application/"]
COPY ["src/Modules/Procurement/TradeFlow.Modules.Procurement.Infrastructure/TradeFlow.Modules.Procurement.Infrastructure.csproj", "Modules/Procurement/TradeFlow.Modules.Procurement.Infrastructure/"]
COPY ["src/Modules/Procurement/TradeFlow.Modules.Procurement.Presentation/TradeFlow.Modules.Procurement.Presentation.csproj", "Modules/Procurement/TradeFlow.Modules.Procurement.Presentation/"]

COPY ["src/Modules/Tenants/TradeFlow.Modules.Tenants.Domain/TradeFlow.Modules.Tenants.Domain.csproj", "Modules/Tenants/TradeFlow.Modules.Tenants.Domain/"]
COPY ["src/Modules/Tenants/TradeFlow.Modules.Tenants.Application/TradeFlow.Modules.Tenants.Application.csproj", "Modules/Tenants/TradeFlow.Modules.Tenants.Application/"]
COPY ["src/Modules/Tenants/TradeFlow.Modules.Tenants.Infrastructure/TradeFlow.Modules.Tenants.Infrastructure.csproj", "Modules/Tenants/TradeFlow.Modules.Tenants.Infrastructure/"]
COPY ["src/Modules/Tenants/TradeFlow.Modules.Tenants.Presentation/TradeFlow.Modules.Tenants.Presentation.csproj", "Modules/Tenants/TradeFlow.Modules.Tenants.Presentation/"]

COPY ["src/Modules/TradeFinance/TradeFlow.Modules.TradeFinance.Domain/TradeFlow.Modules.TradeFinance.Domain.csproj", "Modules/TradeFinance/TradeFlow.Modules.TradeFinance.Domain/"]
COPY ["src/Modules/TradeFinance/TradeFlow.Modules.TradeFinance.Application/TradeFlow.Modules.TradeFinance.Application.csproj", "Modules/TradeFinance/TradeFlow.Modules.TradeFinance.Application/"]
COPY ["src/Modules/TradeFinance/TradeFlow.Modules.TradeFinance.Infrastructure/TradeFlow.Modules.TradeFinance.Infrastructure.csproj", "Modules/TradeFinance/TradeFlow.Modules.TradeFinance.Infrastructure/"]
COPY ["src/Modules/TradeFinance/TradeFlow.Modules.TradeFinance.Presentation/TradeFlow.Modules.TradeFinance.Presentation.csproj", "Modules/TradeFinance/TradeFlow.Modules.TradeFinance.Presentation/"]

COPY ["src/Modules/Vendors/TradeFlow.Modules.Vendors.Domain/TradeFlow.Modules.Vendors.Domain.csproj", "Modules/Vendors/TradeFlow.Modules.Vendors.Domain/"]
COPY ["src/Modules/Vendors/TradeFlow.Modules.Vendors.Application/TradeFlow.Modules.Vendors.Application.csproj", "Modules/Vendors/TradeFlow.Modules.Vendors.Application/"]
COPY ["src/Modules/Vendors/TradeFlow.Modules.Vendors.PublicApi/TradeFlow.Modules.Vendors.PublicApi.csproj", "Modules/Vendors/TradeFlow.Modules.Vendors.PublicApi/"]
COPY ["src/Modules/Vendors/TradeFlow.Modules.Vendors.Infrastructure/TradeFlow.Modules.Vendors.Infrastructure.csproj", "Modules/Vendors/TradeFlow.Modules.Vendors.Infrastructure/"]
COPY ["src/Modules/Vendors/TradeFlow.Modules.Vendors.Presentation/TradeFlow.Modules.Vendors.Presentation.csproj", "Modules/Vendors/TradeFlow.Modules.Vendors.Presentation/"]

COPY ["src/Modules/VirtualFileExplorer/TradeFlow.Modules.VirtualFileExplorer.Domain/TradeFlow.Modules.VirtualFileExplorer.Domain.csproj", "Modules/VirtualFileExplorer/TradeFlow.Modules.VirtualFileExplorer.Domain/"]
COPY ["src/Modules/VirtualFileExplorer/TradeFlow.Modules.VirtualFileExplorer.Application/TradeFlow.Modules.VirtualFileExplorer.Application.csproj", "Modules/VirtualFileExplorer/TradeFlow.Modules.VirtualFileExplorer.Application/"]
COPY ["src/Modules/VirtualFileExplorer/TradeFlow.Modules.VirtualFileExplorer.Infrastructure/TradeFlow.Modules.VirtualFileExplorer.Infrastructure.csproj", "Modules/VirtualFileExplorer/TradeFlow.Modules.VirtualFileExplorer.Infrastructure/"]
COPY ["src/Modules/VirtualFileExplorer/TradeFlow.Modules.VirtualFileExplorer.Presentation/TradeFlow.Modules.VirtualFileExplorer.Presentation.csproj", "Modules/VirtualFileExplorer/TradeFlow.Modules.VirtualFileExplorer.Presentation/"]

COPY ["src/Modules/WorkflowEngine/TradeFlow.Modules.WorkflowEngine.Domain/TradeFlow.Modules.WorkflowEngine.Domain.csproj", "Modules/WorkflowEngine/TradeFlow.Modules.WorkflowEngine.Domain/"]
COPY ["src/Modules/WorkflowEngine/TradeFlow.Modules.WorkflowEngine.Application/TradeFlow.Modules.WorkflowEngine.Application.csproj", "Modules/WorkflowEngine/TradeFlow.Modules.WorkflowEngine.Application/"]
COPY ["src/Modules/WorkflowEngine/TradeFlow.Modules.WorkflowEngine.Infrastructure/TradeFlow.Modules.WorkflowEngine.Infrastructure.csproj", "Modules/WorkflowEngine/TradeFlow.Modules.WorkflowEngine.Infrastructure/"]
COPY ["src/Modules/WorkflowEngine/TradeFlow.Modules.WorkflowEngine.Presentation/TradeFlow.Modules.WorkflowEngine.Presentation.csproj", "Modules/WorkflowEngine/TradeFlow.Modules.WorkflowEngine.Presentation/"]

RUN dotnet restore "./API/TradeFlow.Api/TradeFlow.Api.csproj"

COPY . .

WORKDIR "/src/src/API/TradeFlow.Api"
RUN dotnet publish "./TradeFlow.Api.csproj" -c $BUILD_CONFIGURATION -o /app/publish \
    -m:1 \
    /p:UseAppHost=false \
    /p:RuntimeIdentifier=linux-x64

FROM base AS final
WORKDIR /app

USER root
RUN apt-get update && apt-get install -y \
    curl \
    && rm -rf /var/lib/apt/lists/*

COPY --from=build /app/publish .

RUN mkdir -p /app/data && chown -R app:app /app/data

# Entrypoint: --migrate runs DB migrations then exits, --seed runs migrations +
# seeding then exits, otherwise (optionally migrate first) start the API.
RUN printf '#!/bin/sh\n\
set -e\n\
\n\
if [ "$1" = "--migrate" ]; then\n\
    echo "[entrypoint] Running database migrations only..."\n\
    exec dotnet TradeFlow.Api.dll --migrate\n\
elif [ "$1" = "--seed" ]; then\n\
    echo "[entrypoint] Running data seeding only..."\n\
    exec dotnet TradeFlow.Api.dll --seed\n\
else\n\
    if [ "$RUN_MIGRATIONS" = "true" ]; then\n\
        echo "[entrypoint] Running database migrations before starting API..."\n\
        dotnet TradeFlow.Api.dll --migrate\n\
    fi\n\
    echo "[entrypoint] Starting API..."\n\
    exec dotnet TradeFlow.Api.dll "$@"\n\
fi\n' > /app/entrypoint.sh && chmod +x /app/entrypoint.sh

USER app

ENTRYPOINT ["/app/entrypoint.sh"]
CMD []
