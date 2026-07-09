using BiboWebBot.VoebbParsing;

namespace BiboWebBot.Tests;

public class VoebbLoanParserTests
{
    [Fact]
    public void ParseLoansFromHtml_ExtractsTableRowsWithDueDatesAndTitles()
    {
        var html = """
        <table>
          <tbody>
            <tr>
              <td><input type="checkbox" /></td>
              <td>14.07.2026</td>
              <td>Frist: 14.07.2026</td>
              <td>
                <strong>Die Unsterblichkeit der Hunde</strong><br />
                Kostas Mavroudis
              </td>
            </tr>
          </tbody>
        </table>
        """;

        var loans = VoebbLoanParser.ParseLoansFromHtml(html);

        Assert.Single(loans);
        Assert.StartsWith("Die Unsterblichkeit der Hunde", loans[0].LoanName);
        Assert.Contains("Kostas Mavroudis", loans[0].Title);
        Assert.Equal("14.07.2026", loans[0].DueDate);
    }

    [Fact]
    public void ParseLoansFromTextFallback_ExtractsLoanFromPlainText()
    {
        var html = """
        <div>
          <p>Mein Konto</p>
          <p>Fällig am 01.08.2026</p>
          <p>Berlin in Books</p>
          <p>Vincenzo Latronico</p>
        </div>
        """;

        var loans = VoebbLoanParser.ParseLoansFromTextFallback(html);

        Assert.Single(loans);
        Assert.Equal("Berlin in Books", loans[0].LoanName);
        Assert.Contains("Berlin in Books", loans[0].Title);
        Assert.Equal("01.08.2026", loans[0].DueDate);
    }

    [Fact]
    public void ParseLoansFromHtml_DeduplicatesRepeatedRows()
    {
        var html = """
        <table>
          <tr><td>01.09.2026</td><td>Hinweis</td><td>Pop-Up-Sommerbibliothek</td></tr>
          <tr><td>01.09.2026</td><td>Hinweis</td><td>Pop-Up-Sommerbibliothek</td></tr>
        </table>
        """;

        var loans = VoebbLoanParser.ParseLoansFromHtml(html);

        Assert.Single(loans);
    }
}
