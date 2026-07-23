namespace BiboWebBot.Mqtt;

public interface IMqttPublishService
{
    Task<bool> PublishEarliestDueDateAsync(DateOnly dueDate, string? accountLabel, CancellationToken cancellationToken = default);

    Task<bool> PublishLoanSensorAsync(
        DateOnly dueDate,
        string? accountLabel,
        string? loanName,
        int overdueCount,
        int dueSoonCount,
        CancellationToken cancellationToken = default);

    Task<bool> PublishDueSoonSensorAsync(
        int dueSoonCount,
        string? accountLabel,
        string? nextDueLoanName,
        string? nextDueDate,
        CancellationToken cancellationToken = default);
}
