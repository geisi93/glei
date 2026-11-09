# glei für Alexa

Alexa-Skill für Live-Abfahrten in Graz:

| Du sagst | glei antwortet |
|----------|----------------|
| „Alexa, frag Bim Graz, wann der nächste Dreier in der Krenngasse fährt“ | „Der nächste 3er Richtung Andritz fährt in 3 Minuten, der übernächste in 11 Minuten.“ |
| „… wann fährt der nächste Vierer in der Reininghausstraße?“ | „Der 4er Richtung Liebenau fährt in 3 und in 18 Minuten, Richtung Reininghaus in 7 und in 22 Minuten.“ |
| „… der nächste Vierer in der Reininghausstraße **Richtung Hauptbahnhof**“ | Nur die passende Richtung – auch wenn „Hauptbahnhof“ nicht die Endstation ist |
| „… wann fährt die nächste Bim in der Krenngasse?“ | Die nächsten zwei Abfahrten aller Linien |
| „Alexa, sag Bim Graz, **meine Haltestelle ist** Krenngasse“ | Speichert die Haltestelle – danach reicht „Alexa, frag Bim Graz, wann der nächste Dreier fährt“ |

Ohne Haltestelle (und ohne gespeicherte) fragt glei nach: „Für welche Haltestelle?“

## Aufbau

| Pfad | Inhalt |
|------|--------|
| `TriasDeparture.Alexa/` | Backend (ASP.NET Core): Request-Prüfung, Intents, Antworttexte, Heimathaltestelle (Firestore) |
| `TriasDeparture.Alexa.Tests/` | Tests |
| `skill-package/interactionModels/custom/de-DE.json` | Interaktionsmodell (Aufrufname, Intents, Beispielsätze, Haltestellen-/Linienlisten) |
| `tools/TriasDeparture.Alexa.ModelGenerator/` | Erzeugt `de-DE.json` aus Live-Daten von TRIAS |
| `Dockerfile` | Container für Cloud Run (Build-Kontext = Repo-Root) |

Das Backend nutzt den TRIAS-Code aus [`core/`](../core) direkt – kein Umweg über die öffentliche API.

## Einrichtung

### 1. Amazon-Entwicklerkonto

