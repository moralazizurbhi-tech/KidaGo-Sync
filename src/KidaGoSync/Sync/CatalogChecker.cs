using System.IO;
using System.Text;

namespace KidaGoSync.Sync;

/// <summary>The rule that applied to a line (FEAT-005 C17). A line falls under exactly one.</summary>
public enum CatalogRule
{
    /// <summary>12 digits: one leading zero added (a UPC-A code becomes EAN-13).</summary>
    Padded,
    /// <summary>Kept, but surrounding whitespace was cleaned.</summary>
    Trimmed,
    /// <summary>Removed: nothing but whitespace.</summary>
    Blank,
    /// <summary>Removed: contains a character that is not an ASCII digit, whatever its length.</summary>
    NonDigit,
    /// <summary>Removed: digits only, more than 13.</summary>
    TooLong,
    /// <summary>Removed: the text after the code's separator is not a price (digits with at most two decimals) (FEAT-005 C22).</summary>
    PriceInvalid,
    /// <summary>Removed: the code was already on an earlier line with another price; the first line prevails (FEAT-005 C23).</summary>
    RepeatedCode,
}

public sealed record LineExample(int LineNumber, string Value);

/// <summary>[Count] lines fell under [Rule]; [Examples] holds the first <see cref="CatalogChecker.MaxExamples"/>.</summary>
public sealed record RuleSummary(CatalogRule Rule, int Count, IReadOnlyList<LineExample> Examples);

public sealed record CatalogCheckResult(IReadOnlyList<RuleSummary> Rules, string NormalizedText, int ValidLineCount)
{
    /// <summary>Nothing to correct: the file can be sent as it is ("Catálogo correcto"). A file with no line at all is never clean: the app rejects it.</summary>
    public bool IsClean => Rules.Count == 0 && ValidLineCount > 0;

    /// <summary>Nothing valid would remain, so there is nothing to save or send.</summary>
    public bool HasNoValidLine => ValidLineCount == 0;
}

/// <summary>
/// Classifies and normalizes a catalog file's lines (FEAT-005 TD, CatalogChecker; C17, C21, C22, C23). A pure function
/// of the text, so every caller (Comprobar, the corrected copy, "Normalizar e importar") applies the same rules. A line
/// is a code, optionally followed by a price after the first `;` or `,`.
/// </summary>
public static class CatalogChecker
{
    public const int MaxExamples = 10;

    /// <summary>Checks text already decoded. Each line keeps its own terminator, so the file's line endings are unchanged.</summary>
    public static CatalogCheckResult Check(string text)
    {
        var counts = new Dictionary<CatalogRule, int>();
        var examples = new Dictionary<CatalogRule, List<LineExample>>();
        var output = new StringBuilder(text.Length);
        var valid = 0;
        var number = 0;
        var start = 0;
        var prices = new Dictionary<string, long?>(); // code -> price of its first kept line
        while (start < text.Length)
        {
            var end = text.IndexOfAny(['\r', '\n'], start);
            var contentEnd = end < 0 ? text.Length : end;
            var terminatorEnd = end < 0 ? text.Length : (text[end] == '\r' && end + 1 < text.Length && text[end + 1] == '\n' ? end + 2 : end + 1);
            number++;
            var raw = text[start..contentEnd];
            var (rule, normalized) = Classify(raw, prices);
            if (rule is not null)
            {
                counts[rule.Value] = counts.GetValueOrDefault(rule.Value) + 1;
                if (!examples.TryGetValue(rule.Value, out var list)) examples[rule.Value] = list = [];
                if (list.Count < MaxExamples) list.Add(new LineExample(number, raw));
            }
            if (rule is null or CatalogRule.Padded or CatalogRule.Trimmed)
            {
                valid++;
                output.Append(normalized).Append(text, contentEnd, terminatorEnd - contentEnd);
            }
            start = terminatorEnd;
        }
        var rules = counts.Keys.Order().Select(r => new RuleSummary(r, counts[r], examples[r])).ToList();
        return new CatalogCheckResult(rules, output.ToString(), valid);
    }

    /// <summary>
    /// Checks a file's bytes and returns the normalized bytes in the same encoding (a BOM is kept; no BOM is read as UTF-8).
    /// </summary>
    public static (CatalogCheckResult Result, byte[] NormalizedBytes) CheckBytes(byte[] bytes)
    {
        using var reader = new StreamReader(new MemoryStream(bytes), new UTF8Encoding(false), detectEncodingFromByteOrderMarks: true);
        var text = reader.ReadToEnd();
        var encoding = reader.CurrentEncoding;
        var result = Check(text);
        var preamble = encoding.GetPreamble();
        var hasBom = preamble.Length > 0 && bytes.AsSpan().StartsWith(preamble);
        var body = encoding.GetBytes(result.NormalizedText);
        return (result, hasBom ? [.. preamble, .. body] : body);
    }

    /// <summary>Digits with at most two decimals after `.` or `,`, in whole cents; null when the text is not a price.</summary>
    public static long? PriceCents(string text)
    {
        var m = PriceFormat.Match(text);
        if (!m.Success) return null;
        return long.Parse(m.Groups[1].Value) * 100 + long.Parse(m.Groups[2].Value.PadRight(2, '0'));
    }

    private static readonly System.Text.RegularExpressions.Regex PriceFormat = new(@"^(\d{1,9})(?:[.,](\d{1,2}))?$");

    // The rule (null: a valid line left untouched) and the line as it is kept. The code is decided first (non-digit before
    // length), then the price, then a repeat with another price; a kept line's code is remembered with its price.
    private static (CatalogRule? Rule, string Normalized) Classify(string raw, Dictionary<string, long?> prices)
    {
        var line = raw.Trim();
        if (line.Length == 0) return (CatalogRule.Blank, "");
        var at = line.IndexOfAny([';', ',']);
        var code = (at < 0 ? line : line[..at]).Trim();
        var price = at < 0 ? null : line[(at + 1)..].Trim();
        if (code.Length == 0 || code.Any(c => c is < '0' or > '9')) return (CatalogRule.NonDigit, "");
        if (code.Length > 13) return (CatalogRule.TooLong, "");
        long? cents = null;
        if (price is not null && (cents = PriceCents(price)) is null) return (CatalogRule.PriceInvalid, "");
        var padded = code.Length == 12 ? "0" + code : code;
        if (prices.TryGetValue(padded, out var first))
        {
            if (first != cents) return (CatalogRule.RepeatedCode, "");
        }
        else prices[padded] = cents;
        var normalized = price is null ? padded : padded + line[at] + price;
        if (code.Length == 12) return (CatalogRule.Padded, normalized);
        return (normalized != raw ? CatalogRule.Trimmed : null, normalized);
    }
}
