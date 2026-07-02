namespace BiboWebBot.Models;

public sealed class DailySyncSettings
{
    public bool Enabled { get; set; }

    public string TimeOfDay { get; set; } = "07:00";

    public bool UsePlaywright { get; set; }
}
