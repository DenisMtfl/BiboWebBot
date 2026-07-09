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
}
