using HotsOverlay.Core;
using HotsOverlay.Scraper;

namespace HotsOverlay.App;

/// <summary>Every hero's builds loaded into memory, plus the detector that recognizes a hero by its talents.</summary>
public sealed class HeroCatalog
{
    private readonly Dictionary<string, HeroBuildSet> _bySlug;

    public IReadOnlyList<HeroBuildSet> Sets { get; }
    public HeroDetector Detector { get; }

    private HeroCatalog(List<HeroBuildSet> sets)
    {
        Sets = sets;
        _bySlug = sets.ToDictionary(s => s.HeroSlug);
        Detector = new HeroDetector(sets);
    }

    public HeroBuildSet? Find(string slug) => _bySlug.GetValueOrDefault(slug);

    public static async Task<HeroCatalog> LoadAsync(BuildCache cache)
    {
        var sets = new List<HeroBuildSet>();
        foreach (var hero in await cache.GetOrFetchHeroIndexAsync())
        {
            try
            {
                sets.Add(await cache.GetOrFetchAsync(hero));
            }
            catch (BuildDataUnavailableException)
            {
                // a hero with no data just can't be auto-detected
            }
        }

        return new HeroCatalog(sets);
    }
}
