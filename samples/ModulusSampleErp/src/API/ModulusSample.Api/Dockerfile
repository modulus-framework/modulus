FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS base
WORKDIR /app
EXPOSE 8080

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src

COPY ["Directory.Build.props", "./"]
COPY ["NuGet.config", "./"]

COPY ["src/API/ModulusSample.Api/ModulusSample.Api.csproj", "API/ModulusSample.Api/"]

COPY ["src/Shared/ModulusSample.Shared.Application/ModulusSample.Shared.Application.csproj", "Shared/ModulusSample.Shared.Application/"]
COPY ["src/Shared/ModulusSample.Shared.Domain/ModulusSample.Shared.Domain.csproj", "Shared/ModulusSample.Shared.Domain/"]
COPY ["src/Shared/ModulusSample.Shared.Infrastructure/ModulusSample.Shared.Infrastructure.csproj", "Shared/ModulusSample.Shared.Infrastructure/"]
COPY ["src/Shared/ModulusSample.Shared.Presentation/ModulusSample.Shared.Presentation.csproj", "Shared/ModulusSample.Shared.Presentation/"]

COPY ["src/Modules/Identity/ModulusSample.Modules.Identity.Domain/ModulusSample.Modules.Identity.Domain.csproj", "Modules/Identity/ModulusSample.Modules.Identity.Domain/"]
COPY ["src/Modules/Identity/ModulusSample.Modules.Identity.Application/ModulusSample.Modules.Identity.Application.csproj", "Modules/Identity/ModulusSample.Modules.Identity.Application/"]
COPY ["src/Modules/Identity/ModulusSample.Modules.Identity.Infrastructure/ModulusSample.Modules.Identity.Infrastructure.csproj", "Modules/Identity/ModulusSample.Modules.Identity.Infrastructure/"]
COPY ["src/Modules/Identity/ModulusSample.Modules.Identity.Presentation/ModulusSample.Modules.Identity.Presentation.csproj", "Modules/Identity/ModulusSample.Modules.Identity.Presentation/"]

COPY ["src/Modules/Settings/ModulusSample.Modules.Settings.Domain/ModulusSample.Modules.Settings.Domain.csproj", "Modules/Settings/ModulusSample.Modules.Settings.Domain/"]
COPY ["src/Modules/Settings/ModulusSample.Modules.Settings.Application/ModulusSample.Modules.Settings.Application.csproj", "Modules/Settings/ModulusSample.Modules.Settings.Application/"]
COPY ["src/Modules/Settings/ModulusSample.Modules.Settings.Infrastructure/ModulusSample.Modules.Settings.Infrastructure.csproj", "Modules/Settings/ModulusSample.Modules.Settings.Infrastructure/"]
COPY ["src/Modules/Settings/ModulusSample.Modules.Settings.Presentation/ModulusSample.Modules.Settings.Presentation.csproj", "Modules/Settings/ModulusSample.Modules.Settings.Presentation/"]

COPY ["src/Modules/Tenants/ModulusSample.Modules.Tenants.Domain/ModulusSample.Modules.Tenants.Domain.csproj", "Modules/Tenants/ModulusSample.Modules.Tenants.Domain/"]
COPY ["src/Modules/Tenants/ModulusSample.Modules.Tenants.Application/ModulusSample.Modules.Tenants.Application.csproj", "Modules/Tenants/ModulusSample.Modules.Tenants.Application/"]
COPY ["src/Modules/Tenants/ModulusSample.Modules.Tenants.Infrastructure/ModulusSample.Modules.Tenants.Infrastructure.csproj", "Modules/Tenants/ModulusSample.Modules.Tenants.Infrastructure/"]
COPY ["src/Modules/Tenants/ModulusSample.Modules.Tenants.Presentation/ModulusSample.Modules.Tenants.Presentation.csproj", "Modules/Tenants/ModulusSample.Modules.Tenants.Presentation/"]

COPY ["src/Modules/Features/ModulusSample.Modules.Features.Domain/ModulusSample.Modules.Features.Domain.csproj", "Modules/Features/ModulusSample.Modules.Features.Domain/"]
COPY ["src/Modules/Features/ModulusSample.Modules.Features.Application/ModulusSample.Modules.Features.Application.csproj", "Modules/Features/ModulusSample.Modules.Features.Application/"]
COPY ["src/Modules/Features/ModulusSample.Modules.Features.Infrastructure/ModulusSample.Modules.Features.Infrastructure.csproj", "Modules/Features/ModulusSample.Modules.Features.Infrastructure/"]
COPY ["src/Modules/Features/ModulusSample.Modules.Features.Presentation/ModulusSample.Modules.Features.Presentation.csproj", "Modules/Features/ModulusSample.Modules.Features.Presentation/"]

