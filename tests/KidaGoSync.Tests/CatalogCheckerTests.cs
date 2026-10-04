using System.Text;
using KidaGoSync.Sync;

namespace KidaGoSync.Tests;

public class CatalogCheckerTests
{
    private static int Count(CatalogCheckResult r, CatalogRule rule) => r.Rules.FirstOrDefault(s => s.Rule == rule)?.Count ?? 0;

    [Fact]
    public void ThirteenDigitLineIsKeptUnchangedAndTheFileIsClean()
    {
        var r = CatalogChecker.Check("8601153100055\n");
        Assert.True(r.IsClean);
        Assert.Equal("8601153100055\n", r.NormalizedText);
        Assert.Equal(1, r.ValidLineCount);
    }

    [Fact]
    public void TwelveDigitLineGetsOneLeadingZero()
    {
        var r = CatalogChecker.Check("012345678905\n");
        Assert.Equal("0012345678905\n", r.NormalizedText);
        Assert.Equal(1, Count(r, CatalogRule.Padded));
        Assert.False(r.IsClean);
    }

    [Fact]
    public void ShortLineIsRemovedAndCountedAsTooShort()
    {
        var r = CatalogChecker.Check("4444\n8601153100055\n");
        Assert.Equal("8601153100055\n", r.NormalizedText);
        Assert.Equal(1, Count(r, CatalogRule.TooShort));
        Assert.Equal(new LineExample(1, "4444"), r.Rules.Single().Examples.Single());
    }

    [Fact]
    public void FourteenDigitLineIsRemovedAndCountedAsTooLong()
    {
        var r = CatalogChecker.Check("12345678901234\n");
        Assert.Equal(1, Count(r, CatalogRule.TooLong));
        Assert.True(r.HasNoValidLine);
        Assert.Equal("", r.NormalizedText);
    }

    [Theory]
    [InlineData("X0016SA1Z7")]            // short, with letters
    [InlineData("12345678901X")]           // 12 characters, with a letter
    [InlineData("12345678901234567890X")]  // long, with a letter
    [InlineData("123 4567890123")]         // inner space
    [InlineData("١٢٣٤٥٦٧٨٩٠١٢٣")]          // non-ASCII digits
    public void ALineWithANonDigitIsNonDigitWhateverItsLength(string line)
    {
        var r = CatalogChecker.Check(line + "\n");
        Assert.Equal(1, Count(r, CatalogRule.NonDigit));
        Assert.Equal(0, Count(r, CatalogRule.TooShort) + Count(r, CatalogRule.TooLong));
    }

    [Fact]
    public void BlankLinesAreRemovedAndSurroundingWhitespaceIsCleaned()
    {
        var r = CatalogChecker.Check("  8601153100055 \n\n   \n 012345678905\t\n");
        Assert.Equal("8601153100055\n0012345678905\n", r.NormalizedText);
        Assert.Equal(2, Count(r, CatalogRule.Blank));
        Assert.Equal(1, Count(r, CatalogRule.Trimmed));
        Assert.Equal(1, Count(r, CatalogRule.Padded));
    }

    [Fact]
    public void DuplicatesAreLeftUntouched()
    {
        var r = CatalogChecker.Check("8601153100055\n8601153100055\n");
        Assert.True(r.IsClean);
        Assert.Equal(2, r.ValidLineCount);
    }

    [Fact]
    public void CountsAreCompleteButExamplesStopAtTenWithLineNumbers()
    {
        var r = CatalogChecker.Check(string.Concat(Enumerable.Repeat("12345678901234\n", 25)));
        var summary = r.Rules.Single();
        Assert.Equal(25, summary.Count);
        Assert.Equal(10, summary.Examples.Count);
        Assert.Equal(Enumerable.Range(1, 10), summary.Examples.Select(e => e.LineNumber));
    }

    [Fact]
    public void LineEndingsAreKeptAsTheSourceHasThem()
    {
        Assert.Equal("8601153100055\r\n0012345678905\r\n", CatalogChecker.Check("8601153100055\r\nXX\r\n012345678905\r\n").NormalizedText);
        Assert.Equal("8601153100055\n0012345678905", CatalogChecker.Check("8601153100055\nXX\n012345678905").NormalizedText); // no final newline stays so
    }

    [Fact]
    public void BytesKeepTheirEncodingAndBom()
    {
        var utf8Bom = new UTF8Encoding(true);
        var source = utf8Bom.GetPreamble().Concat(Encoding.UTF8.GetBytes("012345678905\r\nbad\r\n")).ToArray();
        var (_, bom) = CatalogChecker.CheckBytes(source);
        Assert.Equal(utf8Bom.GetPreamble().Concat(Encoding.UTF8.GetBytes("0012345678905\r\n")).ToArray(), bom);

        var (_, plain) = CatalogChecker.CheckBytes(Encoding.UTF8.GetBytes("012345678905\n"));
        Assert.Equal(Encoding.UTF8.GetBytes("0012345678905\n"), plain);

        var utf16 = new UnicodeEncoding(false, true);
        var (_, wide) = CatalogChecker.CheckBytes(utf16.GetPreamble().Concat(utf16.GetBytes("012345678905\n")).ToArray());
        Assert.Equal(utf16.GetPreamble().Concat(utf16.GetBytes("0012345678905\n")).ToArray(), wide);
    }

    [Fact]
    public void EmptyFileHasNoValidLine()
    {
        Assert.True(CatalogChecker.Check("").HasNoValidLine);
    }
}
