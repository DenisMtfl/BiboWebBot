using System.Globalization;
using BiboWebBot.HomeAssistant.Models;
using BiboWebBot.Mqtt;
using BiboWebBot.VoebbParsing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NetDaemon.HassModel;

namespace BiboWebBot.HomeAssistant.Services;

public sealed class BiboLoanSyncHostedService(
    IServiceProvider serviceProvider,
    IConfiguration configuration,
    IHaContext ha,
    ILogger<BiboLoanSyncHostedService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await RunSyncAsync(stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            var settings = configuration.GetSection("DailySync").Get<DailySyncSettings>() ?? new DailySyncSettings();
            if (!settings.Enabled)
            {
                await Task.Delay(TimeSpan.FromMinutes(10), stoppingToken);
                continue;
            }

            var nextRun = GetNextRun(settings.TimeOfDay);
            var delay = nextRun - DateTime.Now;
            if (delay > TimeSpan.Zero)
            {
                await Task.Delay(delay, stoppingToken);
            }

            await RunSyncAsync(stoppingToken);
        }
    }

    private async Task RunSyncAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var scope = serviceProvider.CreateScope();
            var automationService = scope.ServiceProvider.GetRequiredService<IVoebbAutomationService>();
            var mqttPublishService = scope.ServiceProvider.GetRequiredService<IMqttPublishService>();

            var configuredAccounts = LoadConfiguredAccounts();
            if (configuredAccounts.Count == 0)
            {
                logger.LogWarning("Bibo-Sync: Keine VÖBB-Konten konfiguriert.");
                NotifyHomeAssistant("Bibo-Sync: Keine VÖBB-Konten konfiguriert.");
                return;
            }

            var accountResults = new List<(AppSettingsVoebbAccount Account, VoebbOperationResult Result)>();
            foreach (var account in configuredAccounts)
            {
                logger.LogInformation("[{Account}] Lade Ausleihen...", account.DisplayLabel);

                var result = await automationService.LoadLoansAsync(new VoebbCredentials
                {
                    CardId = account.CardId!,
                    Password = account.Password!
                }, cancellationToken);

                accountResults.Add((account, result));

                if (result.Logs.Count > 0)
                {
                    foreach (var logEntry in result.Logs)
                    {
                        logger.LogInformation("[{Account}] {LogEntry}", account.DisplayLabel, logEntry);
                    }
                }

                if (!result.Success)
                {
                    logger.LogWarning("[{Account}] Laden fehlgeschlagen: {Message}", account.DisplayLabel, result.Message);
                }
                else
                {
                    logger.LogInformation("[{Account}] Laden erfolgreich: {Message}", account.DisplayLabel, result.Message);
                }
            }

            var successfulResults = accountResults.Where(x => x.Result.Success).ToList();
            if (successfulResults.Count == 0)
            {
                var message = "Bibo-Sync: Kein Konto konnte erfolgreich geladen werden.";
                logger.LogError(message);
                NotifyHomeAssistant(message);
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
                var message = "Bibo-Sync: Keine Ausleihen erkannt.";
                logger.LogWarning(message);
                NotifyHomeAssistant(message);
                return;
            }

            var deCulture = CultureInfo.GetCultureInfo("de-DE");
            var parsedLoans = allLoans
                .Select(item => new
                {
                    item.Loan,
                    item.AccountLabel,
                    DueDate = DateOnly.TryParseExact(item.Loan.DueDate, "dd.MM.yyyy", deCulture, DateTimeStyles.None, out var dueDate)
                        ? dueDate
                        : (DateOnly?)null
                })
                .Where(x => x.DueDate.HasValue)
                .ToList();

            if (parsedLoans.Count == 0)
            {
                var message = "Bibo-Sync: Keine gültigen Fälligkeitsdaten erkannt.";
                logger.LogWarning(message);
                NotifyHomeAssistant(message);
                return;
            }

            var today = DateOnly.FromDateTime(DateTime.Today);
            var overdueCount = parsedLoans.Count(x => x.DueDate!.Value < today);
            var dueSoonCount = parsedLoans.Count(x => x.DueDate!.Value >= today && x.DueDate.Value <= today.AddDays(7));
            var earliest = parsedLoans.OrderBy(x => x.DueDate!.Value).First();

            var mqttSent = await mqttPublishService.PublishEarliestDueDateAsync(earliest.DueDate!.Value, earliest.AccountLabel, cancellationToken);
            var sensorSent = await mqttPublishService.PublishLoanSensorAsync(
                earliest.DueDate!.Value,
                earliest.AccountLabel,
                earliest.Loan.LoanName,
                overdueCount,
                dueSoonCount,
                cancellationToken);
            var warningSent = await mqttPublishService.PublishDueSoonSensorAsync(
                dueSoonCount,
                earliest.AccountLabel,
                earliest.Loan.LoanName,
                earliest.DueDate.Value.ToString("dd.MM.yyyy", CultureInfo.GetCultureInfo("de-DE")),
                cancellationToken);

            var summary =
                $"Konten: {successfulResults.Count}/{configuredAccounts.Count}\n" +
                $"Ausleihen: {parsedLoans.Count}\n" +
                $"Überfällig: {overdueCount}\n" +
                $"Fällig in 7 Tagen: {dueSoonCount}\n" +
                $"Früheste Rückgabe: {earliest.DueDate:dd.MM.yyyy} | {earliest.Loan.LoanName} | Konto: {earliest.AccountLabel}\n" +
                $"MQTT: {(mqttSent ? "gesendet" : "nicht gesendet")}, Sensor: {(sensorSent ? "gesendet" : "nicht gesendet")}, Warnung: {(warningSent ? "gesendet" : "nicht gesendet")}";

            logger.LogInformation("Bibo-Sync abgeschlossen. {Summary}", summary.ReplaceLineEndings(" | "));
            NotifyHomeAssistant(summary);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Graceful shutdown.
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Bibo-Sync: Unerwarteter Fehler.");
            NotifyHomeAssistant($"Bibo-Sync fehlgeschlagen: {ex.Message}");
        }
    }

    private List<AppSettingsVoebbAccount> LoadConfiguredAccounts()
    {
        var accounts = configuration
            .GetSection("Voebb:Accounts")
            .Get<List<AppSettingsVoebbAccount>>() ?? [];

        var selectedAccounts = accounts
            .Where(x => x.LoadForBatch
                && !string.IsNullOrWhiteSpace(x.CardId)
                && !string.IsNullOrWhiteSpace(x.Password))
            .GroupBy(x => x.CardId)
            .Select(g => g.Last())
            .ToList();

        if (selectedAccounts.Count > 0)
        {
            return selectedAccounts;
        }

        var legacyCardId = configuration["Voebb:CardId"]?.Trim();
        var legacyPassword = configuration["Voebb:Password"];
        var legacyLabel = configuration["Voebb:AccountLabel"]?.Trim();

        if (!string.IsNullOrWhiteSpace(legacyCardId) && !string.IsNullOrWhiteSpace(legacyPassword))
        {
            return [new AppSettingsVoebbAccount
            {
                LoginName = legacyLabel,
                CardId = legacyCardId,
                Password = legacyPassword,
                LoadForBatch = true
            }];
        }

        return [];
    }

    private static DateTime GetNextRun(string timeOfDay)
    {
        var parsed = TimeSpan.TryParseExact(timeOfDay, @"hh\:mm", CultureInfo.InvariantCulture, out var configuredTime)
            ? configuredTime
            : TimeSpan.FromHours(7);

        var next = DateTime.Today.Add(parsed);
        return next <= DateTime.Now ? next.AddDays(1) : next;
    }

    private void NotifyHomeAssistant(string message)
        => ha.CallService(
            "notify",
            "persistent_notification",
            data: new
            {
                title = "BiboWebBot Home Assistant",
                message,
                notification_id = "bibowebbot_loan_sync"
            });
}
