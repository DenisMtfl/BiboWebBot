using System.Globalization;
using BiboWebBot.Models;

namespace BiboWebBot.Services;

public sealed class DailyLoanSyncHostedService(
    IServiceProvider serviceProvider,
    IConfiguration configuration,
    ILogger<DailyLoanSyncHostedService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
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

            await RunSyncAsync(settings, stoppingToken);

            await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
        }
    }

    private async Task RunSyncAsync(DailySyncSettings settings, CancellationToken cancellationToken)
    {
        try
        {
            using var scope = serviceProvider.CreateScope();
            var automationService = scope.ServiceProvider.GetRequiredService<IVoebbAutomationService>();
            var mqttPublishService = scope.ServiceProvider.GetRequiredService<IMqttPublishService>();
            var googleCalendarService = scope.ServiceProvider.GetRequiredService<IGoogleCalendarService>();

            var configuredAccounts = configuration
                .GetSection("Voebb:Accounts")
                .Get<List<AppSettingsVoebbAccount>>() ?? [];

            var selectedAccounts = configuredAccounts
                .Where(x => x.LoadForBatch
                    && !string.IsNullOrWhiteSpace(x.LoginName)
                    && !string.IsNullOrWhiteSpace(x.CardId)
                    && !string.IsNullOrWhiteSpace(x.Password))
                .GroupBy(x => x.CardId)
                .Select(g => g.Last())
                .ToList();

            if (selectedAccounts.Count == 0)
            {
                logger.LogInformation("DailySync: Keine Konten für täglichen Lauf konfiguriert.");
                return;
            }

            var allLoans = new List<VoebbLoanItem>();
            foreach (var account in selectedAccounts)
            {
                var credentials = new VoebbCredentials
                {
                    CardId = account.CardId!,
                    Password = account.Password!
                };

                var result = await automationService.LoadLoansAsync(credentials, cancellationToken);

                if (!result.Success)
                {
                    logger.LogWarning("DailySync: Laden für Konto {Account} fehlgeschlagen: {Message}", account.LoginName, result.Message);
                    continue;
                }

                allLoans.AddRange(result.Loans.Select(loan => new VoebbLoanItem
                {
                    RenewIndex = loan.RenewIndex,
                    AccountCardId = string.IsNullOrWhiteSpace(account.LoginName) ? loan.AccountCardId : account.LoginName!,
                    LoanName = loan.LoanName,
                    Title = loan.Title,
                    DueDate = loan.DueDate,
                    Details = loan.Details
                }));
            }

            var earliest = GetEarliestLoan(allLoans);
            if (earliest is null)
            {
                logger.LogInformation("DailySync: Keine Ausleihen mit gültigem Fälligkeitsdatum gefunden.");
                return;
            }

            var eventSummaryTemplate = configuration["Google:EventSummaryTemplate"];

            var mqttOk = await mqttPublishService.PublishEarliestDueDateAsync(earliest.Value.DueDate, earliest.Value.AccountLabel, cancellationToken);
            var googleOk = await googleCalendarService.CreateEarliestLoanEventByServiceAccountAsync(earliest.Value.DueDate, earliest.Value.AccountLabel, eventSummaryTemplate, cancellationToken);

            logger.LogInformation(
                "DailySync: Frühestes Datum {DueDate} verarbeitet. MQTT={MqttOk}, Google={GoogleOk}",
                earliest.Value.DueDate.ToString("dd.MM.yyyy", CultureInfo.GetCultureInfo("de-DE")),
                mqttOk,
                googleOk);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // graceful shutdown
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "DailySync: Unerwarteter Fehler.");
        }
    }

    private static DateTime GetNextRun(string timeOfDay)
    {
        var today = DateTime.Today;
        var parsed = TimeSpan.TryParseExact(timeOfDay, "hh\\:mm", CultureInfo.InvariantCulture, out var configuredTime)
            ? configuredTime
            : TimeSpan.FromHours(7);

        var next = today.Add(parsed);
        return next <= DateTime.Now ? next.AddDays(1) : next;
    }

    private static (DateOnly DueDate, string? AccountLabel)? GetEarliestLoan(IReadOnlyList<VoebbLoanItem> loans)
    {
        var deCulture = CultureInfo.GetCultureInfo("de-DE");
        var earliest = loans
            .Select(loan => new
            {
                loan.AccountCardId,
                Parsed = DateOnly.TryParseExact(loan.DueDate, "dd.MM.yyyy", deCulture, DateTimeStyles.None, out var dueDate)
                    ? dueDate
                    : (DateOnly?)null
            })
            .Where(x => x.Parsed.HasValue)
            .OrderBy(x => x.Parsed!.Value)
            .FirstOrDefault();

        if (earliest?.Parsed is null)
        {
            return null;
        }

        return (earliest.Parsed.Value, earliest.AccountCardId);
    }
}
