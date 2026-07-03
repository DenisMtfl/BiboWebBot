namespace BiboWebBot.Models;

public sealed class GoogleCalendarListResult
{
    public bool IsSuccess { get; set; }

    public string? ErrorMessage { get; set; }

    public List<GoogleCalendarInfo> Calendars { get; set; } = [];
}
