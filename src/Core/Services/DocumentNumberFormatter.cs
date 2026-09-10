using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Kuestencode.Core.Services;

/// <summary>
/// Generiert fortlaufende Belegnummern (Rechnungen, Angebote, Projekte, Eingangsrechnungen, ...)
/// aus einem frei konfigurierbaren Format-Muster. Tokens: Y/YY/YYYY = Jahr (Anzahl Y = Anzahl
/// Ziffern), ein Lauf aus X = laufende Nummer (Anzahl X = Stellen mit führenden Nullen), alle
/// übrigen Zeichen sind literal. Beispiel: "YY-XXXXX-RE" -> "26-00001-RE".
/// </summary>
public static class DocumentNumberFormatter
{
    /// <summary>
    /// Ermittelt die nächste freie Nummer für das gegebene Format anhand der bereits
    /// vergebenen Nummern. Läuft die Zählung pro Jahr neu an, sobald das Format ein
    /// Y-Token enthält (nur Nummern der aktuellen Periode werden dann als Vergleichsbasis
    /// herangezogen); ohne Y-Token zählt sie fortlaufend.
    /// </summary>
    public static string GenerateNext(string format, DateTime referenceDate, IEnumerable<string> existingNumbers)
    {
        var matchRegex = BuildMatchRegex(format, referenceDate, out var sequenceLength);
        var lastNumber = GetLastSequenceNumber(matchRegex, existingNumbers);
        return Render(format, referenceDate, lastNumber + 1, sequenceLength);
    }

    /// <summary>
    /// Ermittelt die höchste bereits vergebene laufende Nummer für das Format/den Zeitraum,
    /// ohne sie zu inkrementieren. Dient als einmaliger Startwert für
    /// <see cref="NumberSequenceService"/>, wenn für einen Nummernkreis noch kein Zähler existiert.
    /// </summary>
    public static long GetLastSequenceNumber(string format, DateTime referenceDate, IEnumerable<string> existingNumbers)
    {
        var matchRegex = BuildMatchRegex(format, referenceDate, out _);
        return GetLastSequenceNumber(matchRegex, existingNumbers);
    }

    private static long GetLastSequenceNumber(Regex matchRegex, IEnumerable<string> existingNumbers)
    {
        long lastNumber = 0;
        foreach (var number in existingNumbers)
        {
            var match = matchRegex.Match(number);
            if (match.Success && long.TryParse(match.Groups["seq"].Value, out var seq) && seq > lastNumber)
            {
                lastNumber = seq;
            }
        }
        return lastNumber;
    }

    /// <summary>
    /// Zerlegt ein Format in den festen Teil vor dem X-Lauf (Prefix, inkl. gerendertem Jahr),
    /// den festen Teil danach (Suffix) und die Breite des X-Laufs. Damit lässt sich in der UI
    /// nur die laufende Nummer bearbeiten, ohne Präfix/Suffix/Jahreskürzel anzufassen.
    /// </summary>
    public static (string Prefix, string Suffix, int SequenceLength) SplitAroundSequence(string format, DateTime referenceDate)
    {
        var prefix = new StringBuilder();
        var suffix = new StringBuilder();
        var sequenceLength = 0;
        var sequenceFound = false;
        var i = 0;

        while (i < format.Length)
        {
            var c = format[i];
            var runLength = CountRun(format, i, c);

            if (c is 'X' or 'x')
            {
                sequenceLength = runLength;
                sequenceFound = true;
            }
            else
            {
                var rendered = c is 'Y' or 'y' ? YearDigits(referenceDate.Year, runLength) : new string(c, runLength);
                (sequenceFound ? suffix : prefix).Append(rendered);
            }

            i += runLength;
        }

        return (prefix.ToString(), suffix.ToString(), sequenceLength);
    }

    /// <summary>
    /// Rendert eine bereits bekannte, fortlaufende Nummer (z.B. aus <see cref="NumberSequenceService"/>)
    /// in das gegebene Format, ohne bestehende Nummern zu scannen.
    /// </summary>
    public static string Format(string format, DateTime referenceDate, long sequenceNumber)
    {
        var (_, _, sequenceLength) = SplitAroundSequence(format, referenceDate);
        return Render(format, referenceDate, sequenceNumber, sequenceLength);
    }

    private static Regex BuildMatchRegex(string format, DateTime referenceDate, out int sequenceLength)
    {
        var pattern = new StringBuilder("^");
        sequenceLength = 0;
        var i = 0;
        while (i < format.Length)
        {
            var c = format[i];
            var runLength = CountRun(format, i, c);

            if (c is 'Y' or 'y')
            {
                pattern.Append(Regex.Escape(YearDigits(referenceDate.Year, runLength)));
            }
            else if (c is 'X' or 'x')
            {
                sequenceLength = runLength;
                pattern.Append(@"(?<seq>\d+)");
            }
            else
            {
                pattern.Append(Regex.Escape(new string(c, runLength)));
            }

            i += runLength;
        }

        pattern.Append('$');
        return new Regex(pattern.ToString());
    }

    private static string Render(string format, DateTime referenceDate, long sequenceNumber, int sequenceLength)
    {
        var result = new StringBuilder();
        var i = 0;
        while (i < format.Length)
        {
            var c = format[i];
            var runLength = CountRun(format, i, c);

            if (c is 'Y' or 'y')
            {
                result.Append(YearDigits(referenceDate.Year, runLength));
            }
            else if (c is 'X' or 'x')
            {
                result.Append(sequenceNumber.ToString(CultureInfo.InvariantCulture).PadLeft(runLength, '0'));
            }
            else
            {
                result.Append(c, runLength);
            }

            i += runLength;
        }

        return result.ToString();
    }

    private static int CountRun(string s, int start, char c)
    {
        var length = 0;
        while (start + length < s.Length && s[start + length] == c)
        {
            length++;
        }
        return length;
    }

    private static string YearDigits(int year, int digits)
    {
        var full = year.ToString(CultureInfo.InvariantCulture);
        return digits >= full.Length ? full.PadLeft(digits, '0') : full[^digits..];
    }
}
