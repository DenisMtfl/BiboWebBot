# BiboWebBot

BiboWebBot ist eine .NET-10-Lösung zum Abrufen und Auswerten von VÖBB-Ausleihen. Zusätzlich zur bestehenden Blazor-Webanwendung und Console-Anwendung gibt es jetzt ein NetDaemon-Projekt für Home Assistant.

## Überblick

Mit BiboWebBot lassen sich mehrere VÖBB-Konten zentral verwalten und in einem Durchlauf laden. Das früheste Rückgabedatum kann anschließend optional an MQTT und direkt an Home Assistant gemeldet werden.

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
- Neues NetDaemon-Projekt für Home Assistant mit direkter Ausführung im HA-Umfeld

## Lösungsstruktur

- `Projekt/BiboWebBot` – Blazor-Server-Webanwendung
- `Projekt/BiboWebBot.Console` – Console-Anwendung für lokale und automatisierte Läufe
- `Projekt/BiboWebBot.HomeAssistant` – NetDaemon-Projekt für Home Assistant
- `Projekt/BiboWebBot.VoebbParsing` – HTTP-basierte VÖBB-Login- und Parsing-Logik
- `Projekt/BiboWebBot.GoogleCalendar` – Google-Kalender-Integration
- `Projekt/BiboWebBot.Mqtt` – MQTT-Publishing
- `Projekt/BiboWebBot.Tests` – Tests

## Voraussetzungen

- .NET SDK 10
- optional: Google-OAuth-Client für Kalenderfunktionen
- optional: MQTT-Broker für MQTT-Ausgaben
- optional: Home-Assistant-Host und Long-Lived Access Token für das NetDaemon-Projekt

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

### Console-Anwendung starten

```bash
dotnet run --project Projekt/BiboWebBot.Console/BiboWebBot.Console.csproj
```

### Home-Assistant-Projekt starten

```bash
dotnet run --project Projekt/BiboWebBot.HomeAssistant/BiboWebBot.HomeAssistant.csproj
```

## Konfiguration

Die relevanten Einstellungen liegen in den lokalen `appsettings.json`-Dateien der jeweiligen Projekte.

### Home Assistant / NetDaemon

`Projekt/BiboWebBot.HomeAssistant/appsettings.json` enthält die NetDaemon- und Home-Assistant-Konfiguration sowie die Bibo-spezifischen Einstellungen:

- `HomeAssistant:Host` / `HomeAssistant:Token` – HA-Verbindung
- `Voebb:Accounts` – Konten für den Abruf
- `Mqtt:*` – MQTT-Konfiguration
- `DailySync:Enabled` – aktiviert den regelmäßigen Lauf
- `DailySync:TimeOfDay` – Uhrzeit im Format `HH:mm`

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

- `LoginName` – Anzeigename in Logs und Ausgaben
- `CardId` – Bibliotheksausweisnummer
- `Password` – VÖBB-Passwort
- `LoadForBatch` – aktiviert das Konto für den Lauf

## Betriebsmodi

### Interaktiv über Blazor

Geeignet für die manuelle Nutzung im Browser mit sichtbaren Ladeprotokollen, Kontenverwaltung und Google-Kalender-Auswahl.

### Automatisiert über Console

Geeignet für lokale Tasks, Scheduler oder andere Automatisierungen, wenn Ausleihen ohne Browseroberfläche geladen und weiterverarbeitet werden sollen.

### Direkt in Home Assistant über NetDaemon

Geeignet für den Betrieb als Home-Assistant-nahe Automatisierung mit MQTT-Ausgabe und optionaler Daily-Sync-Funktion.

Das NetDaemon-Projekt veröffentlicht zusätzlich MQTT-Discovery-Sensoren für das früheste Rückgabedatum und für bald fällige Ausleihen. In Home Assistant erscheinen sie mit Datum bzw. Anzahl als Zustand und Konto-/Ausleihdetails als Attribute.

### MQTT-Sensoren

Wenn MQTT aktiviert ist, legt das NetDaemon-Projekt automatisch die Entitäten `sensor.bibowebbot_next_due` und `sensor.bibowebbot_due_soon` an.

- `sensor.bibowebbot_next_due` zeigt das früheste Rückgabedatum im ISO-Format `yyyy-MM-dd`
- `sensor.bibowebbot_due_soon` zeigt die Anzahl der in den nächsten 7 Tagen fälligen Ausleihen
- Weitere Details wie Konto und Ausleihname stehen in den Attributen

## Sicherheit

- Zugangsdaten nur in lokalen, nicht versionierten `appsettings.json`-Dateien pflegen.
- Keine echten Zugangsdaten, MQTT-Passwörter oder Google-Secrets in ein öffentliches Repository committen.

## Relevante Dateien

- `Projekt/BiboWebBot.HomeAssistant/program.cs` – NetDaemon-Host und DI-Konfiguration
- `Projekt/BiboWebBot.HomeAssistant/appsettings.json` – HA-, VÖBB- und MQTT-Konfiguration
- `Projekt/BiboWebBot.HomeAssistant/Services/BiboLoanSyncHostedService.cs` – Start- und Daily-Sync-Logik
- `Projekt/BiboWebBot.VoebbParsing/VoebbAutomationService.cs` – HTTP-Login und Parsing
- `Projekt/BiboWebBot.Mqtt/MqttPublishService.cs` – MQTT-Versand
