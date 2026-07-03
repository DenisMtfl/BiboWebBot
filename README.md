# BiboWebBot

BiboWebBot ist eine Blazor-Server-Anwendung, um VÖBB-Ausleihen für ein oder mehrere Konten zu laden und anzuzeigen.

## Features

- Konfigurationsseite für mehrere Konten
- Laden mehrerer Konten mit sichtbaren Status-Logs
- Ein HTTP-basierter Lademodus ohne Browser-Automation
- Ausleihliste mit Konto-/Login-Name pro Zeile
- Browser-Storage für gespeicherte Konten und Auswahl
- Optionaler Google-Login und automatischer Kalendereintrag für das früheste Fälligkeitsdatum
  - Kalender per Dropdown aus allen verfügbaren Google-Kalendern auswählbar
  - Termin-Name frei konfigurierbar (Platzhalter `{Konto}` und `{Datum}`)
- MQTT-Versand des frühesten Abgabedatums als Text
  - Kann z. B. in Home Assistant als Sensor-/Statuswert weiterverwendet werden
- Täglicher Hintergrundlauf (DailySync) für automatisches Laden + MQTT + Google-Kalender

## Tech Stack

- .NET 10 (`net10.0`)
- Blazor Server
- QuickGrid (`Microsoft.AspNetCore.Components.QuickGrid`)
- HTTP-basierte Scraping-/Login-Logik
- Google Calendar API (`Google.Apis.Calendar.v3`)
- Google Auth (`Microsoft.AspNetCore.Authentication.Google`)
- MQTT (`MQTTnet`)

## Prerequisites

- .NET SDK 10 installiert
- Keine Browser-Binaries erforderlich

## Getting Started

1. Wiederherstellen und bauen:

```bash
dotnet restore
dotnet build
```

2. Wiederherstellen und bauen:

```bash
dotnet restore
dotnet build
```

3. Anwendung starten:

```bash
dotnet run
```

4. Lokale URL aus dem Terminal aufrufen (meist `https://localhost:xxxx`).

## Konfiguration (appsettings)

- `Voebb:Accounts`: Konten für Sammelladen (`LoginName`, `CardId`, `Password`, `LoadForBatch`)
- `Google:ClientId` / `Google:ClientSecret`: für Google-Login in der UI
- `Google:ServiceAccountJsonPath` / `Google:CalendarId`: für serverseitigen DailySync-Kalendereintrag
- `Google:EventSummaryTemplate`: Termin-Name-Vorlage für DailySync (Platzhalter `{Konto}`, `{Datum}`)
- `Mqtt:Enabled`, `Mqtt:Host`, `Mqtt:Port`, `Mqtt:Topic`, optional `Username`, `Password`, `UseTls`, `ClientId`
- `DailySync:Enabled`, `DailySync:TimeOfDay` (z. B. `07:00`)

## Usage

1. In der App die Seite Konten Konfiguration öffnen.
2. Ein oder mehrere VÖBB-Konten hinzufügen:
   - Login-Name (Anzeige in der UI)
   - Card ID
   - Passwort
3. Konten speichern und zur Sammelauswahl markieren.
4. Auf der Startseite den Modus wählen:
   - Laden starten und Status/Logs beobachten
5. Laden starten und Status/Logs beobachten.
6. Optional: per Navigation `Google Login` anmelden, damit das früheste Datum direkt in den Google-Kalender geschrieben werden kann. Auf der Seite Konten Konfiguration kann anschließend per Dropdown der Ziel-Kalender ausgewählt und der Termin-Name angepasst werden.
7. Optional: DailySync in `appsettings.json` aktivieren für tägliche automatische Prüfung.

## Security Notes

- Zugangsdaten werden zur Komfortnutzung im Browser-Storage gespeichert.
- Alternativ können Konten zentral in `appsettings*.json` unter `Voebb:Accounts` gepflegt werden (`LoginName`, `CardId`, `Password`, optional `LoadForBatch`).
- MQTT/Google-Zugangsdaten nie in ein öffentliches Repository committen.
- Nur auf vertrauenswürdigen Geräten verwenden.
- Exportierte Browser-Profile nicht weitergeben.

## Project Structure

- `Components/Pages/Home.razor`: Laden der Ausleihen und Ergebnistabelle
- `Components/Pages/Accounts.razor`: Konten-Konfiguration
- `Services/VoebbAutomationService.cs`: Login-, Navigation- und Scraping-Logik ohne Browser-Automation
- `Services/DailyLoanSyncHostedService.cs`: täglicher Hintergrundlauf für automatische Synchronisation
- `Services/GoogleCalendarService.cs`: Google-Kalender-Synchronisation
- `Services/MqttPublishService.cs`: MQTT-Publishing
- `Models/`: Datenmodelle fuer Konten, Ausleihen und Ergebnisobjekte
- `wwwroot/credentialsStorage.js`: Browser-Storage-Helfer

## Build Status

Das Projekt baut aktuell erfolgreich mit:

```bash
dotnet build
```
