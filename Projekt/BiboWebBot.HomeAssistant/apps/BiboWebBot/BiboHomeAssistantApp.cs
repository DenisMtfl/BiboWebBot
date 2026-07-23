using System.Globalization;
using System.IO;
using System.Net;
using System.Text.RegularExpressions;
using BiboWebBot.VoebbParsing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using NetDaemon.HassModel;

namespace BiboWebBot.HomeAssistant.Apps;

[NetDaemonApp]
public sealed class BiboHomeAssistantApp : IDisposable
{
    private const string StartUrl = "https://www.voebb.de/aDISWeb/app/prod00";
    private readonly IConfiguration configuration;
    private readonly IHaContext ha;
    private readonly ILogger<BiboHomeAssistantApp> logger;
    private readonly CancellationTokenSource cancellation = new();

    public BiboHomeAssistantApp(IHaContext ha, IConfiguration configuration, ILogger<BiboHomeAssistantApp> logger)
    {
        this.ha = ha;
        this.configuration = LoadConfiguration(configuration);
        this.logger = logger;
        _ = RunAsync(cancellation.Token);
    }

    private static IConfiguration LoadConfiguration(IConfiguration provided)
    {
        if (provided["Mqtt:Enabled"] is not null)
        {
            return provided;
        }

        const string homeAssistantConfiguration = "/config/netdaemon6/appsettings.json";
        if (File.Exists(homeAssistantConfiguration))
        {
            return new ConfigurationBuilder()
                .AddJsonFile(homeAssistantConfiguration, optional: false, reloadOnChange: false)
                .Build();
        }

        return provided;
    }

    public void Dispose()
    {
        cancellation.Cancel();
        cancellation.Dispose();
    }

    private async Task RunAsync(CancellationToken token)
    {
        try
        {
            logger.LogInformation("BiboWebBot Home Assistant App gestartet.");
            await PublishDiscoveryAsync(token);
            Notify("BiboWebBot Home Assistant App gestartet.");
            await SyncAsync(token);

            while (!token.IsCancellationRequested)
            {
                var syncSettings = configuration.GetSection("Sync");
                var enabled = syncSettings.GetValue<bool?>("Enabled") ?? true;
                var intervalHours = syncSettings.GetValue<double?>("IntervalHours") ?? 1;
                var interval = TimeSpan.FromHours(Math.Max(0.1, intervalHours));

                if (!enabled)
                {
                    await Task.Delay(TimeSpan.FromMinutes(10), token);
                    continue;
                }

                await Task.Delay(interval, token);
                await SyncAsync(token);
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "BiboWebBot-Hintergrundtask wurde beendet.");
        }
    }

