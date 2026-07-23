using System.Globalization;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using MQTTnet;
using MQTTnet.Client;
using MQTTnet.Protocol;

namespace BiboWebBot.Mqtt;

public sealed class MqttPublishService(IConfiguration configuration, ILogger<MqttPublishService> logger) : IMqttPublishService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<bool> PublishEarliestDueDateAsync(DateOnly dueDate, string? accountLabel, CancellationToken cancellationToken = default)
    {
        var settings = configuration.GetSection("Mqtt").Get<MqttSettings>() ?? new MqttSettings();
        if (!settings.Enabled)
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(settings.Host) || string.IsNullOrWhiteSpace(settings.Topic))
        {
            logger.LogWarning("MQTT ist aktiviert, aber Host oder Topic fehlen in der Konfiguration.");
            return false;
        }

        try
        {
            using var client = await ConnectAsync(settings, cancellationToken);
            if (client is null)
            {
                return false;
            }

            var dateText = dueDate.ToString("dd.MM.yyyy", CultureInfo.GetCultureInfo("de-DE"));
            var message = new MqttApplicationMessageBuilder()
                .WithTopic(settings.Topic)
                .WithPayload(dateText)
                .WithQualityOfServiceLevel(MqttQualityOfServiceLevel.AtLeastOnce)
                .Build();

            await client.PublishAsync(message, cancellationToken);
            await client.DisconnectAsync(new MqttClientDisconnectOptions(), cancellationToken);
            return true;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "MQTT-Nachricht konnte nicht gesendet werden.");
            return false;
        }
    }

    public async Task<bool> PublishLoanSensorAsync(
        DateOnly dueDate,
        string? accountLabel,
        string? loanName,
        int overdueCount,
        int dueSoonCount,
        CancellationToken cancellationToken = default)
    {
        var settings = configuration.GetSection("Mqtt").Get<MqttSettings>() ?? new MqttSettings();
        if (!settings.Enabled)
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(settings.Host))
        {
            logger.LogWarning("MQTT ist aktiviert, aber der Host fehlt in der Konfiguration.");
            return false;
        }

        try
        {
            using var client = await ConnectAsync(settings, cancellationToken);
            if (client is null)
            {
                return false;
            }

            if (settings.DiscoveryEnabled)
            {
                await PublishDiscoveryAsync(client, settings, cancellationToken);
            }

            var stateTopic = string.IsNullOrWhiteSpace(settings.SensorStateTopic)
                ? "bibo/homeassistant/next-due/state"
                : settings.SensorStateTopic;

            var attributesTopic = string.IsNullOrWhiteSpace(settings.SensorAttributesTopic)
                ? "bibo/homeassistant/next-due/attributes"
                : settings.SensorAttributesTopic;

            var stateText = dueDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            var stateMessage = new MqttApplicationMessageBuilder()
                .WithTopic(stateTopic)
                .WithPayload(stateText)
                .WithRetainFlag()
                .WithQualityOfServiceLevel(MqttQualityOfServiceLevel.AtLeastOnce)
                .Build();

            var attributesPayload = JsonSerializer.Serialize(new
            {
                account_label = accountLabel,
                loan_name = loanName,
                overdue_count = overdueCount,
                due_soon_count = dueSoonCount,
                due_date_display = dueDate.ToString("dd.MM.yyyy", CultureInfo.GetCultureInfo("de-DE"))
            }, JsonOptions);

            var attributesMessage = new MqttApplicationMessageBuilder()
                .WithTopic(attributesTopic)
                .WithPayload(attributesPayload)
                .WithRetainFlag()
                .WithQualityOfServiceLevel(MqttQualityOfServiceLevel.AtLeastOnce)
                .Build();

            await client.PublishAsync(stateMessage, cancellationToken);
            await client.PublishAsync(attributesMessage, cancellationToken);
            await client.DisconnectAsync(new MqttClientDisconnectOptions(), cancellationToken);
            return true;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "MQTT-Sensor konnte nicht gesendet werden.");
            return false;
        }
    }

    public async Task<bool> PublishDueSoonSensorAsync(
        int dueSoonCount,
        string? accountLabel,
        string? nextDueLoanName,
        string? nextDueDate,
        CancellationToken cancellationToken = default)
    {
        var settings = configuration.GetSection("Mqtt").Get<MqttSettings>() ?? new MqttSettings();
        if (!settings.Enabled)
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(settings.Host))
        {
            logger.LogWarning("MQTT ist aktiviert, aber der Host fehlt in der Konfiguration.");
            return false;
        }

        try
        {
            using var client = await ConnectAsync(settings, cancellationToken);
            if (client is null)
            {
                return false;
            }

            if (settings.DiscoveryEnabled)
            {
                await PublishDueSoonDiscoveryAsync(client, settings, cancellationToken);
            }

            var stateTopic = string.IsNullOrWhiteSpace(settings.WarningSensorStateTopic)
                ? "bibo/homeassistant/due-soon/state"
                : settings.WarningSensorStateTopic;

            var attributesTopic = string.IsNullOrWhiteSpace(settings.WarningSensorAttributesTopic)
                ? "bibo/homeassistant/due-soon/attributes"
                : settings.WarningSensorAttributesTopic;

            var stateMessage = new MqttApplicationMessageBuilder()
                .WithTopic(stateTopic)
                .WithPayload(dueSoonCount.ToString(CultureInfo.InvariantCulture))
                .WithRetainFlag()
                .WithQualityOfServiceLevel(MqttQualityOfServiceLevel.AtLeastOnce)
                .Build();

            var attributesPayload = JsonSerializer.Serialize(new
            {
                account_label = accountLabel,
                next_due_loan_name = nextDueLoanName,
                next_due_date = nextDueDate,
                warning = dueSoonCount > 0,
                warning_text = dueSoonCount > 0 ? $"{dueSoonCount} Ausleihen bald fällig" : "Keine Ausleihen bald fällig"
            }, JsonOptions);

            var attributesMessage = new MqttApplicationMessageBuilder()
                .WithTopic(attributesTopic)
                .WithPayload(attributesPayload)
                .WithRetainFlag()
                .WithQualityOfServiceLevel(MqttQualityOfServiceLevel.AtLeastOnce)
                .Build();

            await client.PublishAsync(stateMessage, cancellationToken);
            await client.PublishAsync(attributesMessage, cancellationToken);
            await client.DisconnectAsync(new MqttClientDisconnectOptions(), cancellationToken);
            return true;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "MQTT-Warnsensor konnte nicht gesendet werden.");
            return false;
        }
    }

    private async Task<IMqttClient?> ConnectAsync(MqttSettings settings, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(settings.Topic) && !settings.DiscoveryEnabled)
        {
            logger.LogWarning("MQTT ist aktiviert, aber Topic fehlt in der Konfiguration.");
            return null;
        }

        var clientFactory = new MqttFactory();
        var client = clientFactory.CreateMqttClient();

        var clientId = string.IsNullOrWhiteSpace(settings.ClientId)
            ? $"BiboWebBot-{Guid.NewGuid():N}"
            : settings.ClientId;

        var optionsBuilder = new MqttClientOptionsBuilder()
            .WithClientId(clientId)
            .WithTcpServer(settings.Host, settings.Port);

        if (!string.IsNullOrWhiteSpace(settings.Username))
        {
            optionsBuilder = optionsBuilder.WithCredentials(settings.Username, settings.Password);
        }

        if (settings.UseTls)
        {
            optionsBuilder = optionsBuilder.WithTlsOptions(tls => tls.UseTls());
        }

        await client.ConnectAsync(optionsBuilder.Build(), cancellationToken);
        return client;
    }

    private static async Task PublishDueSoonDiscoveryAsync(IMqttClient client, MqttSettings settings, CancellationToken cancellationToken)
    {
        var discoveryPrefix = string.IsNullOrWhiteSpace(settings.DiscoveryPrefix) ? "homeassistant" : settings.DiscoveryPrefix;
        var entityId = string.IsNullOrWhiteSpace(settings.WarningSensorUniqueId) ? "bibowebbot_due_soon" : settings.WarningSensorUniqueId;
        var discoveryTopic = $"{discoveryPrefix}/sensor/{entityId}/config";
        var stateTopic = string.IsNullOrWhiteSpace(settings.WarningSensorStateTopic) ? "bibo/homeassistant/due-soon/state" : settings.WarningSensorStateTopic;
        var attributesTopic = string.IsNullOrWhiteSpace(settings.WarningSensorAttributesTopic) ? "bibo/homeassistant/due-soon/attributes" : settings.WarningSensorAttributesTopic;

        var payload = JsonSerializer.Serialize(new
        {
            name = string.IsNullOrWhiteSpace(settings.WarningSensorName) ? "BiboWebBot Bald fällig" : settings.WarningSensorName,
            unique_id = entityId,
            object_id = entityId,
            state_topic = stateTopic,
            json_attributes_topic = attributesTopic,
            icon = string.IsNullOrWhiteSpace(settings.WarningSensorIcon) ? "mdi:alert-circle-outline" : settings.WarningSensorIcon,
            state_class = "measurement",
            unit_of_measurement = "Ausleihen",
            device = new
            {
                identifiers = new[] { string.IsNullOrWhiteSpace(settings.SensorUniqueId) ? "bibowebbot_next_due" : settings.SensorUniqueId, "BiboWebBot" },
                name = string.IsNullOrWhiteSpace(settings.DeviceName) ? "BiboWebBot" : settings.DeviceName,
                manufacturer = string.IsNullOrWhiteSpace(settings.DeviceManufacturer) ? "BiboWebBot" : settings.DeviceManufacturer,
                model = string.IsNullOrWhiteSpace(settings.DeviceModel) ? "NetDaemon" : settings.DeviceModel
            }
        }, JsonOptions);

        var message = new MqttApplicationMessageBuilder()
            .WithTopic(discoveryTopic)
            .WithPayload(payload)
            .WithRetainFlag()
            .WithQualityOfServiceLevel(MqttQualityOfServiceLevel.AtLeastOnce)
            .Build();

        await client.PublishAsync(message, cancellationToken);
    }

    private static async Task PublishDiscoveryAsync(IMqttClient client, MqttSettings settings, CancellationToken cancellationToken)
    {
        var discoveryPrefix = string.IsNullOrWhiteSpace(settings.DiscoveryPrefix) ? "homeassistant" : settings.DiscoveryPrefix;
        var discoveryTopic = $"{discoveryPrefix}/sensor/{settings.SensorUniqueId}/config";

        var payload = JsonSerializer.Serialize(new
        {
            name = string.IsNullOrWhiteSpace(settings.SensorName) ? "BiboWebBot Nächste Rückgabe" : settings.SensorName,
            unique_id = string.IsNullOrWhiteSpace(settings.SensorUniqueId) ? "bibowebbot_next_due" : settings.SensorUniqueId,
            object_id = string.IsNullOrWhiteSpace(settings.SensorUniqueId) ? "bibowebbot_next_due" : settings.SensorUniqueId,
            state_topic = string.IsNullOrWhiteSpace(settings.SensorStateTopic) ? "bibo/homeassistant/next-due/state" : settings.SensorStateTopic,
            json_attributes_topic = string.IsNullOrWhiteSpace(settings.SensorAttributesTopic) ? "bibo/homeassistant/next-due/attributes" : settings.SensorAttributesTopic,
            device_class = "date",
            icon = string.IsNullOrWhiteSpace(settings.SensorIcon) ? "mdi:book-clock" : settings.SensorIcon,
            availability_topic = $"{(string.IsNullOrWhiteSpace(settings.SensorStateTopic) ? "bibo/homeassistant/next-due/state" : settings.SensorStateTopic)}/availability",
            payload_available = "online",
            payload_not_available = "offline",
            device = new
            {
                identifiers = new[] { string.IsNullOrWhiteSpace(settings.SensorUniqueId) ? "bibowebbot_next_due" : settings.SensorUniqueId, "BiboWebBot" },
                name = string.IsNullOrWhiteSpace(settings.DeviceName) ? "BiboWebBot" : settings.DeviceName,
                manufacturer = string.IsNullOrWhiteSpace(settings.DeviceManufacturer) ? "BiboWebBot" : settings.DeviceManufacturer,
                model = string.IsNullOrWhiteSpace(settings.DeviceModel) ? "NetDaemon" : settings.DeviceModel
            }
        }, JsonOptions);

        var message = new MqttApplicationMessageBuilder()
            .WithTopic(discoveryTopic)
            .WithPayload(payload)
            .WithRetainFlag()
            .WithQualityOfServiceLevel(MqttQualityOfServiceLevel.AtLeastOnce)
            .Build();

        await client.PublishAsync(message, cancellationToken);
    }
}
