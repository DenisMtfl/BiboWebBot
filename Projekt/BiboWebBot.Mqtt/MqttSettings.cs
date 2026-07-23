namespace BiboWebBot.Mqtt;

public sealed class MqttSettings
{
    public bool Enabled { get; set; }

    public string Host { get; set; } = string.Empty;

    public int Port { get; set; } = 1883;

    public string Topic { get; set; } = "bibo/earliest-due-date";

    public string? Username { get; set; }

    public string? Password { get; set; }

    public bool UseTls { get; set; }

    public string? ClientId { get; set; }

    public bool DiscoveryEnabled { get; set; } = true;

    public string DiscoveryPrefix { get; set; } = "homeassistant";

    public string SensorName { get; set; } = "BiboWebBot Nächste Rückgabe";

    public string SensorUniqueId { get; set; } = "bibowebbot_next_due";

    public string SensorStateTopic { get; set; } = "bibo/homeassistant/next-due/state";

    public string SensorAttributesTopic { get; set; } = "bibo/homeassistant/next-due/attributes";

    public string DeviceName { get; set; } = "BiboWebBot";

    public string DeviceManufacturer { get; set; } = "BiboWebBot";

    public string DeviceModel { get; set; } = "NetDaemon";

    public string SensorIcon { get; set; } = "mdi:book-clock";

    public string WarningSensorName { get; set; } = "BiboWebBot Bald fällig";

    public string WarningSensorUniqueId { get; set; } = "bibowebbot_due_soon";

    public string WarningSensorStateTopic { get; set; } = "bibo/homeassistant/due-soon/state";

    public string WarningSensorAttributesTopic { get; set; } = "bibo/homeassistant/due-soon/attributes";

    public string WarningSensorIcon { get; set; } = "mdi:alert-circle-outline";
}