    private async Task SyncAsync(CancellationToken token)
    {
        try
        {
            var accounts = configuration.GetSection("Voebb:Accounts").GetChildren()
                .Select(section => new Account(
                    section["LoginName"],
                    section["CardId"],
                    section["Password"],
                    section.GetValue<bool?>("LoadForBatch") ?? true))
                .Where(account => account.Enabled && !string.IsNullOrWhiteSpace(account.CardId) && !string.IsNullOrWhiteSpace(account.Password))
                .ToList();

            if (accounts.Count == 0)
            {
                Notify("Bibo-Sync: Keine VÖBB-Konten konfiguriert.");
                return;
            }

            var loans = new List<Loan>();
            foreach (var account in accounts)
            {
                var result = await LoadLoansAsync(account, token);
                logger.LogInformation("[{Account}] {Message}", account.Label, result.Message);
                loans.AddRange(result.Loans.Select(loan => loan with { Account = account.Label }));
            }

            var today = DateOnly.FromDateTime(DateTime.Today);
            var validLoans = loans.Where(loan => loan.DueDate.HasValue).ToList();
            if (validLoans.Count == 0)
            {
                Notify("Bibo-Sync: Keine Ausleihen mit gültigem Rückgabedatum gefunden.");
                return;
            }

            var earliest = validLoans.OrderBy(loan => loan.DueDate).First();
            var overdueCount = validLoans.Count(loan => loan.DueDate < today);
            var dueSoonCount = validLoans.Count(loan => loan.DueDate >= today && loan.DueDate <= today.AddDays(7));
            var settings = configuration.GetSection("Mqtt");

            var sensorTopic = settings["SensorStateTopic"] ?? "bibo/homeassistant/next-due/state";
            var attributesTopic = settings["SensorAttributesTopic"] ?? "bibo/homeassistant/next-due/attributes";
            var warningTopic = settings["WarningSensorStateTopic"] ?? "bibo/homeassistant/due-soon/state";
            var warningAttributesTopic = settings["WarningSensorAttributesTopic"] ?? "bibo/homeassistant/due-soon/attributes";

            await PublishAsync(sensorTopic, earliest.DueDate!.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), token);
            await PublishAsync(attributesTopic, $"{{\"account_label\":\"{Escape(earliest.Account)}\",\"loan_name\":\"{Escape(earliest.Name)}\",\"overdue_count\":{overdueCount},\"due_soon_count\":{dueSoonCount}}}", token);
            await PublishAsync(warningTopic, dueSoonCount.ToString(CultureInfo.InvariantCulture), token);
            await PublishAsync(warningAttributesTopic, $"{{\"warning\":{(dueSoonCount > 0 ? "true" : "false")},\"warning_text\":\"{dueSoonCount} Ausleihen bald fällig\"}}", token);

            Notify($"Konten: {accounts.Count}\nAusleihen: {validLoans.Count}\nÜberfällig: {overdueCount}\nBald fällig: {dueSoonCount}\nFrüheste Rückgabe: {earliest.DueDate:dd.MM.yyyy} | {earliest.Name} | {earliest.Account}");
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Bibo-Sync fehlgeschlagen.");
            Notify($"Bibo-Sync fehlgeschlagen: {exception.Message}");
        }
    }

    private async Task<Result> LoadLoansAsync(Account account, CancellationToken token)
    {
        var service = new VoebbAutomationService();
        var response = await service.LoadLoansAsync(new VoebbCredentials
        {
            CardId = account.CardId!,
            Password = account.Password!
        }, token);

        var loans = response.Loans
            .Select(loan => new Loan(
                string.IsNullOrWhiteSpace(loan.LoanName) ? loan.Title : loan.LoanName,
                ParseDueDate(loan.DueDate),
                account.Label))
            .ToList();

        return new Result(loans, response.Message);
    }

    private static DateOnly? ParseDueDate(string value)
    {
        var culture = CultureInfo.GetCultureInfo("de-DE");
        return DateOnly.TryParseExact(value, ["d.M.yyyy", "dd.MM.yyyy"], culture, DateTimeStyles.None, out var date)
            ? date
            : null;
    }

    private static Dictionary<string, string> ExtractHiddenInputs(string html)
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (Match match in Regex.Matches(html, "<input[^>]*type=['\"]hidden['\"][^>]*>", RegexOptions.IgnoreCase))
        {
            var name = Regex.Match(match.Value, "name=['\"](?<name>[^'\"]+)['\"]", RegexOptions.IgnoreCase).Groups["name"].Value;
            var value = Regex.Match(match.Value, "value=['\"](?<value>[^'\"]*)['\"]", RegexOptions.IgnoreCase).Groups["value"].Value;
            if (!string.IsNullOrWhiteSpace(name))
            {
                values[name] = WebUtility.HtmlDecode(value);
            }
        }

        return values;
    }

    private void Notify(string message)
        => ha.CallService("persistent_notification", "create", data: new
        {
            title = "BiboWebBot Home Assistant",
            message,
            notification_id = "bibowebbot_loan_sync"
        });

    private Task PublishAsync(string topic, string payload, CancellationToken token)
    {
        ha.CallService("mqtt", "publish", data: new { topic, payload, retain = true });
        return Task.CompletedTask;
    }

    private Task PublishDiscoveryAsync(CancellationToken token)
    {
        var settings = configuration.GetSection("Mqtt");
        var mqttEnabled = settings.GetValue<bool?>("Enabled");
        var discoveryEnabled = settings.GetValue<bool?>("DiscoveryEnabled");
        logger.LogInformation("MQTT-Konfiguration: Enabled={MqttEnabled}, DiscoveryEnabled={DiscoveryEnabled}; Discovery wird veröffentlicht", mqttEnabled, discoveryEnabled);

        var prefix = settings["DiscoveryPrefix"] ?? "homeassistant";
        var deviceName = Escape(settings["DeviceName"] ?? "BiboWebBot");
        var manufacturer = Escape(settings["DeviceManufacturer"] ?? "BiboWebBot");
        var model = Escape(settings["DeviceModel"] ?? "NetDaemon");
        var nextDueTopic = settings["SensorStateTopic"] ?? "bibo/homeassistant/next-due/state";
        var nextDueAttributesTopic = settings["SensorAttributesTopic"] ?? "bibo/homeassistant/next-due/attributes";
        var dueSoonTopic = settings["WarningSensorStateTopic"] ?? "bibo/homeassistant/due-soon/state";
        var dueSoonAttributesTopic = settings["WarningSensorAttributesTopic"] ?? "bibo/homeassistant/due-soon/attributes";

        var nextDuePayload = $"{{\"name\":\"{Escape(settings["SensorName"] ?? "BiboWebBot Nächste Rückgabe")}\",\"unique_id\":\"{Escape(settings["SensorUniqueId"] ?? "bibowebbot_next_due")}\",\"state_topic\":\"{Escape(nextDueTopic)}\",\"json_attributes_topic\":\"{Escape(nextDueAttributesTopic)}\",\"device_class\":\"date\",\"icon\":\"{Escape(settings["SensorIcon"] ?? "mdi:book-clock")}\",\"device\":{{\"identifiers\":[\"bibowebbot\"],\"name\":\"{deviceName}\",\"manufacturer\":\"{manufacturer}\",\"model\":\"{model}\"}}}}";
        var dueSoonPayload = $"{{\"name\":\"{Escape(settings["WarningSensorName"] ?? "BiboWebBot Bald fällig")}\",\"unique_id\":\"{Escape(settings["WarningSensorUniqueId"] ?? "bibowebbot_due_soon")}\",\"state_topic\":\"{Escape(dueSoonTopic)}\",\"json_attributes_topic\":\"{Escape(dueSoonAttributesTopic)}\",\"icon\":\"{Escape(settings["WarningSensorIcon"] ?? "mdi:alert-circle-outline")}\",\"unit_of_measurement\":\"Ausleihen\",\"device\":{{\"identifiers\":[\"bibowebbot\"],\"name\":\"{deviceName}\",\"manufacturer\":\"{manufacturer}\",\"model\":\"{model}\"}}}}";

        var nextDueDiscoveryTopic = $"{prefix}/sensor/bibowebbot_next_due/config";
        var dueSoonDiscoveryTopic = $"{prefix}/sensor/bibowebbot_due_soon/config";
        logger.LogInformation("Veröffentliche MQTT Discovery Topics: {NextDueTopic}, {DueSoonTopic}", nextDueDiscoveryTopic, dueSoonDiscoveryTopic);
        ha.CallService("mqtt", "publish", data: new { topic = nextDueDiscoveryTopic, payload = nextDuePayload, retain = true });
        ha.CallService("mqtt", "publish", data: new { topic = dueSoonDiscoveryTopic, payload = dueSoonPayload, retain = true });
        logger.LogInformation("MQTT Discovery für BiboWebBot veröffentlicht.");
        return Task.CompletedTask;
    }

    private static string Escape(string? value)
        => (value ?? string.Empty).Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\r", " ").Replace("\n", " ");

    private sealed record Account(string? LoginName, string? CardId, string? Password, bool Enabled)
    {
        public string Label => string.IsNullOrWhiteSpace(LoginName) ? CardId ?? "Unbekannt" : LoginName;
    }

    private sealed record Loan(string Name, DateOnly? DueDate, string Account);

    private sealed record Result(IReadOnlyList<Loan> Loans, string Message);
}
