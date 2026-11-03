using System.Globalization;
using System.Text.RegularExpressions;

namespace Restaurant_Aqa_Framework.Support;

/// <summary>Reads an amount out of a currency string such as "$10.00" or "Grand Total $0.00".</summary>
public static partial class MoneyText
{
    public static decimal Parse(string text)
    {
        var match = Amount().Match(text);
        if (!match.Success)
            throw new FormatException($"No amount in '{text}'.");

        return decimal.Parse(match.Value.Replace(",", "", StringComparison.Ordinal), CultureInfo.InvariantCulture);
    }

    [GeneratedRegex(@"\d{1,3}(?:,\d{3})*(?:\.\d+)?|\d+(?:\.\d+)?")]
    private static partial Regex Amount();
}
