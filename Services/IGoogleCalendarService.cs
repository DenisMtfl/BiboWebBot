using BiboWebBot.Models;

namespace BiboWebBot.Services;

public interface IGoogleCalendarService
{
    Task<bool> CreateEarliestLoanEventAsync(HttpContext context, DateOnly dueDate, string? accountLabel, string? calendarId = null, string? eventSummaryTemplate = null, CancellationToken cancellationToken = default);

    Task<bool> CreateEarliestLoanEventByServiceAccountAsync(DateOnly dueDate, string? accountLabel, string? eventSummaryTemplate = null, CancellationToken cancellationToken = default);

    Task<GoogleCalendarListResult> GetAvailableCalendarsAsync(HttpContext context, CancellationToken cancellationToken = default);
}
