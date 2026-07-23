# Project template for source deploy
This is the project template for source deploy. You will use this template if you want to deploy your NetDaemon apps as is and let NetDaemon runtime compile them at run-time. This allows you to easier edit the files in source editor in Home Assistant like Visual Studio Code server.  

This is generated using NetDaemon runtime version 5 and .NET 9.

## Getting started
Please see [netdaemon.xyz](https://netdaemon.xyz) for more information about getting starting developing apps for Home Assistant using NetDaemon.

Please add code generation features in `program.cs` when using code generation features by removing comments!

## Use the code generator
See https://netdaemon.xyz/docs/hass_model/hass_model_codegen

## Issues

- If you have issues or suggestions of improvements to this template, please [add an issue](https://github.com/net-daemon/netdaemon-app-template)
- If you have issues or suggestions of improvements to NetDaemon, please [add an issue](https://github.com/net-daemon/netdaemon/issues)

## Discuss the NetDaemon

Please [join the Discord server](https://discord.gg/K3xwfcX) to get support or if you want to contribute and help others.

# BiboWebBot für Home Assistant mit NetDaemon

Diese Anwendung stellt den BiboWebBot als source-deployte NetDaemon-App für Home Assistant bereit. NetDaemon kompiliert die C#-Dateien beim Start direkt auf Home Assistant. Die VÖBB-HTTP- und Parserlogik wird zusammen mit der App unter `apps/BiboWebBot` deployed.

## Voraussetzungen

- Home Assistant mit installiertem und konfiguriertem NetDaemon-Add-on
- NetDaemon V5 beziehungsweise Runtime 26.x
- MQTT-Integration in Home Assistant, wenn die Sensoren verwendet werden sollen
- Ein gültiger VÖBB-Bibliotheksausweis mit Passwort
- SSH-Zugriff auf den Home-Assistant-Host

## Projektstruktur

```text
BiboWebBot.HomeAssistant/
├── apps/
│   ├── GlobalUsings.cs
│   └── BiboWebBot/
│       ├── BiboHomeAssistantApp.cs
│       ├── IVoebbAutomationService.cs
│       ├── VoebbAutomationService.cs
│       ├── VoebbCredentials.cs
│       ├── VoebbLoanItem.cs
│       ├── VoebbLoanParser.cs
│       └── VoebbOperationResult.cs
├── appsettings.json
└── BiboWebBot.HomeAssistant.csproj
```

`BiboHomeAssistantApp.cs` ist der Einstiegspunkt. Die übrigen VÖBB-Dateien stammen aus dem gemeinsamen `BiboWebBot.VoebbParsing`-Projekt und werden als Source-Dateien mit veröffentlicht, damit der NetDaemon-Compiler keine Projekt- oder DLL-Referenzen benötigt.

## Konfiguration

Die aktive Konfiguration liegt auf Home Assistant unter:

```text
/config/netdaemon6/appsettings.json
```

Alle Einstellungen werden aus dieser Datei gelesen. Es werden keine Umgebungsvariablen benötigt.

### Home Assistant

```json
"HomeAssistant": {
  "Host": "192.168.1.50",
  "Port": 8123,
  "Ssl": false,
  "Token": "DEIN_LONG_LIVED_ACCESS_TOKEN"
}
```

### VÖBB-Konten

```json
"Voebb": {
  "Accounts": [
    {
      "LoginName": "Mein Konto",
      "CardId": "BIBLIOTHEKSAUSWEIS",
      "Password": "VÖBB_PASSWORT",
      "LoadForBatch": true
    }
  ]
}
```

Mehrere Konten können in `Accounts` eingetragen werden. Konten mit `LoadForBatch: false` werden übersprungen.

### Synchronisation

Die Synchronisation läuft:

1. direkt beim Start der NetDaemon-App
2. anschließend standardmäßig stündlich

```json
"Sync": {
  "Enabled": true,
  "IntervalHours": 1
}
```

Beispiele:

```json
"IntervalHours": 0.5
```

führt alle 30 Minuten aus. Mit `Enabled: false` wird die Synchronisation deaktiviert.

### MQTT und Home-Assistant-Entitäten

MQTT Discovery wird beim Start veröffentlicht. Die Konfiguration sieht beispielsweise so aus:

```json
"Mqtt": {
  "Enabled": true,
  "DiscoveryEnabled": true,
  "DiscoveryPrefix": "homeassistant",
  "SensorStateTopic": "bibo/homeassistant/next-due/state",
  "SensorAttributesTopic": "bibo/homeassistant/next-due/attributes",
  "WarningSensorStateTopic": "bibo/homeassistant/due-soon/state",
  "WarningSensorAttributesTopic": "bibo/homeassistant/due-soon/attributes"
}
```

Die App verwendet den Home-Assistant-Service `mqtt.publish`. Der MQTT-Broker muss deshalb bereits in Home Assistant eingerichtet und verbunden sein.

Erwartete Entitäten:

| Entität | Bedeutung |
|---|---|
| `sensor.bibowebbot_next_due` | Frühestes Rückgabedatum als Datum |
| `sensor.bibowebbot_due_soon` | Anzahl der Ausleihen, die innerhalb von sieben Tagen fällig werden |

Die Entitäten werden über MQTT Discovery mit dem Gerät `BiboWebBot` verbunden und sind anschließend unter **Einstellungen → Geräte & Dienste → MQTT** zu finden.

## Build und Deployment

### Lokal veröffentlichen

In PowerShell aus dem Repository-Root:

```powershell
$project = "A:\BiboWebBot\Projekt\BiboWebBot.HomeAssistant\BiboWebBot.HomeAssistant.csproj"
$publish = "A:\BiboWebBot\publish\HomeAssistant"

dotnet publish $project -c Release -o $publish
```

### Source-Dateien nach Home Assistant kopieren

Die C#-Dateien dürfen nur in den App-Unterordner kopiert werden. Eine zusätzliche Datei unter `/config/netdaemon6/BiboHomeAssistantApp.cs` führt zu doppelten Klassendefinitionen.

```powershell
scp "$publish\appsettings.json" root@192.168.1.50:/config/netdaemon6/appsettings.json
scp "$publish\apps\BiboWebBot\*.cs" root@192.168.1.50:/config/netdaemon6/apps/BiboWebBot/
```

Der korrekte Zielpfad ist:

```text
/config/netdaemon6/apps/BiboWebBot/
```

Vor einem vollständigen Deployment kann der Zielordner bereinigt werden:

```powershell
ssh root@192.168.1.50 "rm -f /config/netdaemon6/BiboHomeAssistantApp.cs; rm -rf /config/netdaemon6/apps/BiboWebBot; mkdir -p /config/netdaemon6/apps/BiboWebBot"
```

### NetDaemon neu starten

Nach Änderungen an C#-Dateien oder `appsettings.json` den NetDaemon-Add-on vollständig neu starten:

```text
Home Assistant
→ Einstellungen
→ Add-ons
→ NetDaemon
→ Stoppen
→ Starten
```

## Logs und Diagnose

Ein erfolgreicher Start sieht ungefähr so aus:

```text
Successfully connected to Home Assistant
BiboWebBot Home Assistant App gestartet.
MQTT Discovery für BiboWebBot veröffentlicht.
Successfully loaded app BiboWebBot.HomeAssistant.Apps.BiboHomeAssistantApp
Running 1, Error 0
```

VÖBB-Fehler werden pro Konto protokolliert, beispielsweise:

```text
[Mein Konto] Loginformular konnte mit den HTTP-Fallbacks nicht gefunden werden.
```

Wenn die Console-Anwendung funktioniert, die NetDaemon-App aber nicht, müssen alle VÖBB-Source-Dateien gemeinsam deployed werden. Insbesondere `VoebbAutomationService.cs` benötigt seine Modelle und den Parser.

### Keine Entitäten sichtbar

1. Prüfen, ob `Mqtt.Enabled` und `Mqtt.DiscoveryEnabled` auf `true` stehen.
2. Prüfen, ob die MQTT-Integration in Home Assistant verbunden ist.
3. NetDaemon vollständig neu starten.
4. Nach `MQTT Discovery für BiboWebBot veröffentlicht.` suchen.
5. Unter **Einstellungen → Geräte & Dienste → Entitäten** nach `BiboWebBot` suchen.
6. Prüfen, ob der MQTT-Broker die Discovery-Topics unter `homeassistant/sensor/.../config` erhält.

### Doppelte App-Definition

Wenn `CS0101`, `CS0111` oder `CS8863` erscheint, existiert die App wahrscheinlich zweimal. Es darf nur diese Datei vorhanden sein:

```text
/config/netdaemon6/apps/BiboWebBot/BiboHomeAssistantApp.cs
```

### Source-Compilerfehler

Der NetDaemon-Source-Compiler verfügt nicht über alle Assembly-Referenzen eines normalen .NET-Projekts. Deshalb dürfen Source-Deploy-Dateien keine nicht mitgelieferten Projekt-DLLs voraussetzen. Bei Fehlern wie `CookieContainer`, `DecompressionMethods` oder fehlenden Namespaces müssen die betroffenen Abhängigkeiten entweder als kompatible Source-Dateien mit deployed oder durch Source-Deploy-kompatible APIs ersetzt werden.

## Sicherheit

- Keine echten Tokens oder Passwörter in Git committen.
- Nach einer versehentlichen Veröffentlichung von Zugangsdaten den Home-Assistant-Token und die VÖBB-/MQTT-Passwörter rotieren.
- In Support-Logs Zugangsdaten, Tokens und vollständige Konfigurationsdateien schwärzen.
- `appsettings.json` auf Home Assistant mit restriktiven Dateirechten schützen.

## Weiterführende Informationen

- [NetDaemon](https://netdaemon.xyz/)
- [NetDaemon HassModel Code Generator](https://netdaemon.xyz/docs/hass_model/hass_model_codegen)
- [MQTT Discovery in Home Assistant](https://www.home-assistant.io/integrations/mqtt/#mqtt-discovery)
