# Build context is the repository root: docker build -f api/Dockerfile .

# ── Build stage ────────────────────────────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /repo

COPY core/TriasDeparture.Domain/TriasDeparture.Domain.csproj             core/TriasDeparture.Domain/
COPY core/TriasDeparture.Application/TriasDeparture.Application.csproj   core/TriasDeparture.Application/
COPY core/TriasDeparture.Infrastructure/TriasDeparture.Infrastructure.csproj core/TriasDeparture.Infrastructure/
COPY core/TriasDeparture.Tests/TriasDeparture.Tests.csproj               core/TriasDeparture.Tests/
COPY api/TriasDeparture.Api/TriasDeparture.Api.csproj                    api/TriasDeparture.Api/

RUN dotnet restore api/TriasDeparture.Api/TriasDeparture.Api.csproj \
 && dotnet restore core/TriasDeparture.Tests/TriasDeparture.Tests.csproj

COPY core/ core/
COPY api/  api/

RUN dotnet test core/TriasDeparture.Tests/TriasDeparture.Tests.csproj \
    --configuration Release \
    --no-restore \
    --logger "console;verbosity=minimal"

RUN dotnet publish api/TriasDeparture.Api/TriasDeparture.Api.csproj \
    --configuration Release \
    --no-restore \
    --output /app/publish

# ── Runtime stage ──────────────────────────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

COPY --from=build /app/publish .

ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080

ENTRYPOINT ["dotnet", "TriasDeparture.Api.dll"]
