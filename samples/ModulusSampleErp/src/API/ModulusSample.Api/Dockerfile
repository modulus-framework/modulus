FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS base
WORKDIR /app
EXPOSE 8080

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src

COPY ["Directory.Build.props", "./"]
COPY ["Directory.Packages.props", "./"]

COPY ["src/API/ModulusSample.Api/ModulusSample.Api.csproj", "API/ModulusSample.Api/"]

COPY ["src/Shared/ModulusSample.Shared.Application/ModulusSample.Shared.Application.csproj", "Shared/ModulusSample.Shared.Application/"]
COPY ["src/Shared/ModulusSample.Shared.Domain/ModulusSample.Shared.Domain.csproj", "Shared/ModulusSample.Shared.Domain/"]
COPY ["src/Shared/ModulusSample.Shared.Infrastructure/ModulusSample.Shared.Infrastructure.csproj", "Shared/ModulusSample.Shared.Infrastructure/"]
COPY ["src/Shared/ModulusSample.Shared.Presentation/ModulusSample.Shared.Presentation.csproj", "Shared/ModulusSample.Shared.Presentation/"]

COPY ["src/Modules/Users/ModulusSample.Modules.Users.Domain/ModulusSample.Modules.Users.Domain.csproj", "Modules/Users/ModulusSample.Modules.Users.Domain/"]
COPY ["src/Modules/Users/ModulusSample.Modules.Users.Application/ModulusSample.Modules.Users.Application.csproj", "Modules/Users/ModulusSample.Modules.Users.Application/"]
COPY ["src/Modules/Users/ModulusSample.Modules.Users.Infrastructure/ModulusSample.Modules.Users.Infrastructure.csproj", "Modules/Users/ModulusSample.Modules.Users.Infrastructure/"]
COPY ["src/Modules/Users/ModulusSample.Modules.Users.Presentation/ModulusSample.Modules.Users.Presentation.csproj", "Modules/Users/ModulusSample.Modules.Users.Presentation/"]

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
