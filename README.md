# BiboWebBot

BiboWebBot ist eine .NET-10-Lösung zum Abrufen und Auswerten von VÖBB-Ausleihen. Die Lösung kombiniert eine Blazor-Server-Webanwendung für die interaktive Nutzung mit einer Console-Anwendung für lokale oder automatisierte Batch-Läufe.

## Überblick

Mit BiboWebBot lassen sich mehrere VÖBB-Konten zentral verwalten und in einem Durchlauf laden. Das früheste Rückgabedatum kann anschließend optional an Google Kalender und MQTT übertragen werden.

## Hauptfunktionen

- Laden von Ausleihen für mehrere VÖBB-Konten
- HTTP-basierter Abruf ohne Browser-Automation
- Anzeige von Status- und Fehlerlogs in der Weboberfläche
- Verwaltung von Konten in der Blazor-App mit Speicherung im Browser
- Unterstützung zusätzlicher Konten aus `appsettings*.json`
- Optionaler Google-Login in der Web-App
  - Auswahl des Zielkalenders
  - frei konfigurierbarer Terminname mit `{Konto}` und `{Datum}`
  - Übertragung des frühesten Rückgabedatums nach manuellem Laden
- Optionaler MQTT-Versand des frühesten Rückgabedatums
- Optionaler täglicher Hintergrundlauf (`DailySync`) in der Web-App für Laden und MQTT
- Optionale Console-Ausführung für Batch-Läufe mit MQTT und Google-Kalender-Eintrag

## Lösungsstruktur

- `Projekt/BiboWebBot` – Blazor-Server-Webanwendung
- `Projekt/BiboWebBot.Console` – Console-Anwendung für lokale und automatisierte Läufe
- `Projekt/BiboWebBot.VoebbParsing` – HTTP-basierte VÖBB-Login- und Parsing-Logik
- `Projekt/BiboWebBot.GoogleCalendar` – Google-Kalender-Integration
- `Projekt/BiboWebBot.Mqtt` – MQTT-Publishing
- `Projekt/BiboWebBot.Tests` – Tests

## Voraussetzungen

- .NET SDK 10
- optional: Google-OAuth-Client für Kalenderfunktionen
- optional: MQTT-Broker für MQTT-Ausgaben

## Schnellstart

### Lösung wiederherstellen und bauen

```bash
dotnet restore
dotnet build
```

### Webanwendung starten

```bash
dotnet run --project Projekt/BiboWebBot/BiboWebBot.csproj
```

Anschließend die im Terminal ausgegebene URL im Browser öffnen.

### Console-Anwendung starten

```bash
dotnet run --project Projekt/BiboWebBot.Console/BiboWebBot.Console.csproj
```

## Konfiguration

Die relevanten Einstellungen liegen in den lokalen `appsettings.json`-Dateien der jeweiligen Projekte. Als Vorlage sind versionierte `appsettings.example.json`-Dateien enthalten; diese sollten lokal nach `appsettings.json` kopiert und dort mit echten Werten befuellt werden.

### VÖBB-Konten

```json
"Voebb": {
  "Accounts": [
    {
      "LoginName": "Name",
      "CardId": "Ausweisnummer",
      "Password": "Passwort",
      "LoadForBatch": true
    }
  ]
}
```

Bedeutung der Felder:

- `LoginName` – Anzeigename in UI, Logs und Ausgaben
- `CardId` – Bibliotheksausweisnummer
- `Password` – VÖBB-Passwort
- `LoadForBatch` – aktiviert das Konto für Sammelläufe

### Web-App (`Projekt/BiboWebBot/appsettings.json`)

Ausgangspunkt: `Projekt/BiboWebBot/appsettings.example.json`

- `Google:ClientId` / `Google:ClientSecret` – Google-Login für die Web-App
- `Google:CalendarId` – Standard-Zielkalender
- `Google:EventSummaryTemplate` – Titelvorlage für Kalendereinträge
- `Mqtt:*` – MQTT-Konfiguration
- `DailySync:Enabled` – aktiviert den täglichen Hintergrundlauf
- `DailySync:TimeOfDay` – Uhrzeit im Format `HH:mm`

Hinweis: Der aktuelle `DailySync` der Web-App veröffentlicht das früheste Rückgabedatum per MQTT. Ein automatischer Google-Kalender-Eintrag wird dort derzeit nicht ausgeführt.

### Console-App (`Projekt/BiboWebBot.Console/appsettings.json`)

Ausgangspunkt: `Projekt/BiboWebBot.Console/appsettings.example.json`

- `Google:Enabled` – aktiviert die Google-Kalender-Synchronisation
- `Google:ClientId` / `Google:ClientSecret` – OAuth-Client für die Console-App
- `Google:TokenStorePath` – lokaler Speicherort für OAuth-Tokens
- `Google:RedirectUri` – Redirect-URI für den lokalen OAuth-Flow
- `Google:CalendarId` – Zielkalender
- `Google:EventSummaryTemplate` – Titelvorlage für Kalendereinträge
- `Mqtt:*` – MQTT-Konfiguration

## Typische Nutzung der Web-App

1. `/accounts` öffnen.
2. VÖBB-Konten anlegen oder Konten aus `appsettings*.json` verwenden.
3. Gewünschte Konten für Sammelläufe markieren.
4. Optional per Google anmelden und den Zielkalender auswählen.
5. Auf der Startseite die Ausleihen für konfigurierte Konten laden.
6. Das früheste Rückgabedatum optional an Google Kalender und/oder MQTT übertragen.

## Betriebsmodi

### Interaktiv über Blazor

Geeignet für die manuelle Nutzung im Browser mit sichtbaren Ladeprotokollen, Kontenverwaltung und Google-Kalender-Auswahl.

### Automatisiert über Console

Geeignet für lokale Tasks, Scheduler oder andere Automatisierungen, wenn Ausleihen ohne Browseroberfläche geladen und weiterverarbeitet werden sollen.

## Sicherheit

- Zugangsdaten im Browser-Storage nur auf vertrauenswürdigen Geräten verwenden.
- Keine echten Zugangsdaten, MQTT-Passwörter oder Google-Secrets in ein öffentliches Repository committen.
- Echte Werte nur in lokalen, nicht versionierten `appsettings.json`-Dateien pflegen; im Repository bleiben ausschließlich die `appsettings.example.json`-Vorlagen.

## Relevante Dateien

- `Projekt/BiboWebBot/Components/Pages/Home.razor` – Laden der Ausleihen und Statusanzeige
- `Projekt/BiboWebBot/Components/Pages/Accounts.razor` – Konten- und Kalenderkonfiguration
- `Projekt/BiboWebBot/Services/DailyLoanSyncHostedService.cs` – täglicher Hintergrundlauf
- `Projekt/BiboWebBot.VoebbParsing/VoebbAutomationService.cs` – HTTP-Login und Parsing
- `Projekt/BiboWebBot.GoogleCalendar/GoogleCalendarService.cs` – Google-Kalender-Funktionen
- `Projekt/BiboWebBot.Mqtt/MqttPublishService.cs` – MQTT-Versand
