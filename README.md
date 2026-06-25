# BiboWebBot

BiboWebBot ist eine Blazor-Server-Anwendung, um VÖBB-Ausleihen fuer ein oder mehrere Konten zu laden und anzuzeigen.

## Features

- Konfigurationsseite fuer mehrere Konten
- Laden mehrerer Konten mit sichtbaren Status-Logs
- Zwei Lademodi:
  - Playwright (Browser-Automation)
  - HTTP-Modus (ohne Playwright)
- Ausleihliste mit Konto-/Login-Name pro Zeile
- Browser-Storage fuer gespeicherte Konten und Auswahl

## Tech Stack

- .NET 10 (`net10.0`)
- Blazor Server
- QuickGrid (`Microsoft.AspNetCore.Components.QuickGrid`)
- Playwright for browser automation (`Microsoft.Playwright`)

## Prerequisites

- .NET SDK 10 installiert
- Bei erster Playwright-Nutzung: Browser-Binaries installieren

## Getting Started

1. Wiederherstellen und bauen:

```bash
dotnet restore
dotnet build
```

2. Optional fuer Playwright: Browser installieren:

```bash
dotnet build
pwsh bin/Debug/net10.0/playwright.ps1 install
```

3. Anwendung starten:

```bash
dotnet run
```

4. Lokale URL aus dem Terminal aufrufen (meist `https://localhost:xxxx`).

## Usage

1. In der App die Seite Konten Konfiguration oeffnen.
2. Ein oder mehrere VÖBB-Konten hinzufuegen:
   - Login-Name (Anzeige in der UI)
   - Card ID
   - Passwort
3. Konten speichern und zur Sammelauswahl markieren.
4. Auf der Startseite den Modus waehlen:
   - Mit Playwright
   - Ohne Playwright (HTTP)
5. Laden starten und Status/Logs beobachten.

## Security Notes

- Zugangsdaten werden zur Komfortnutzung im Browser-Storage gespeichert.
- Nur auf vertrauenswuerdigen Geraeten verwenden.
- Exportierte Browser-Profile nicht weitergeben.

## Project Structure

- `Components/Pages/Home.razor`: Laden der Ausleihen und Ergebnistabelle
- `Components/Pages/Accounts.razor`: Konten-Konfiguration
- `Services/VoebbAutomationService.cs`: Login-, Navigation- und Scraping-Logik
- `Models/`: Datenmodelle fuer Konten, Ausleihen und Ergebnisobjekte
- `wwwroot/credentialsStorage.js`: Browser-Storage-Helfer

## Build Status

Das Projekt baut aktuell erfolgreich mit:

```bash
dotnet build
```
