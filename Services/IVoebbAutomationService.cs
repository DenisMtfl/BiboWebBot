using BiboWebBot.Models;

namespace BiboWebBot.Services;

public interface IVoebbAutomationService
{
    Task<VoebbOperationResult> LoadLoansAsync(VoebbCredentials credentials, CancellationToken cancellationToken = default);

    Task<VoebbOperationResult> LoadLoansWithoutPlaywrightAsync(VoebbCredentials credentials, CancellationToken cancellationToken = default);

    Task<VoebbOperationResult> RenewLoanAsync(VoebbCredentials credentials, int renewIndex, CancellationToken cancellationToken = default);

    Task<VoebbOperationResult> RenewAllAsync(VoebbCredentials credentials, CancellationToken cancellationToken = default);
}
