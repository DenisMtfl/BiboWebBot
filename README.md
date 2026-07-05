# BiboWebBot

Inoffizielle Blazor-Server-WebApp zum Laden und Verwalten von VÖBB-Ausleihen über ein oder mehrere Konten.

> **Wichtig:** Kein offizielles VÖBB-Projekt. Änderungen an VÖBB-Seiten/Flows können die Funktion beeinflussen.

## Highlights

- Mehrere Konten verwalten und für Sammelladen markieren
- Zwei Lademodi:
  - **Playwright** (Browser-Automation)
  - **HTTP-Fallback** (ohne Browser)
- Robuste Parserlogik mit Tabellen- und Text-Fallback
- Sichtbare Lade-/Diagnose-Logs pro Konto
- Optional:
  - Google-Login + Kalendereintrag für frühestes Fälligkeitsdatum
  - MQTT-Publishing des frühesten Datums
  - täglicher Hintergrund-Sync (DailySync)

## Inhaltsverzeichnis

- [Funktionsumfang](#funktionsumfang)
- [Architektur](#architektur)
- [Voraussetzungen](#voraussetzungen)
- [Schnellstart](#schnellstart)
- [Konfiguration](#konfiguration)
- [Bedienung](#bedienung)
- [Lademodi erklärt](#lademodi-erklärt)
- [Google Kalender Integration](#google-kalender-integration)
- [MQTT Integration](#mqtt-integration)
- [DailySync (Hintergrundlauf)](#dailysync-hintergrundlauf)
- [Sicherheit](#sicherheit)
- [Fehlerbehebung](#fehlerbehebung)
- [Entwicklung](#entwicklung)
- [Projektstruktur](#projektstruktur)
- [Haftungsausschluss](#haftungsausschluss)

## Funktionsumfang

- Konten in der UI speichern (Login-Name, Ausweisnummer, Passwort)
- Mehrere Konten gesammelt laden
- Ergebnisansicht mit Login-Name, Ausleihname und Fälligkeitsdatum
- Retry bei Timeout-Szenarien
- Parser-Tests über xUnit

## Architektur

- **UI:** Blazor Server (`Components/Pages`)
- **Domain-Modelle:** `Models/`
- **Login/Navigation/Scraping:** `Services/VoebbAutomationService.cs`
- **Parser:** `Services/VoebbLoanParser.cs`
- **Kalender:** `Services/GoogleCalendarService.cs`
- **MQTT:** `Services/MqttPublishService.cs`
- **Daily Job:** `Services/DailyLoanSyncHostedService.cs`
- **Client Storage:** `wwwroot/credentialsStorage.js` (Local Storage)

## Voraussetzungen

- .NET SDK 10 (`net10.0`)
- Internetzugriff auf `voebb.de`
- Für Playwright-Modus: installierte Browser-Binaries

## Schnellstart

```bash
dotnet restore
dotnet build
dotnet run
```

Danach die lokale URL aus dem Terminal öffnen (z. B. `https://localhost:xxxx`).

### Playwright-Browser installieren

Falls der Playwright-Modus meldet, dass Browser fehlen:

```bash
dotnet build
pwsh bin/Debug/net10.0/playwright.ps1 install chromium
```

Alternative ohne `pwsh` (macOS ARM, mit mitgelieferter Node-Binary):

```bash
./bin/Debug/net10.0/.playwright/node/darwin-arm64/node \
  ./bin/Debug/net10.0/.playwright/package/cli.js install chromium
```

## Konfiguration

### `appsettings.json`

Relevante Bereiche:

- `Voebb:Accounts`: serverseitig vorkonfigurierte Konten (optional)
- `Google:ClientId`, `Google:ClientSecret`: OAuth für UI-Login
- `Google:ServiceAccountJsonPath`, `Google:CalendarId`: serverseitiger DailySync-Kalendereintrag
- `Google:EventSummaryTemplate`: z. B. `VÖBB fällig: {Konto} am {Datum}`
- `Mqtt:Enabled`, `Mqtt:Host`, `Mqtt:Port`, `Mqtt:Topic`, optional `Username`, `Password`, `UseTls`, `ClientId`
- `DailySync:Enabled`, `DailySync:TimeOfDay`

## Bedienung

1. Seite **Konten Konfiguration** öffnen (`/accounts`)
2. Für jedes Konto eingeben:
   - Login-Name
   - Bibliotheksausweis
   - Passwort
3. Konto speichern und für Sammelladen markieren
4. Zur Startseite (`/`) gehen
5. Lademodus wählen und Ausleihen laden
6. Ergebnisliste und Logs prüfen

Optional:

- Google anmelden (`/auth/google/login`) und frühestes Fälligkeitsdatum in gewählten Kalender schreiben
- DailySync aktivieren für automatisches tägliches Laden + Kalender/MQTT Aktionen

## Lademodi erklärt

### Playwright

- Simuliert Browser-Interaktion (robust bei dynamischen Flows)
- Benötigt Browser-Binaries

### HTTP-Fallback

- Kein Browser erforderlich
- Schneller/leichter, aber anfälliger bei HTML-/Flow-Änderungen
- Nutzt zusätzliche Fallback-Navigation und Text-Parsing

## Google Kalender Integration

Die App unterstützt:

- Google OAuth Login
- Abruf verfügbarer Kalender (`/api/google-calendar/calendars`)
- Erstellen eines Kalendereintrags für frühestes Fälligkeitsdatum (`/api/google-calendar/sync-earliest`)

Scopes:

- `https://www.googleapis.com/auth/calendar.events`
- `https://www.googleapis.com/auth/calendar.readonly`

## MQTT Integration

Wenn aktiviert, publiziert die App das früheste Fälligkeitsdatum auf das konfigurierte Topic.  
Gedacht für Home Assistant / Automations-Workflows.

## DailySync (Hintergrundlauf)

`DailyLoanSyncHostedService` kann täglich automatisch:

1. Ausleihen laden
2. Frühestes Datum ermitteln
3. MQTT publizieren
4. Kalendereintrag erstellen

Steuerung über `DailySync` in der Konfiguration.

## Sicherheit

- Kontodaten werden in der UI im **Browser Local Storage** gespeichert.
- Sensible Daten (Google Secrets, MQTT-Credentials) **nicht** ins öffentliche Repo committen.
- App nur auf vertrauenswürdigen Geräten nutzen.
- Auf geteilten Geräten gespeicherte Konten nach Nutzung löschen.

## Fehlerbehebung

### „Keine konfigurierten Konten …“

Mindestens ein Konto speichern und für Sammelladen markieren.

### „Playwright-Browser fehlt“

Playwright Browser installieren (siehe oben).

### „Login fehlgeschlagen“

Ausweisnummer/Passwort prüfen; ggf. Login direkt auf VÖBB testen.

### „Zeitüberschreitung bei der Kommunikation“

Neu versuchen; alternativ den HTTP-Modus testen.

## Entwicklung

### Build

```bash
dotnet build BiboWebBot.sln
```

### Tests

```bash
dotnet test BiboWebBot.sln
```

## Projektstruktur

```text
BiboWebBot/
├─ Components/
│  └─ Pages/
│     ├─ Home.razor
│     ├─ Accounts.razor
│     └─ About.razor
├─ Models/
├─ Services/
│  ├─ VoebbAutomationService.cs
│  ├─ VoebbLoanParser.cs
│  ├─ GoogleCalendarService.cs
│  ├─ MqttPublishService.cs
│  └─ DailyLoanSyncHostedService.cs
├─ wwwroot/
│  └─ credentialsStorage.js
├─ BiboWebBot.Tests/
├─ BiboWebBot.csproj
└─ BiboWebBot.sln
```

## Haftungsausschluss

Dieses Projekt wird ohne Gewähr bereitgestellt.  
Kompatibilität mit VÖBB kann sich durch externe Änderungen jederzeit ändern.
