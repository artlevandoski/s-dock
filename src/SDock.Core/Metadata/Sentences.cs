using System.Text.RegularExpressions;

namespace SDock.Core.Metadata;

/// <summary>Short text for the carousel: the first sentence of a description.</summary>
public static partial class Sentences
{
    // Words whose full stop doesn't end a sentence.
    private static readonly HashSet<string> Abbreviations = new(StringComparer.OrdinalIgnoreCase)
    {
        "mr", "mrs", "ms", "dr", "st", "jr", "sr", "vs", "vol", "no", "ep", "pt", "inc", "ltd", "co", "etc", "e.g", "i.e", "u.s",
    };

    [GeneratedRegex(@"[ \t ]+")]
    private static partial Regex SpacesRe();

    /// <summary>
    /// The first sentence (or first line, if that's shorter), at most <paramref name="maxLength"/> characters,
    /// cut at a word with "…" if longer. Empty for no text.
    /// </summary>
    public static string First(string? text, int maxLength = 220)
    {
        if (string.IsNullOrWhiteSpace(text)) return "";
        var lines = text.Replace("\r", "").Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        // A very short first line is usually a tagline in capitals ("STEP INTO A WORLD OF ADVENTURE"); keep it only if alone.
        var line = lines.Length > 1 && lines[0].Length < 12 ? lines[1] : lines[0];
        line = SpacesRe().Replace(line, " ");

        var end = -1;
        for (var i = 0; i < line.Length; i++)
        {
            var c = line[i];
            if (c is not ('.' or '!' or '?' or '。' or '！' or '？')) continue;
            // An ellipsis or a decimal ("2.5") isn't the end; nor is "Vol." and the like.
            var next = i + 1 < line.Length ? line[i + 1] : ' ';
            if (c == '.' && (next == '.' || char.IsDigit(next) || (i > 0 && line[i - 1] == '.'))) continue;
            if (c == '.' && Abbreviations.Contains(WordBefore(line, i))) continue;
            // Closing quotes or brackets belong to the sentence.
            var stop = i;
            while (stop + 1 < line.Length && line[stop + 1] is '"' or '\'' or '”' or '’' or ')' or '»') stop++;
            if (stop + 1 >= line.Length || char.IsWhiteSpace(line[stop + 1]) || c is '。' or '！' or '？')
            {
                end = stop;
                break;
            }
        }
        var sentence = end >= 0 ? line[..(end + 1)] : line;
        if (sentence.Length <= maxLength) return sentence;
        var cut = sentence.LastIndexOf(' ', maxLength - 1);
        return sentence[..(cut > maxLength / 2 ? cut : maxLength - 1)].TrimEnd(',', ';', ':', ' ', '-') + "…";
    }

    private static string WordBefore(string s, int dot)
    {
        var start = dot;
        while (start > 0 && (char.IsLetter(s[start - 1]) || s[start - 1] == '.')) start--;
        return s[start..dot];
    }
}