COPY ["src/Modules/VirtualFileExplorer/ModulusSample.Modules.VirtualFileExplorer.Domain/ModulusSample.Modules.VirtualFileExplorer.Domain.csproj", "Modules/VirtualFileExplorer/ModulusSample.Modules.VirtualFileExplorer.Domain/"]
COPY ["src/Modules/VirtualFileExplorer/ModulusSample.Modules.VirtualFileExplorer.Application/ModulusSample.Modules.VirtualFileExplorer.Application.csproj", "Modules/VirtualFileExplorer/ModulusSample.Modules.VirtualFileExplorer.Application/"]
COPY ["src/Modules/VirtualFileExplorer/ModulusSample.Modules.VirtualFileExplorer.Infrastructure/ModulusSample.Modules.VirtualFileExplorer.Infrastructure.csproj", "Modules/VirtualFileExplorer/ModulusSample.Modules.VirtualFileExplorer.Infrastructure/"]
COPY ["src/Modules/VirtualFileExplorer/ModulusSample.Modules.VirtualFileExplorer.Presentation/ModulusSample.Modules.VirtualFileExplorer.Presentation.csproj", "Modules/VirtualFileExplorer/ModulusSample.Modules.VirtualFileExplorer.Presentation/"]

COPY ["src/Modules/Notifications/ModulusSample.Modules.Notifications.Domain/ModulusSample.Modules.Notifications.Domain.csproj", "Modules/Notifications/ModulusSample.Modules.Notifications.Domain/"]
COPY ["src/Modules/Notifications/ModulusSample.Modules.Notifications.Application/ModulusSample.Modules.Notifications.Application.csproj", "Modules/Notifications/ModulusSample.Modules.Notifications.Application/"]
COPY ["src/Modules/Notifications/ModulusSample.Modules.Notifications.Infrastructure/ModulusSample.Modules.Notifications.Infrastructure.csproj", "Modules/Notifications/ModulusSample.Modules.Notifications.Infrastructure/"]
COPY ["src/Modules/Notifications/ModulusSample.Modules.Notifications.Presentation/ModulusSample.Modules.Notifications.Presentation.csproj", "Modules/Notifications/ModulusSample.Modules.Notifications.Presentation/"]

COPY ["src/Modules/ModulusSample.Modules.Media/ModulusSample.Modules.Media.Domain/ModulusSample.Modules.Media.Domain.csproj", "Modules/Media/ModulusSample.Modules.Media.Domain/"]
COPY ["src/Modules/ModulusSample.Modules.Media/ModulusSample.Modules.Media.Application/ModulusSample.Modules.Media.Application.csproj", "Modules/Media/ModulusSample.Modules.Media.Application/"]
COPY ["src/Modules/ModulusSample.Modules.Media/ModulusSample.Modules.Media.Infrastructure/ModulusSample.Modules.Media.Infrastructure.csproj", "Modules/Media/ModulusSample.Modules.Media.Infrastructure/"]
COPY ["src/Modules/ModulusSample.Modules.Media/ModulusSample.Modules.Media.Presentation/ModulusSample.Modules.Media.Presentation.csproj", "Modules/Media/ModulusSample.Modules.Media.Presentation/"]

RUN dotnet restore "./API/ModulusSample.Api/ModulusSample.Api.csproj"

COPY . .

WORKDIR "/src/src/API/ModulusSample.Api"
RUN dotnet build "./ModulusSample.Api.csproj" -c $BUILD_CONFIGURATION -o /app/build \
    -m:1 \
    /p:RuntimeIdentifier=linux-x64 \
    /p:TreatWarningsAsErrors=false \
    /p:CodeAnalysisTreatWarningsAsErrors=false

FROM build AS publish
ARG BUILD_CONFIGURATION=Release
RUN dotnet publish "./ModulusSample.Api.csproj" -c $BUILD_CONFIGURATION -o /app/publish \
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
    exec dotnet ModulusSample.Api.dll --migrate\n\
elif [ "$1" = "--seed" ]; then\n\
    echo "[entrypoint] Running data seeding only..."\n\
    exec dotnet ModulusSample.Api.dll --seed\n\
else\n\
    if [ "$RUN_MIGRATIONS" = "true" ]; then\n\
        echo "[entrypoint] Running database migrations before starting API..."\n\
        dotnet ModulusSample.Api.dll --migrate\n\
    fi\n\
    echo "[entrypoint] Starting API..."\n\
    exec dotnet ModulusSample.Api.dll "$@"\n\
fi\n' > /app/entrypoint.sh && chmod +x /app/entrypoint.sh

USER app

ENTRYPOINT ["/app/entrypoint.sh"]
CMD []
