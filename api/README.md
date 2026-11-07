# glei API

Schlanke ASP.NET Core 10 Web API als Fassade vor der [TRIAS-Schnittstelle](https://www.verbundlinie.at/de/kundenservice/weitere-infostellen/faqs-hilfe/faq-zur-ogd-service-schnittstelle-trias) der Verbund Linie.

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

Erstelle `api/TriasDeparture.Api/appsettings.Development.json` (wird per `.gitignore` nicht eingecheckt) und befülle:

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

## Starten (lokal)

```bash
dotnet run --project api/TriasDeparture.Api
```

Swagger UI ist unter `http://localhost:<port>/` erreichbar (nur Development).

## Docker

Build-Kontext ist das Repo-Root (die API nutzt den Code aus `core/`):

```bash
docker build -f api/Dockerfile -t glei .
docker run -p 8080:8080 \
  -e Trias__BaseUrl="https://..." \
  glei
```

## Deployment

Google Cloud Run (Service `glei`, Region `europe-west1`), automatisch bei jedem Push auf `main`, der `api/**` oder `core/**` ändert.

## Technologiestack

- .NET 10 / ASP.NET Core
- Swashbuckle (Swagger UI)
- Serilog
