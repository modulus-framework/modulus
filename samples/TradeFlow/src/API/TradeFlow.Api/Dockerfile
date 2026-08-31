FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS base
WORKDIR /app
EXPOSE 8080

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src

COPY ["Directory.Build.props", "./"]
COPY ["NuGet.config", "./"]
COPY ["nupkg/", "nupkg/"]

COPY ["src/API/ProcureFlow.Api/ProcureFlow.Api.csproj", "API/ProcureFlow.Api/"]

COPY ["src/Shared/ProcureFlow.Shared.Application/ProcureFlow.Shared.Application.csproj", "Shared/ProcureFlow.Shared.Application/"]
COPY ["src/Shared/ProcureFlow.Shared.Domain/ProcureFlow.Shared.Domain.csproj", "Shared/ProcureFlow.Shared.Domain/"]
COPY ["src/Shared/ProcureFlow.Shared.Infrastructure/ProcureFlow.Shared.Infrastructure.csproj", "Shared/ProcureFlow.Shared.Infrastructure/"]
COPY ["src/Shared/ProcureFlow.Shared.Presentation/ProcureFlow.Shared.Presentation.csproj", "Shared/ProcureFlow.Shared.Presentation/"]

COPY ["src/Modules/Identity/ProcureFlow.Modules.Identity.Domain/ProcureFlow.Modules.Identity.Domain.csproj", "Modules/Identity/ProcureFlow.Modules.Identity.Domain/"]
COPY ["src/Modules/Identity/ProcureFlow.Modules.Identity.Application/ProcureFlow.Modules.Identity.Application.csproj", "Modules/Identity/ProcureFlow.Modules.Identity.Application/"]
COPY ["src/Modules/Identity/ProcureFlow.Modules.Identity.Infrastructure/ProcureFlow.Modules.Identity.Infrastructure.csproj", "Modules/Identity/ProcureFlow.Modules.Identity.Infrastructure/"]
COPY ["src/Modules/Identity/ProcureFlow.Modules.Identity.Presentation/ProcureFlow.Modules.Identity.Presentation.csproj", "Modules/Identity/ProcureFlow.Modules.Identity.Presentation/"]

COPY ["src/Modules/Settings/ProcureFlow.Modules.Settings.Domain/ProcureFlow.Modules.Settings.Domain.csproj", "Modules/Settings/ProcureFlow.Modules.Settings.Domain/"]
COPY ["src/Modules/Settings/ProcureFlow.Modules.Settings.Application/ProcureFlow.Modules.Settings.Application.csproj", "Modules/Settings/ProcureFlow.Modules.Settings.Application/"]
COPY ["src/Modules/Settings/ProcureFlow.Modules.Settings.Infrastructure/ProcureFlow.Modules.Settings.Infrastructure.csproj", "Modules/Settings/ProcureFlow.Modules.Settings.Infrastructure/"]
COPY ["src/Modules/Settings/ProcureFlow.Modules.Settings.Presentation/ProcureFlow.Modules.Settings.Presentation.csproj", "Modules/Settings/ProcureFlow.Modules.Settings.Presentation/"]

COPY ["src/Modules/Tenants/ProcureFlow.Modules.Tenants.Domain/ProcureFlow.Modules.Tenants.Domain.csproj", "Modules/Tenants/ProcureFlow.Modules.Tenants.Domain/"]
COPY ["src/Modules/Tenants/ProcureFlow.Modules.Tenants.Application/ProcureFlow.Modules.Tenants.Application.csproj", "Modules/Tenants/ProcureFlow.Modules.Tenants.Application/"]
COPY ["src/Modules/Tenants/ProcureFlow.Modules.Tenants.Infrastructure/ProcureFlow.Modules.Tenants.Infrastructure.csproj", "Modules/Tenants/ProcureFlow.Modules.Tenants.Infrastructure/"]
COPY ["src/Modules/Tenants/ProcureFlow.Modules.Tenants.Presentation/ProcureFlow.Modules.Tenants.Presentation.csproj", "Modules/Tenants/ProcureFlow.Modules.Tenants.Presentation/"]

COPY ["src/Modules/Features/ProcureFlow.Modules.Features.Domain/ProcureFlow.Modules.Features.Domain.csproj", "Modules/Features/ProcureFlow.Modules.Features.Domain/"]
COPY ["src/Modules/Features/ProcureFlow.Modules.Features.Application/ProcureFlow.Modules.Features.Application.csproj", "Modules/Features/ProcureFlow.Modules.Features.Application/"]
COPY ["src/Modules/Features/ProcureFlow.Modules.Features.Infrastructure/ProcureFlow.Modules.Features.Infrastructure.csproj", "Modules/Features/ProcureFlow.Modules.Features.Infrastructure/"]
COPY ["src/Modules/Features/ProcureFlow.Modules.Features.Presentation/ProcureFlow.Modules.Features.Presentation.csproj", "Modules/Features/ProcureFlow.Modules.Features.Presentation/"]

