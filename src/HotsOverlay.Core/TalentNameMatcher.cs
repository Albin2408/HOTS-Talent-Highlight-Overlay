using System.Text;

namespace HotsOverlay.Core;

/// <summary>
/// A talent found on screen: which tier it belongs to and where its card is. <see cref="IsAlternate"/>
/// marks a situational "or consider..." talent rather than the build's main pick for the tier.
/// </summary>
public sealed record TalentMatch(TalentTier Tier, double Score, BoxF NameBox, BoxF CardBox, bool IsAlternate = false)
{
    /// <summary>The name that was matched (the pick, or the alternate that was seen).</summary>
    public string TalentName { get; init; } = Tier.PickName;
}

/// <summary>
/// Finds a build's recommended talents among OCR'd screen text. HOTS's talent panel prints each
/// talent's name as text on its card, and the scraped build already knows the recommended names,
/// so we locate the card by name instead of computing slot positions from a calibrated region —
/// which means no calibration, no level reading, and no dependence on resolution/UI layout.
/// </summary>
public static class TalentNameMatcher
{
    public const double MinScore = 0.80;

    /// <summary>Words on the same OCR line allowed beyond the matched name (e.g. a stray hotkey
    /// badge). Keeps a card *description* that merely mentions another talent from matching.</summary>
    private const int MaxExtraWordsOnLine = 1;

    /// <summary>Where the build's main picks are on screen, best match first.</summary>
    public static IReadOnlyList<TalentMatch> Find(IReadOnlyList<OcrLineBox> lines, HeroBuild build) =>
        FindAll(lines, build).Where(m => !m.IsAlternate).ToList();

    /// <summary>Main picks and situational alternates, best match first.</summary>
    public static IReadOnlyList<TalentMatch> FindAll(IReadOnlyList<OcrLineBox> lines, HeroBuild build)
    {
        var matches = new List<TalentMatch>();

        foreach (var tier in build.Tiers)
        {
            if (BestMatch(lines, tier, tier.PickName, isAlternate: false) is { } pick)
                matches.Add(pick);

            foreach (var alternate in tier.Alternates)
            {
                if (BestMatch(lines, tier, alternate, isAlternate: true) is { } alt)
                    matches.Add(alt);
            }
        }

        return matches
            .OrderByDescending(m => m.Score)
            .ThenByDescending(m => m.NameBox.Height)
            .ToList();
    }

    private static TalentMatch? BestMatch(IReadOnlyList<OcrLineBox> lines, TalentTier tier, string talentName, bool isAlternate)
    {
        string target = Normalize(talentName);
        if (target.Length < 3)
            return null;

        int targetWords = target.Count(c => c == ' ') + 1;
        TalentMatch? best = null;

        foreach (var line in lines)
        {
            var words = line.Words;
            for (int start = 0; start < words.Count; start++)
            {
                for (int len = Math.Max(1, targetWords - 1); len <= targetWords + 1 && start + len <= words.Count; len++)
                {
                    int extra = words.Count - len;
                    if (extra > MaxExtraWordsOnLine)
                        continue;

                    string candidate = Normalize(string.Join(' ', words.Skip(start).Take(len).Select(w => w.Text)));
                    double score = Similarity(candidate, target);
                    if (score < MinScore || (best is not null && score <= best.Score))
                        continue;

                    var nameBox = words.Skip(start).Take(len).Select(w => w.Box).Aggregate(BoxF.Union);
                    best = new TalentMatch(tier, score, nameBox, CardBoxFor(nameBox, line, lines), isAlternate)
                    {
                        TalentName = talentName,
                    };
                }
            }
        }

        return best;
    }

    /// <summary>Estimates the whole card's bounds from its title text (and the description line
    /// under it, if the OCR found one). Proportions are relative to the title's text height, so
    /// they hold across resolutions and UI scales.</summary>
    internal static BoxF CardBoxFor(BoxF name, OcrLineBox own, IReadOnlyList<OcrLineBox> lines)
    {
        double h = name.Height;

        var description = lines
            .Where(l => !ReferenceEquals(l, own)
                        && l.Box.Y >= name.Y + h * 0.5
                        && l.Box.Y <= name.Bottom + 3 * h
                        && Math.Abs(l.Box.X - name.X) <= 2 * h)
            .OrderBy(l => l.Box.Y)
            .Select(l => (BoxF?)l.Box)
            .FirstOrDefault();

        double left = Math.Max(0, name.X - 3.6 * h);
        double top = Math.Max(0, name.Y - 1.1 * h);
        double right = Math.Max(name.Right, description?.Right ?? 0) + 4.5 * h;
        double bottom = (description?.Bottom ?? name.Bottom + 1.5 * h) + 1.0 * h;
        return new BoxF(left, top, right - left, bottom - top);
    }

    /// <summary>
    /// Text spans that could be a card's title: each short OCR line whole, or minus its first or last
    /// word (a stray icon glyph / hotkey badge). Long lines are descriptions, so they're skipped.
    /// Few spans per line keeps scanning cheap even against hundreds of known talent names.
    /// </summary>
    internal static IEnumerable<(string Normalized, BoxF Box)> TitleCandidates(IReadOnlyList<OcrLineBox> lines)
    {
        foreach (var line in lines)
        {
            var words = line.Words;
            int n = words.Count;
            if (n == 0 || n > 8)
                continue;

            var spans = new List<(int Start, int Length)> { (0, n) };
            if (n > 1)
            {
                spans.Add((1, n - 1));
                spans.Add((0, n - 1));
            }

            foreach (var (start, length) in spans)
            {
                var span = words.Skip(start).Take(length).ToList();
                string normalized = Normalize(string.Join(' ', span.Select(w => w.Text)));
                if (normalized.Length > 0)
                    yield return (normalized, span.Select(w => w.Box).Aggregate(BoxF.Union));
            }
        }
    }

    /// <summary>Lowercases, drops apostrophes, turns other punctuation into spaces, collapses whitespace.</summary>
    public static string Normalize(string text)
    {
        var sb = new StringBuilder(text.Length);
        bool pendingSpace = false;

        foreach (char c in text)
        {
            if (c is '\'' or '’' or '`')
                continue;

            if (char.IsLetterOrDigit(c))
            {
                if (pendingSpace && sb.Length > 0)
                    sb.Append(' ');
                sb.Append(char.ToLowerInvariant(c));
                pendingSpace = false;
            }
            else
            {
                pendingSpace = true;
            }
        }

        return sb.ToString();
    }

    /// <summary>1 - (edit distance / longer length): 1.0 identical, tolerant of an OCR typo or two.</summary>
    public static double Similarity(string a, string b)
    {
        if (a.Length == 0 && b.Length == 0)
            return 1;
        int longest = Math.Max(a.Length, b.Length);
        return 1.0 - (double)EditDistance(a, b) / longest;
    }

    private static int EditDistance(string a, string b)
    {
        var previous = new int[b.Length + 1];
        var current = new int[b.Length + 1];
        for (int j = 0; j <= b.Length; j++)
            previous[j] = j;

        for (int i = 1; i <= a.Length; i++)
        {
            current[0] = i;
            for (int j = 1; j <= b.Length; j++)
            {
                int cost = a[i - 1] == b[j - 1] ? 0 : 1;
                current[j] = Math.Min(Math.Min(current[j - 1] + 1, previous[j] + 1), previous[j - 1] + cost);
            }
            (previous, current) = (current, previous);
        }

        return previous[b.Length];
    }
}
