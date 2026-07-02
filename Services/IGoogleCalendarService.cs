namespace BiboWebBot.Services;

public interface IGoogleCalendarService
{
    Task<bool> CreateEarliestLoanEventAsync(HttpContext context, DateOnly dueDate, string? accountLabel, CancellationToken cancellationToken = default);

    Task<bool> CreateEarliestLoanEventByServiceAccountAsync(DateOnly dueDate, string? accountLabel, CancellationToken cancellationToken = default);
}
