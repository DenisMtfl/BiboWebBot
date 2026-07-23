using System.Net;
using System.Text.RegularExpressions;

namespace BiboWebBot.VoebbParsing;

public static class VoebbLoanParser
{
    private static readonly Regex HtmlLineBreakRegex = new("<br\\s*/?>", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex HtmlTagRegex = new("<.*?>", RegexOptions.Singleline | RegexOptions.Compiled);
    private static readonly Regex InlineWhitespaceRegex = new("[ \\t\\r\\f\\v]+", RegexOptions.Compiled);
    private static readonly Regex BracketedLineRegex = new("^\\[.*\\]$", RegexOptions.Compiled);
    private static readonly Regex NumericIdentifierRegex = new("^\\d{6,}$", RegexOptions.Compiled);
    private static readonly Regex LoanMetadataRegex = new("sprecher|autor|regie|bibliothek|fällig|verlängerung|abholcode|konto|hinweis", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex DueDateRegex = new("(\\d{1,2}\\.\\d{1,2}\\.\\d{2,4})", RegexOptions.Compiled);
    private static readonly Regex TableRowRegex = new("<tr[^>]*>(?<row>.*?)</tr>", RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.Compiled);
    private static readonly Regex TableCellRegex = new("<t[dh][^>]*>(?<cell>.*?)</t[dh]>", RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.Compiled);
    private static readonly Regex TitleLineSeparatorRegex = new("\\s*\\|\\s*|\\r?\\n", RegexOptions.Compiled);
    private static readonly Regex AnyWhitespaceRegex = new("\\s+", RegexOptions.Compiled);
    private static readonly Regex ScriptAndStyleRegex = new("<script.*?</script>|<style.*?</style>", RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.Compiled);
    private static readonly Regex StructuralTagRegex = new("</?(tr|td|th|div|li|p|br|h1|h2|h3)[^>]*>", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex FallbackNoiseRegex = new("sprecher|autor|regie|hinweis|heute verlängert|verlängerung|abholcode", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex LoanContextRegex = new("bibliothek|fällig|verläng|ausleih|entliehen|loan|checkout", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static IReadOnlyList<VoebbLoanItem> ParseLoansFromHtml(string html)
    {
        static string CleanText(string raw)
        {
            var withLineBreaks = HtmlLineBreakRegex.Replace(raw, "\n");
            var withoutTags = HtmlTagRegex.Replace(withLineBreaks, " ");
            var decoded = WebUtility.HtmlDecode(withoutTags);
            return InlineWhitespaceRegex.Replace(decoded, " ").Trim();
        }

        static string PickLoanName(IEnumerable<string> lines)
        {
            foreach (var raw in lines)
            {
                var line = raw.TrimStart('-', ' ', ':', ';', ',');
                if (string.IsNullOrWhiteSpace(line))
                {
                    continue;
                }

                if (BracketedLineRegex.IsMatch(line))
                {
                    continue;
                }

                if (NumericIdentifierRegex.IsMatch(line))
                {
                    continue;
                }

                if (LoanMetadataRegex.IsMatch(line))
                {
                    continue;
                }

                return line;
            }

            return string.Empty;
        }

        static bool LooksLikeLibraryColumn(string value)
            => !string.IsNullOrWhiteSpace(value)
                && value.Contains("bibliothek", StringComparison.OrdinalIgnoreCase)
                && value.Contains(':', StringComparison.Ordinal);

        var result = new List<VoebbLoanItem>();
        var rows = TableRowRegex.Matches(html);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (Match rowMatch in rows)
        {
            var rowHtml = rowMatch.Groups["row"].Value;
            var cellMatches = TableCellRegex.Matches(rowHtml);
            if (cellMatches.Count < 3)
            {
                continue;
            }

            var cells = cellMatches.Select(m => CleanText(m.Groups["cell"].Value)).ToList();
            var details = string.Join(" | ", cells.Where(c => !string.IsNullOrWhiteSpace(c)));
            if (string.IsNullOrWhiteSpace(details) || !seen.Add(details))
            {
                continue;
            }

            var dueCellIndex = cells.FindIndex(DueDateRegex.IsMatch);
            if (dueCellIndex < 0)
            {
                continue;
            }

            var dueDate = DueDateRegex.Match(cells[dueCellIndex]).Groups[1].Value;
            if (string.IsNullOrWhiteSpace(dueDate))
            {
                continue;
            }

            var titleCellIndex = Math.Min(dueCellIndex + 2, cells.Count - 1);
            var titleText = cells[titleCellIndex];
            if (string.IsNullOrWhiteSpace(titleText) || LooksLikeLibraryColumn(titleText))
            {
                var titleFallback = cells
                    .Skip(dueCellIndex + 1)
                    .FirstOrDefault(cell => !string.IsNullOrWhiteSpace(cell)
                        && !LooksLikeLibraryColumn(cell)
                        && !cell.Contains("hinweis", StringComparison.OrdinalIgnoreCase)
                        && !DueDateRegex.IsMatch(cell));

                if (string.IsNullOrWhiteSpace(titleFallback))
                {
                    continue;
                }

                titleText = titleFallback;
            }

            var lines = TitleLineSeparatorRegex.Split(titleText).Where(x => !string.IsNullOrWhiteSpace(x));
            var loanName = PickLoanName(lines);

            result.Add(new VoebbLoanItem
            {
                RenewIndex = -1,
                LoanName = string.IsNullOrWhiteSpace(loanName) ? titleText : loanName,
                Title = titleText,
                DueDate = dueDate,
                Details = details
            });
        }

        return result;
    }

    public static IReadOnlyList<VoebbLoanItem> ParseLoansFromTextFallback(string html)
    {
        static string Normalize(string value)
            => AnyWhitespaceRegex.Replace(WebUtility.HtmlDecode(value), " ").Trim();

        static bool IsNoise(string line)
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                return true;
            }

            if (BracketedLineRegex.IsMatch(line))
            {
                return true;
            }

            if (NumericIdentifierRegex.IsMatch(line))
            {
                return true;
            }

            return FallbackNoiseRegex.IsMatch(line);
        }

        var withoutScripts = ScriptAndStyleRegex.Replace(html, string.Empty);
        var withBreaks = StructuralTagRegex.Replace(withoutScripts, "\n");
        var plainText = HtmlTagRegex.Replace(withBreaks, " ");

        var lines = plainText
            .Split('\n')
            .Select(Normalize)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .ToList();

        var results = new List<VoebbLoanItem>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < lines.Count; i++)
        {
            var dateMatch = DueDateRegex.Match(lines[i]);
            if (!dateMatch.Success)
            {
                continue;
            }

            var dueDate = dateMatch.Groups[1].Value;
            var window = lines.Skip(i).Take(12).ToList();
            if (!window.Any(LoanContextRegex.IsMatch))
            {
                continue;
            }

            var titleCandidates = window
                .Skip(1)
                .Where(x => !IsNoise(x))
                .Where(x => !DueDateRegex.IsMatch(x))
                .ToList();

            if (titleCandidates.Count == 0)
            {
                continue;
            }

            var loanName = titleCandidates[0];
            var title = string.Join(" | ", titleCandidates.Take(4));
            var key = $"{dueDate}|{title}";
            if (!seen.Add(key))
            {
                continue;
            }

            results.Add(new VoebbLoanItem
            {
                RenewIndex = -1,
                LoanName = loanName,
                Title = title,
                DueDate = dueDate,
                Details = string.Join(" | ", window.Take(6))
            });
        }

        return results;
    }
}
