namespace BiboWebBot.VoebbParsing;

public sealed class VoebbLoanItem
{
    public int RenewIndex { get; init; }

    public string AccountCardId { get; init; } = string.Empty;

    public string LoanName { get; init; } = string.Empty;

    public string Title { get; init; } = string.Empty;

    public string DueDate { get; init; } = string.Empty;

    public string Details { get; init; } = string.Empty;
}