COPY ["src/Modules/Notifications/ProcureFlow.Modules.Notifications.Domain/ProcureFlow.Modules.Notifications.Domain.csproj", "Modules/Notifications/ProcureFlow.Modules.Notifications.Domain/"]
COPY ["src/Modules/Notifications/ProcureFlow.Modules.Notifications.Application/ProcureFlow.Modules.Notifications.Application.csproj", "Modules/Notifications/ProcureFlow.Modules.Notifications.Application/"]
COPY ["src/Modules/Notifications/ProcureFlow.Modules.Notifications.Infrastructure/ProcureFlow.Modules.Notifications.Infrastructure.csproj", "Modules/Notifications/ProcureFlow.Modules.Notifications.Infrastructure/"]
COPY ["src/Modules/Notifications/ProcureFlow.Modules.Notifications.Presentation/ProcureFlow.Modules.Notifications.Presentation.csproj", "Modules/Notifications/ProcureFlow.Modules.Notifications.Presentation/"]

COPY ["src/Modules/Media/ProcureFlow.Modules.Media.Domain/ProcureFlow.Modules.Media.Domain.csproj", "Modules/Media/ProcureFlow.Modules.Media.Domain/"]
COPY ["src/Modules/Media/ProcureFlow.Modules.Media.Application/ProcureFlow.Modules.Media.Application.csproj", "Modules/Media/ProcureFlow.Modules.Media.Application/"]
COPY ["src/Modules/Media/ProcureFlow.Modules.Media.Infrastructure/ProcureFlow.Modules.Media.Infrastructure.csproj", "Modules/Media/ProcureFlow.Modules.Media.Infrastructure/"]
COPY ["src/Modules/Media/ProcureFlow.Modules.Media.Presentation/ProcureFlow.Modules.Media.Presentation.csproj", "Modules/Media/ProcureFlow.Modules.Media.Presentation/"]

COPY ["src/Modules/Catalog/ProcureFlow.Modules.Catalog.Domain/ProcureFlow.Modules.Catalog.Domain.csproj", "Modules/Catalog/ProcureFlow.Modules.Catalog.Domain/"]
COPY ["src/Modules/Catalog/ProcureFlow.Modules.Catalog.Application/ProcureFlow.Modules.Catalog.Application.csproj", "Modules/Catalog/ProcureFlow.Modules.Catalog.Application/"]
COPY ["src/Modules/Catalog/ProcureFlow.Modules.Catalog.Infrastructure/ProcureFlow.Modules.Catalog.Infrastructure.csproj", "Modules/Catalog/ProcureFlow.Modules.Catalog.Infrastructure/"]
COPY ["src/Modules/Catalog/ProcureFlow.Modules.Catalog.Presentation/ProcureFlow.Modules.Catalog.Presentation.csproj", "Modules/Catalog/ProcureFlow.Modules.Catalog.Presentation/"]

COPY ["src/Modules/Partners/ProcureFlow.Modules.Partners.Domain/ProcureFlow.Modules.Partners.Domain.csproj", "Modules/Partners/ProcureFlow.Modules.Partners.Domain/"]
COPY ["src/Modules/Partners/ProcureFlow.Modules.Partners.Application/ProcureFlow.Modules.Partners.Application.csproj", "Modules/Partners/ProcureFlow.Modules.Partners.Application/"]
COPY ["src/Modules/Partners/ProcureFlow.Modules.Partners.Infrastructure/ProcureFlow.Modules.Partners.Infrastructure.csproj", "Modules/Partners/ProcureFlow.Modules.Partners.Infrastructure/"]
COPY ["src/Modules/Partners/ProcureFlow.Modules.Partners.Presentation/ProcureFlow.Modules.Partners.Presentation.csproj", "Modules/Partners/ProcureFlow.Modules.Partners.Presentation/"]

COPY ["src/Modules/Inventory/ProcureFlow.Modules.Inventory.Domain/ProcureFlow.Modules.Inventory.Domain.csproj", "Modules/Inventory/ProcureFlow.Modules.Inventory.Domain/"]
COPY ["src/Modules/Inventory/ProcureFlow.Modules.Inventory.Application/ProcureFlow.Modules.Inventory.Application.csproj", "Modules/Inventory/ProcureFlow.Modules.Inventory.Application/"]
COPY ["src/Modules/Inventory/ProcureFlow.Modules.Inventory.Infrastructure/ProcureFlow.Modules.Inventory.Infrastructure.csproj", "Modules/Inventory/ProcureFlow.Modules.Inventory.Infrastructure/"]
COPY ["src/Modules/Inventory/ProcureFlow.Modules.Inventory.Presentation/ProcureFlow.Modules.Inventory.Presentation.csproj", "Modules/Inventory/ProcureFlow.Modules.Inventory.Presentation/"]

