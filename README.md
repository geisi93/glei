# glei

Live-Abfahrten für Bus und Bim in der Steiermark („Der 3er kommt glei.“) – eine schlanke ASP.NET Core 10 Web API als Fassade vor der [TRIAS-Schnittstelle](https://www.verbundlinie.at/de/kundenservice/weitere-infostellen/faqs-hilfe/faq-zur-ogd-service-schnittstelle-trias) der Verbund Linie.

## Endpunkte

| Methode | Pfad | Beschreibung |
|---------|------|--------------|
| GET | `/api/stops/search?query=krenngasse` | Haltestellen suchen |
| GET | `/api/departures/{stopId}` | Alle Abfahrten einer Haltestelle |
| GET | `/api/departures/{stopId}/line/{line}` | Abfahrten gefiltert nach Linie |

Beispiel-Abfahrt (alle Zeiten in UTC, `minutes` wird bei jedem Aufruf neu berechnet):

```json
{
  "line": "3",
  "destination": "Andritz",
  "minutes": 4,
  "plannedTime": "2026-10-07T16:05:00Z",
  "estimatedTime": "2026-10-07T16:06:00Z",
  "mode": "tram",
  "cancelled": false
}
```

Ist TRIAS nicht erreichbar, antwortet die API mit `502` (Fehler) bzw. `504` (Timeout nach 10 s) als ProblemDetails.

## Konfiguration

Erstelle `src/TriasDeparture.Api/appsettings.Development.json` (wird per `.gitignore` nicht eingecheckt) und befülle:

```json
{
  "Trias": {
    "BaseUrl": "https://<endpoint>/trias"
  }
}
```

> Die Endpoint-URL erhältst du per Mail an `ogdtrias@verbundlinie.at`. Es sind keine Zugangsdaten nötig.
>
> `BaseUrl` wird beim Start validiert – fehlt sie, startet die API nicht.
> Requests werden mit `Content-Type: text/xml` gesendet (von TRIAS vorausgesetzt).

## Starten (lokal)

```bash
dotnet run --project src/TriasDeparture.Api
```

Swagger UI ist unter `http://localhost:<port>/` erreichbar (nur Development).

## Docker

```bash
docker build -t glei .
docker run -p 8080:8080 \
  -e Trias__BaseUrl="https://..." \
  glei
```

## Tests

```bash
dotnet test
```

## Architektur

```
Client
  │
  ▼
TriasDeparture.Api          (Controller, Program.cs, Swagger)
  │
  ▼
TriasDeparture.Application  (DepartureService)
  │
  ▼
TriasDeparture.Infrastructure (TriasHttpClient, XmlBuilder, XmlParser)
  │
  ▼
TRIAS API (Verbund Linie)
```

### Caching

| Daten | Cache-Key | TTL |
|-------|-----------|-----|
| Haltestellen | `stops:{query}` | 24 Stunden |
| Abfahrten | `departures:{stopId}` | 15 Sekunden |

## Technologiestack

- .NET 10 / ASP.NET Core
- Swashbuckle (Swagger UI)
- Serilog
- IMemoryCache
- xUnit + Shouldly
