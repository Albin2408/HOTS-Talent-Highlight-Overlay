namespace HotsOverlay.Core;

/// <summary>A hero inferred from talent names seen on screen.</summary>
public sealed record HeroGuess(string Slug, string Name, double Score, IReadOnlyList<string> MatchedTalents);

/// <summary>
/// Works out which hero you're playing from the talent card names on screen. HOTS lists a hero's
/// talents on its panel (at level 1 you're choosing one immediately), and talent names are almost
/// unique per hero (~99% in the bundled data), so even a single recognized name usually pins the
/// hero down. A name shared by several heroes is ambiguous and never decides on its own.
/// </summary>
public sealed class HeroDetector
{
    /// <summary>Short names ("Rage") are too easy to mistake for ordinary on-screen words.</summary>
    private const int MinNameLength = 5;

    private const double FuzzyMinSimilarity = 0.86;

    /// <summary>The best hero needs at least one near-exact name...</summary>
    private const double MinTopScore = 0.9;

    /// <summary>...and must clearly beat the runner-up, so shared names stay inconclusive.</summary>
    private const double MinLead = 0.5;

    /// <summary>Consecutive scans that must agree before <see cref="Observe"/> reports a hero.</summary>
    public const int ConfirmationsRequired = 2;

    private readonly Dictionary<string, List<(string Slug, string Name)>> _owners = new();
    private readonly Dictionary<int, List<string>> _namesByLength = new();

    private string? _pendingSlug;
    private int _pendingCount;

    public HeroDetector(IEnumerable<HeroBuildSet> heroes)
    {
        foreach (var hero in heroes)
        {
            foreach (var pickName in hero.Builds.SelectMany(b => b.Tiers).SelectMany(t => t.Alternates.Prepend(t.PickName)))
            {
                string name = TalentNameMatcher.Normalize(pickName);
                if (name.Length < MinNameLength)
                    continue;

                if (!_owners.TryGetValue(name, out var owners))
                {
                    _owners[name] = owners = new List<(string, string)>();
                    if (!_namesByLength.TryGetValue(name.Length, out var bucket))
                        _namesByLength[name.Length] = bucket = new List<string>();
                    bucket.Add(name);
                }

                if (owners.All(o => o.Slug != hero.HeroSlug))
                    owners.Add((hero.HeroSlug, hero.HeroName));
            }
        }
    }

    /// <summary>Single-scan guess; null when the screen doesn't clearly show one hero's talents.</summary>
    public HeroGuess? Guess(IReadOnlyList<OcrLineBox> lines)
    {
        var seen = new Dictionary<string, double>(); // known talent name -> best similarity on screen

        foreach (var (text, _) in TalentNameMatcher.TitleCandidates(lines))
        {
            if (text.Length < MinNameLength)
                continue;

            if (_owners.ContainsKey(text))
            {
                seen[text] = 1.0;
                continue;
            }

            for (int length = text.Length - 2; length <= text.Length + 2; length++)
            {
                if (!_namesByLength.TryGetValue(length, out var bucket))
                    continue;

                foreach (var name in bucket)
                {
                    double similarity = TalentNameMatcher.Similarity(text, name);
                    if (similarity >= FuzzyMinSimilarity && similarity > seen.GetValueOrDefault(name))
                        seen[name] = similarity;
                }
            }
        }

        if (seen.Count == 0)
            return null;

        var scores = new Dictionary<string, (string Name, double Score, List<string> Talents)>();
        foreach (var (name, similarity) in seen)
        {
            var owners = _owners[name];
            double weight = similarity / owners.Count; // a shared name is only weak evidence for each owner
            foreach (var (slug, heroName) in owners)
            {
                var entry = scores.GetValueOrDefault(slug, (heroName, 0.0, new List<string>()));
                entry.Item3.Add(name);
                scores[slug] = (heroName, entry.Item2 + weight, entry.Item3);
            }
        }

        var ranked = scores.OrderByDescending(s => s.Value.Score).ToList();
        var top = ranked[0];
        double runnerUp = ranked.Count > 1 ? ranked[1].Value.Score : 0;

        if (top.Value.Score < MinTopScore || top.Value.Score - runnerUp < MinLead)
            return null;

        return new HeroGuess(top.Key, top.Value.Name, top.Value.Score, top.Value.Talents);
    }

    /// <summary>
    /// Feeds one scan; returns the hero only once <see cref="ConfirmationsRequired"/> consecutive
    /// scans agree, so a stray word in chat can't flip your hero. Scans that show nothing leave the
    /// pending guess alone (the panel can flicker between OCR frames).
    /// </summary>
    public HeroGuess? Observe(IReadOnlyList<OcrLineBox> lines)
    {
        var guess = Guess(lines);
        if (guess is null)
            return null;

        if (guess.Slug == _pendingSlug)
        {
            _pendingCount++;
        }
        else
        {
            _pendingSlug = guess.Slug;
            _pendingCount = 1;
        }

        return _pendingCount >= ConfirmationsRequired ? guess : null;
    }
}
