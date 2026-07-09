using Microsoft.AspNetCore.Http;

namespace BiboWebBot.GoogleCalendar;

public interface IGoogleCalendarService
{
    Task<bool> CreateEarliestLoanEventAsync(HttpContext context, DateOnly dueDate, string? accountLabel, string? calendarId = null, string? eventSummaryTemplate = null, CancellationToken cancellationToken = default);

    Task<bool> CreateEarliestLoanEventByConsoleLoginAsync(DateOnly dueDate, string? accountLabel, string? eventSummaryTemplate = null, CancellationToken cancellationToken = default);

    Task<GoogleCalendarListResult> GetAvailableCalendarsAsync(HttpContext context, CancellationToken cancellationToken = default);
}
