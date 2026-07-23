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
          <tr><td>Fällig: 01.09.2026</td><td>Hinweis</td><td>Pop-Up-Sommerbibliothek</td></tr>
          <tr><td>Fällig: 01.09.2026</td><td>Hinweis</td><td>Pop-Up-Sommerbibliothek</td></tr>
        </table>
        """;

        var loans = VoebbLoanParser.ParseLoansFromHtml(html);

        Assert.Single(loans);
    }

    [Fact]
    public void ParseLoansFromHtml_IgnoresDatedAccountOverviewRowsWhenThereAreNoLoans()
    {
        var html = """
        <h2>Ausleihen</h2>
        <table>
          <tr><td>20.01.2027</td><td>Information</td><td>Kontostand vom:</td></tr>
          <tr><td>22.07.2026</td><td>Information</td><td>93 Mahnungen</td></tr>
        </table>
        """;

        var loans = VoebbLoanParser.ParseLoansFromHtml(html);

        Assert.Empty(loans);
    }

    [Fact]
    public void ParseLoansFromTextFallback_IgnoresUnrelatedDatesNearLoansHeading()
    {
        var html = """
        <div>
          <h2>Ausleihen</h2>
          <p>Kontostand vom:</p>
          <p>20.01.2027</p>
          <p>93 Mahnungen</p>
          <p>22.07.2026</p>
        </div>
        """;

        var loans = VoebbLoanParser.ParseLoansFromTextFallback(html);

        Assert.Empty(loans);
    }

    [Fact]
    public void ParseLoansFromTextFallback_IgnoresScriptContent()
    {
        var html = """
        <script>const sample = 'Fällig am 01.01.2026';</script>
        <div>
          <p>Fällig am 04.09.2026</p>
          <p>Der echte Titel</p>
          <p>Bibliothek am Wasserturm</p>
        </div>
        """;

        var loans = VoebbLoanParser.ParseLoansFromTextFallback(html);

        var loan = Assert.Single(loans);
        Assert.Equal("04.09.2026", loan.DueDate);
        Assert.Equal("Der echte Titel", loan.LoanName);
    }
}
