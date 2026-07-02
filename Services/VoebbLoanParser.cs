using System.Net;
using System.Text.RegularExpressions;
using BiboWebBot.Models;

namespace BiboWebBot.Services;

public static class VoebbLoanParser
{
    public static IReadOnlyList<VoebbLoanItem> ParseLoansFromHtml(string html)
    {
        static string CleanText(string raw)
        {
            var withLineBreaks = Regex.Replace(raw, "<br\\s*/?>", "\n", RegexOptions.IgnoreCase);
            var withoutTags = Regex.Replace(withLineBreaks, "<.*?>", " ", RegexOptions.Singleline);
            var decoded = WebUtility.HtmlDecode(withoutTags);
            return Regex.Replace(decoded, "[ \t\r\f\v]+", " ").Trim();
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

                if (Regex.IsMatch(line, "^\\[.*\\]$"))
                {
                    continue;
                }

                if (Regex.IsMatch(line, "^\\d{6,}$"))
                {
                    continue;
                }

                if (Regex.IsMatch(line, "sprecher|autor|regie|bibliothek|fällig|verlängerung|abholcode|konto|hinweis", RegexOptions.IgnoreCase))
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
        var rows = Regex.Matches(html, "<tr[^>]*>(?<row>.*?)</tr>", RegexOptions.IgnoreCase | RegexOptions.Singleline);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (Match rowMatch in rows)
        {
            var rowHtml = rowMatch.Groups["row"].Value;
            var cellMatches = Regex.Matches(rowHtml, "<t[dh][^>]*>(?<cell>.*?)</t[dh]>", RegexOptions.IgnoreCase | RegexOptions.Singleline);
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

            var dateRegex = new Regex("(\\d{1,2}\\.\\d{1,2}\\.\\d{2,4})", RegexOptions.Compiled);
            var dueCellIndex = cells.FindIndex(cell => dateRegex.IsMatch(cell));
            if (dueCellIndex < 0)
            {
                continue;
            }

            var dueDate = dateRegex.Match(cells[dueCellIndex]).Groups[1].Value;
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
                        && !dateRegex.IsMatch(cell));

                if (string.IsNullOrWhiteSpace(titleFallback))
                {
                    continue;
                }

                titleText = titleFallback;
            }

            var lines = Regex.Split(titleText, "\\s*\\|\\s*|\\r?\\n").Where(x => !string.IsNullOrWhiteSpace(x));
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
            => Regex.Replace(WebUtility.HtmlDecode(value), "\\s+", " ").Trim();

        static bool IsNoise(string line)
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                return true;
            }

            if (Regex.IsMatch(line, "^\\[.*\\]$"))
            {
                return true;
            }

            if (Regex.IsMatch(line, "^\\d{6,}$"))
            {
                return true;
            }

            return Regex.IsMatch(line, "sprecher|autor|regie|hinweis|heute verlängert|verlängerung|abholcode", RegexOptions.IgnoreCase);
        }

        var withoutScripts = Regex.Replace(html, "<script.*?</script>|<style.*?</style>", string.Empty, RegexOptions.IgnoreCase | RegexOptions.Singleline);
        var withBreaks = Regex.Replace(withoutScripts, "</?(tr|td|th|div|li|p|br|h1|h2|h3)[^>]*>", "\n", RegexOptions.IgnoreCase);
        var plainText = Regex.Replace(withBreaks, "<.*?>", " ", RegexOptions.Singleline);

        var lines = plainText
            .Split('\n')
            .Select(Normalize)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .ToList();

        var results = new List<VoebbLoanItem>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var dateRegex = new Regex("(\\d{1,2}\\.\\d{1,2}\\.\\d{2,4})", RegexOptions.Compiled);

        for (var i = 0; i < lines.Count; i++)
        {
            var dateMatch = dateRegex.Match(lines[i]);
            if (!dateMatch.Success)
            {
                continue;
            }

            var dueDate = dateMatch.Groups[1].Value;
            var window = lines.Skip(i).Take(12).ToList();
            if (!window.Any(x => Regex.IsMatch(x, "bibliothek|fällig|verläng|ausleih|entliehen|loan|checkout", RegexOptions.IgnoreCase)))
            {
                continue;
            }

            var titleCandidates = window
                .Skip(1)
                .Where(x => !IsNoise(x))
                .Where(x => !dateRegex.IsMatch(x))
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
