namespace BiboWebBot.HomeAssistant.Models;

public sealed class AppSettingsVoebbAccount
{
    public string? LoginName { get; set; }

    public string? CardId { get; set; }

    public string? Password { get; set; }

    public bool LoadForBatch { get; set; } = true;

    public string DisplayLabel => string.IsNullOrWhiteSpace(LoginName) ? CardId ?? string.Empty : LoginName;
}
