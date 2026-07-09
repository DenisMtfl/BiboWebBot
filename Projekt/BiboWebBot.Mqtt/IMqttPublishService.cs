namespace BiboWebBot.Mqtt;

public interface IMqttPublishService
{
    Task<bool> PublishEarliestDueDateAsync(DateOnly dueDate, string? accountLabel, CancellationToken cancellationToken = default);
}
