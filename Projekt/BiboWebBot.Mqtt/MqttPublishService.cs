using System.Globalization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using MQTTnet;
using MQTTnet.Client;
using MQTTnet.Protocol;

namespace BiboWebBot.Mqtt;

public sealed class MqttPublishService(IConfiguration configuration, ILogger<MqttPublishService> logger) : IMqttPublishService
{
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
            var clientFactory = new MqttFactory();
            using var client = clientFactory.CreateMqttClient();

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

            var dateText = dueDate.ToString("dd.MM.yyyy", CultureInfo.GetCultureInfo("de-DE"));
            var payload = string.IsNullOrWhiteSpace(accountLabel)
                ? $"{dateText}"
                : $"{dateText}";

            var message = new MqttApplicationMessageBuilder()
                .WithTopic(settings.Topic)
                .WithPayload(payload)
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
}
