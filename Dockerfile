# ── Build stage ────────────────────────────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /repo

COPY TriasDeparture.slnx .
COPY src/TriasDeparture.Domain/TriasDeparture.Domain.csproj             src/TriasDeparture.Domain/
COPY src/TriasDeparture.Application/TriasDeparture.Application.csproj   src/TriasDeparture.Application/
COPY src/TriasDeparture.Infrastructure/TriasDeparture.Infrastructure.csproj src/TriasDeparture.Infrastructure/
COPY src/TriasDeparture.Api/TriasDeparture.Api.csproj                   src/TriasDeparture.Api/
COPY tests/TriasDeparture.Tests/TriasDeparture.Tests.csproj             tests/TriasDeparture.Tests/

RUN dotnet restore

COPY . .

RUN dotnet test tests/TriasDeparture.Tests/TriasDeparture.Tests.csproj \
    --configuration Release \
    --no-restore \
    --logger "console;verbosity=minimal"

RUN dotnet publish src/TriasDeparture.Api/TriasDeparture.Api.csproj \
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
