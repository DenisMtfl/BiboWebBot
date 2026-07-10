using System.Globalization;
using BiboWebBot.Mqtt;
using BiboWebBot.VoebbParsing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Serilog;
using Serilog.Events;

Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Information()
    .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
    .Enrich.FromLogContext()
    .WriteTo.Console(outputTemplate: "{Timestamp:HH:mm:ss} [{Level:u3}] {Message:lj}{NewLine}{Exception}")
    .CreateLogger();

try
{
    var configuration = new ConfigurationBuilder()
        .SetBasePath(AppContext.BaseDirectory)
        .AddJsonFile("appsettings.json", optional: false)
        .Build();

    var services = new ServiceCollection();
    services.AddSingleton<IConfiguration>(configuration);
    services.AddLogging(logging => logging.AddSerilog(Log.Logger, dispose: false));
    services.AddTransient<IVoebbAutomationService, VoebbAutomationService>();
    services.AddTransient<IMqttPublishService, MqttPublishService>();

    using var serviceProvider = services.BuildServiceProvider();
    var voebbAutomationService = serviceProvider.GetRequiredService<IVoebbAutomationService>();
    var mqttService = serviceProvider.GetRequiredService<IMqttPublishService>();

    var configuredAccounts = LoadConfiguredAccounts(configuration);
    if (configuredAccounts.Count == 0)
    {
        Log.Error("Konfiguration fehlt: Voebb:Accounts mit CardId und Password ist erforderlich.");
        return;
    }

    var accountResults = new List<(ConsoleVoebbAccount Account, VoebbOperationResult Result)>();
    foreach (var account in configuredAccounts)
    {
        Log.Information("[{Account}] Lade Ausleihen...", account.DisplayLabel);

        var loadResult = await voebbAutomationService.LoadLoansAsync(new VoebbCredentials
        {
            CardId = account.CardId,
            Password = account.Password
        });

        accountResults.Add((account, loadResult));

        if (loadResult.Logs.Count > 0)
        {
            Log.Information("[{Account}] Detail-Log:", account.DisplayLabel);
            foreach (var log in loadResult.Logs)
            {
                Log.Information("[{Account}] {LogEntry}", account.DisplayLabel, log);
            }
        }

        if (!loadResult.Success)
        {
            Log.Warning("Laden fehlgeschlagen für {Account}: {Message}", account.DisplayLabel, loadResult.Message);
        }
        else
        {
            Log.Information("Laden erfolgreich für {Account}: {Message}", account.DisplayLabel, loadResult.Message);
        }
    }

    var successfulResults = accountResults.Where(x => x.Result.Success).ToList();
    if (successfulResults.Count == 0)
    {
        Log.Error("Kein Konto konnte erfolgreich geladen werden.");
        return;
    }

    var allLoans = successfulResults
        .SelectMany(x => x.Result.Loans.Select(loan => new
        {
            Loan = loan,
            AccountLabel = x.Account.DisplayLabel
        }))
        .ToList();

    if (allLoans.Count == 0)
    {
        Log.Warning("Keine Ausleihen erkannt.");
        return;
    }

    var deCulture = CultureInfo.GetCultureInfo("de-DE");
    var parsedLoans = allLoans
        .Select(item => new
        {
            item.Loan,
            item.AccountLabel,
            DueDate = DateOnly.TryParseExact(item.Loan.DueDate, "dd.MM.yyyy", deCulture, DateTimeStyles.None, out var due)
                ? due
                : (DateOnly?)null
        })
        .Where(x => x.DueDate.HasValue)
        .ToList();

    if (parsedLoans.Count == 0)
    {
        Log.Warning("Keine gültigen Fälligkeitsdaten erkannt.");
        return;
    }

    var today = DateOnly.FromDateTime(DateTime.Today);
    var overdueCount = parsedLoans.Count(x => x.DueDate!.Value < today);
    var dueSoonCount = parsedLoans.Count(x => x.DueDate!.Value >= today && x.DueDate.Value <= today.AddDays(7));
    var earliest = parsedLoans.OrderBy(x => x.DueDate!.Value).First();

    Log.Information("Konten geladen: {Loaded}/{Total}", successfulResults.Count, configuredAccounts.Count);
    Log.Information("Ausleihen erkannt: {Count}", parsedLoans.Count);
    Log.Information("Überfällig: {Count}", overdueCount);
    Log.Information("Fällig in 7 Tagen: {Count}", dueSoonCount);
    Log.Information("Früheste Rückgabe: {Date} | {Loan} | Konto: {Account}", earliest.DueDate.ToString(), earliest.Loan.LoanName, earliest.AccountLabel);

    var mqttSent = await mqttService.PublishEarliestDueDateAsync(earliest.DueDate!.Value, earliest.AccountLabel);
    Log.Information(mqttSent ? "MQTT gesendet." : "MQTT nicht gesendet (Konfiguration/Fehler).");
}
catch (Exception ex)
{
    Log.Fatal(ex, "Unerwarteter Fehler in BiboWebBot.Console.");
}
finally
{
    Log.CloseAndFlush();
    WaitForExitIfInteractive();
}

static void WaitForExitIfInteractive()
{
    if (Console.IsInputRedirected || Console.IsOutputRedirected)
    {
        return;
    }

    Console.WriteLine();
    Console.WriteLine("Fertig. Drücke eine beliebige Taste zum Beenden...");
    Console.ReadKey(intercept: true);
}

static List<ConsoleVoebbAccount> LoadConfiguredAccounts(IConfiguration configuration)
{
    var accounts = configuration
        .GetSection("Voebb:Accounts")
        .GetChildren()
        .Select(section => new ConsoleVoebbAccount(
            section["LoginName"]?.Trim(),
            section["CardId"]?.Trim() ?? string.Empty,
            section["Password"] ?? string.Empty,
            section.GetValue<bool?>("LoadForBatch") ?? true))
        .Where(x => !string.IsNullOrWhiteSpace(x.CardId) && !string.IsNullOrWhiteSpace(x.Password) && x.LoadForBatch)
        .ToList();

    if (accounts.Count > 0)
    {
        return accounts;
    }

    var legacyCardId = configuration["Voebb:CardId"]?.Trim();
    var legacyPassword = configuration["Voebb:Password"];
    var legacyLabel = configuration["Voebb:AccountLabel"]?.Trim();

    if (!string.IsNullOrWhiteSpace(legacyCardId) && !string.IsNullOrWhiteSpace(legacyPassword))
    {
        return [new ConsoleVoebbAccount(legacyLabel, legacyCardId, legacyPassword, true)];
    }

    return [];
}

file sealed record ConsoleVoebbAccount(string? LoginName, string CardId, string Password, bool LoadForBatch)
{
    public string DisplayLabel => string.IsNullOrWhiteSpace(LoginName) ? CardId : LoginName;
}
