namespace BiboWebBot.VoebbParsing;

public sealed class VoebbOperationResult
{
    public bool Success { get; init; }

    public string Message { get; init; } = string.Empty;

    public IReadOnlyList<VoebbLoanItem> Loans { get; init; } = [];

    public IReadOnlyList<string> Logs { get; init; } = [];
}