Auf [developer.amazon.com](https://developer.amazon.com/alexa/console/ask) mit **demselben Amazon-Konto anmelden, mit dem dein Echo verbunden ist** (kostenlos). Nur dann funktioniert der Skill im Entwicklungsmodus auf deinen Geräten.

### 2. Skill anlegen

**Create Skill**:

| Feld | Wert |
|------|------|
| Name | `glei` |
| Primary locale | **German (DE)** – gilt auch für Geräte in Österreich |
| Type of experience | Other |
| Model | **Custom** |
| Hosting service | **Provision your own** |
| Template | Start from Scratch |

Danach in der Skill-Liste **Copy Skill ID** (`amzn1.ask.skill.…`) – die brauchst du in Schritt 4.

### 3. Interaktionsmodell importieren

Im Skill: **Build → Interaction Model → JSON Editor** → Inhalt von [`skill-package/interactionModels/custom/de-DE.json`](skill-package/interactionModels/custom/de-DE.json) einfügen (oder die Datei hineinziehen) → **Save** → **Build skill**. Der Build dauert ein paar Minuten.

### 4. Firestore (für die Heimathaltestelle)

Google Cloud Console, Projekt `glei` → **Firestore → Create database**:

- **Firestore in Native mode**, Database ID `(default)`
- Location: **europe-west1**
- Security rules: *Production* (nur das Backend greift zu)

Gespeichert wird pro Amazon-Konto nur ein SHA-256-Hash der anonymen Alexa-User-ID plus die Haltestelle.

### 5. Cloud Run Service `glei-alexa`

Wie bei der API: **Cloud Run → Deploy container → Service → Continuously deploy from a repository → Set up with Cloud Build** → `geisi93/glei`, Branch `^main$`, Build type **Dockerfile**, Source location `/alexa/Dockerfile`.

| Einstellung | Wert |
|-------------|------|
| Service name | `glei-alexa` |
| Region | `europe-west1` |
| Authentication | **Allow public access** (Alexa muss den Endpoint erreichen; das Backend prüft selbst, dass Anfragen von Amazon kommen) |
| Billing / Scaling | Request-based, Min 0, Max 3 |
| Container port | 8080 |
| Memory / CPU | 512 MiB / 1, **Startup CPU boost** an |

**Variables & Secrets:**

| Name | Wert |
|------|------|
| `Trias__BaseUrl` | TRIAS-Endpoint (wie bei der API) |
| `Alexa__SkillId` | Skill ID aus Schritt 2 |
| `Firestore__ProjectId` | Projekt-ID des Google-Cloud-Projekts (z. B. `glei-123456`, siehe Projektauswahl) |

Danach den **Trigger anpassen** (Cloud Build → Triggers, Region `europe-west1` → Trigger von `glei-alexa` → Edit):

- **Included files**: `alexa/**` und `core/**`
- Build-Schritt: nach `-f` muss `alexa/Dockerfile` stehen, Build-Kontext `.` (Repo-Root) – wie beim API-Trigger

> Sollte Firestore mit „PermissionDenied“ antworten: dem Service-Account des Dienstes (*Cloud Run → glei-alexa → Security*) die Rolle **Cloud Datastore User** geben.

### 6. Endpoint eintragen

Alexa Developer Console → **Build → Endpoint → HTTPS**:

- Default Region: die URL von `glei-alexa` (`https://glei-alexa-….europe-west1.run.app/`)
- SSL certificate type: **„My development endpoint is a sub-domain of a domain that has a wildcard certificate from a certificate authority“**

**Save Endpoints**.

### 7. Testen

- **Test**-Tab → oben auf **Development** stellen → tippen oder sprechen: *„frag bim graz wann fährt der nächste dreier in der krenngasse“*
- Am Echo: *„Alexa, frag Bim Graz, wann der nächste Dreier in der Krenngasse fährt.“*

## Tipps

**Kürzer fragen mit einer Routine:** Alexa-App → *Mehr → Routinen → +* → Auslöser *Sprache*: „wann kommt mein Dreier“ → Aktion *Benutzerdefiniert*: „frag bim graz wann fährt der nächste dreier in der krenngasse“. Dann reicht „Alexa, wann kommt mein Dreier?“.

**Aufrufname:** „Bim Graz“ statt „glei“ – Alexa macht aus „glei“ immer „gleich“, und die Konsole verlangt mindestens zwei Wörter. Ändern unter *Build → Invocations → Skill Invocation Name* – und `InvocationName` im Generator anpassen. Nicht erlaubt: „Skill“, „App“, „Alexa“, „Amazon“, „Echo“.

**„Öffne Bim Graz“ geht am Echo nicht** („wird auf diesem Gerät nicht unterstützt“, Alexa+): stattdessen *„Alexa, starte den Skill Bim Graz“* oder direkt *„Alexa, frag Bim Graz, …“*.

**Zweistellige Linien:** Die Spracherkennung macht aus „dreiundzwanziger“ manchmal „zwei dreier“ – das Modell und das Backend verstehen das als 23.

**Neue Haltestellen/Linien:** Modell neu erzeugen und in Schritt 3 erneut importieren:

```bash
dotnet run --project alexa/tools/TriasDeparture.Alexa.ModelGenerator
```

Der TRIAS-Endpoint kommt aus der Umgebungsvariable `TRIAS_BASE_URL` oder aus `api/TriasDeparture.Api/appsettings.Development.json`.

## Lokal starten

`alexa/TriasDeparture.Alexa/appsettings.Development.json` (nicht eingecheckt) mit `Trias:BaseUrl` anlegen, dann:

```bash
dotnet run --project alexa/TriasDeparture.Alexa
```

Das Launch-Profil schaltet die Signaturprüfung ab und speichert Heimathaltestellen nur im Speicher. Beispiel:

```bash
curl -s http://localhost:5162/ -H "Content-Type: application/json" -d '{
  "version": "1.0",
  "context": { "System": { "application": { "applicationId": "local" }, "user": { "userId": "me" } } },
  "request": { "type": "IntentRequest", "requestId": "1", "timestamp": "2026-11-07T12:00:00Z", "locale": "de-DE",
    "intent": { "name": "NextDepartureIntent", "slots": {
      "line": { "name": "line", "value": "dreier" },
      "stop": { "name": "stop", "value": "Krenngasse" } } } }
}'
```

## Konfiguration

| Schlüssel | Bedeutung | Standard |
|-----------|-----------|----------|
| `Trias:BaseUrl` | TRIAS-Endpoint | – (Pflicht) |
| `Alexa:SkillId` | Nur Anfragen dieses Skills werden beantwortet | – (Pflicht, wenn geprüft wird) |
| `Alexa:VerifyRequests` | Signatur/Zeitstempel von Amazon prüfen | `true` (lokal `false`) |
| `HomeStops:Store` | `Firestore` oder `InMemory` | `Firestore` (lokal `InMemory`) |
| `Firestore:ProjectId` | Google-Cloud-Projekt; leer = automatisch erkennen | leer |
