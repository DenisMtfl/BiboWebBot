# BiboWebBot

BiboWebBot is a Blazor Server app to load and display VÖBB loan data for one or more accounts.

## Features

- Multi-account configuration page
- Parallel/sequential loading with visible status logs
- Two loading modes:
  - Playwright (browser automation)
  - HTTP mode (without Playwright)
- Loan list UI with account/login name in each row
- Local browser storage for saved account credentials and selected accounts

## Tech Stack

- .NET 10 (`net10.0`)
- Blazor Server
- QuickGrid (`Microsoft.AspNetCore.Components.QuickGrid`)
- Playwright for browser automation (`Microsoft.Playwright`)

## Prerequisites

- .NET SDK 10 installed
- On first Playwright use: Playwright browser binaries installed

## Getting Started

1. Restore and build:

```bash
dotnet restore
dotnet build
```

2. (Optional, for Playwright mode) Install Playwright browsers:

```bash
dotnet build
pwsh bin/Debug/net10.0/playwright.ps1 install
```

3. Run the app:

```bash
dotnet run
```

4. Open the local URL shown in the terminal (usually `https://localhost:xxxx`).

## Usage

1. Open the account configuration page in the app (`Konten Konfiguration`).
2. Add one or more VÖBB accounts:
   - Login name (display name in UI)
   - Card ID
   - Password
3. Save and select the accounts to load.
4. Go to Home and choose mode:
   - `Mit Playwright`
   - `Ohne Playwright`
5. Start loading and watch status/log output.

## Security Notes

- Credentials are stored in browser local storage for convenience.
- Use only on trusted devices.
- Do not share exported browser profiles.

## Project Structure

- `Components/Pages/Home.razor`: Loan loading and result table
- `Components/Pages/Accounts.razor`: Account configuration UI
- `Services/VoebbAutomationService.cs`: Login, navigation, scraping logic
- `Models/`: DTOs for accounts, loans, and operation results
- `wwwroot/credentialsStorage.js`: Browser local storage helper

## Build Status

The project currently builds successfully with:

```bash
dotnet build
```
