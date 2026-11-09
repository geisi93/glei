# glei

Live-Abfahrten für Bus und Bim in der Steiermark („Der 3er kommt glei.“) auf Basis der [TRIAS-Schnittstelle](https://www.verbundlinie.at/de/kundenservice/weitere-infostellen/faqs-hilfe/faq-zur-ogd-service-schnittstelle-trias) der Verbund Linie.

## Aufbau

| Ordner | Inhalt |
|--------|--------|
| [`core/`](core) | Gemeinsamer TRIAS-Code: Modelle, Services, TRIAS-Client (XML-Builder/-Parser, Caching) + Tests |
| [`api/`](api) | REST API – [Doku](api/README.md) |
| [`alexa/`](alexa) | Alexa-Skill („Alexa, frag Bim Graz …“) – [Einrichtung](alexa/README.md) |

```
api/TriasDeparture.Api            (Controller, Program.cs, Swagger)
  │
  ▼
core/TriasDeparture.Application   (DepartureService)
  │
  ▼
core/TriasDeparture.Infrastructure (TriasHttpClient, XmlBuilder, XmlParser)
  │
  ▼
TRIAS (Verbund Linie)
```

### Caching (core)

| Daten | Cache-Key | TTL |
|-------|-----------|-----|
| Haltestellen | `stops:{query}` | 24 Stunden |
| Abfahrten | `departures:{stopId}` | 15 Sekunden |

## Entwicklung

```bash
dotnet build
dotnet test
```

Voraussetzung: .NET 10 SDK. Tests: xUnit + Shouldly.

## Lizenz

[MIT](LICENSE)
