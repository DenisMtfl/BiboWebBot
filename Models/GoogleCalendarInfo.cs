namespace BiboWebBot.Models;

public sealed class GoogleCalendarInfo
{
    public string Id { get; set; } = string.Empty;

    public string Summary { get; set; } = string.Empty;

    public bool IsPrimary { get; set; }
}
