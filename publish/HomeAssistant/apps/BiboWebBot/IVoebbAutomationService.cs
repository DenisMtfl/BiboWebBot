namespace BiboWebBot.VoebbParsing;

public interface IVoebbAutomationService
{
    Task<VoebbOperationResult> LoadLoansAsync(VoebbCredentials credentials, CancellationToken cancellationToken = default);

    Task<VoebbOperationResult> RenewLoanAsync(VoebbCredentials credentials, int renewIndex, CancellationToken cancellationToken = default);

    Task<VoebbOperationResult> RenewAllAsync(VoebbCredentials credentials, CancellationToken cancellationToken = default);
}