COPY ["src/Modules/Sales/ProcureFlow.Modules.Sales.Domain/ProcureFlow.Modules.Sales.Domain.csproj", "Modules/Sales/ProcureFlow.Modules.Sales.Domain/"]
COPY ["src/Modules/Sales/ProcureFlow.Modules.Sales.Application/ProcureFlow.Modules.Sales.Application.csproj", "Modules/Sales/ProcureFlow.Modules.Sales.Application/"]
COPY ["src/Modules/Sales/ProcureFlow.Modules.Sales.Infrastructure/ProcureFlow.Modules.Sales.Infrastructure.csproj", "Modules/Sales/ProcureFlow.Modules.Sales.Infrastructure/"]
COPY ["src/Modules/Sales/ProcureFlow.Modules.Sales.Presentation/ProcureFlow.Modules.Sales.Presentation.csproj", "Modules/Sales/ProcureFlow.Modules.Sales.Presentation/"]

COPY ["src/Modules/Purchasing/ProcureFlow.Modules.Purchasing.Domain/ProcureFlow.Modules.Purchasing.Domain.csproj", "Modules/Purchasing/ProcureFlow.Modules.Purchasing.Domain/"]
COPY ["src/Modules/Purchasing/ProcureFlow.Modules.Purchasing.Application/ProcureFlow.Modules.Purchasing.Application.csproj", "Modules/Purchasing/ProcureFlow.Modules.Purchasing.Application/"]
COPY ["src/Modules/Purchasing/ProcureFlow.Modules.Purchasing.Infrastructure/ProcureFlow.Modules.Purchasing.Infrastructure.csproj", "Modules/Purchasing/ProcureFlow.Modules.Purchasing.Infrastructure/"]
COPY ["src/Modules/Purchasing/ProcureFlow.Modules.Purchasing.Presentation/ProcureFlow.Modules.Purchasing.Presentation.csproj", "Modules/Purchasing/ProcureFlow.Modules.Purchasing.Presentation/"]

COPY ["src/Modules/Billing/ProcureFlow.Modules.Billing.Domain/ProcureFlow.Modules.Billing.Domain.csproj", "Modules/Billing/ProcureFlow.Modules.Billing.Domain/"]
COPY ["src/Modules/Billing/ProcureFlow.Modules.Billing.Application/ProcureFlow.Modules.Billing.Application.csproj", "Modules/Billing/ProcureFlow.Modules.Billing.Application/"]
COPY ["src/Modules/Billing/ProcureFlow.Modules.Billing.Infrastructure/ProcureFlow.Modules.Billing.Infrastructure.csproj", "Modules/Billing/ProcureFlow.Modules.Billing.Infrastructure/"]
COPY ["src/Modules/Billing/ProcureFlow.Modules.Billing.Presentation/ProcureFlow.Modules.Billing.Presentation.csproj", "Modules/Billing/ProcureFlow.Modules.Billing.Presentation/"]

RUN dotnet restore "./API/ProcureFlow.Api/ProcureFlow.Api.csproj"

COPY . .

WORKDIR "/src/src/API/ProcureFlow.Api"
RUN dotnet build "./ProcureFlow.Api.csproj" -c $BUILD_CONFIGURATION -o /app/build \
    -m:1 \
    /p:RuntimeIdentifier=linux-x64 \
    /p:TreatWarningsAsErrors=false \
    /p:CodeAnalysisTreatWarningsAsErrors=false

FROM build AS publish
ARG BUILD_CONFIGURATION=Release
RUN dotnet publish "./ProcureFlow.Api.csproj" -c $BUILD_CONFIGURATION -o /app/publish \
    -m:1 \
    /p:UseAppHost=false \
    /p:RuntimeIdentifier=linux-x64 \
    /p:TreatWarningsAsErrors=false \
    /p:CodeAnalysisTreatWarningsAsErrors=false

FROM base AS final
WORKDIR /app

USER root
RUN apt-get update && apt-get install -y \
    curl \
    libfontconfig1 \
    && rm -rf /var/lib/apt/lists/*

COPY --from=publish /app/publish .

RUN mkdir -p /app/data && chown -R app:app /app/data

RUN printf '#!/bin/sh\n\
set -e\n\
\n\
if [ "$1" = "--migrate" ]; then\n\
    echo "[entrypoint] Running database migrations only..."\n\
    exec dotnet ProcureFlow.Api.dll --migrate\n\
elif [ "$1" = "--seed" ]; then\n\
    echo "[entrypoint] Running data seeding only..."\n\
    exec dotnet ProcureFlow.Api.dll --seed\n\
else\n\
    if [ "$RUN_MIGRATIONS" = "true" ]; then\n\
        echo "[entrypoint] Running database migrations before starting API..."\n\
        dotnet ProcureFlow.Api.dll --migrate\n\
    fi\n\
    echo "[entrypoint] Starting API..."\n\
    exec dotnet ProcureFlow.Api.dll "$@"\n\
fi\n' > /app/entrypoint.sh && chmod +x /app/entrypoint.sh

USER app

ENTRYPOINT ["/app/entrypoint.sh"]
CMD []
