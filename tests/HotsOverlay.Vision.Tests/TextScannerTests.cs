using System.Drawing;
using System.Drawing.Text;
using HotsOverlay.Core;
using HotsOverlay.Vision;

namespace HotsOverlay.Vision.Tests;

/// <summary>
/// Drives the real Windows OCR engine over a rendered imitation of HOTS's talent card list
/// (bold white title + smaller description on a dark panel) to prove the whole pipeline —
/// bitmap -> WinRT OCR -> boxes -> name matcher — works, without needing the game running.
/// </summary>
public class TextScannerTests
{
    private static Bitmap RenderPanel(params (string Title, string Description)[] cards)
    {
        var bmp = new Bitmap(520, 80 + cards.Length * 100, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(bmp);
        g.Clear(Color.FromArgb(28, 30, 70));
        g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;

        using var titleFont = new Font("Segoe UI", 17, FontStyle.Bold, GraphicsUnit.Pixel);
        using var descFont = new Font("Segoe UI", 14, FontStyle.Regular, GraphicsUnit.Pixel);

        for (int i = 0; i < cards.Length; i++)
        {
            float y = 40 + i * 100;
            g.FillRectangle(new SolidBrush(Color.FromArgb(40, 44, 100)), 16, y - 12, 488, 84);
            g.FillEllipse(Brushes.Orange, 30, y + 6, 44, 44);
            g.DrawString(cards[i].Title, titleFont, Brushes.White, 90, y);
            g.DrawString(cards[i].Description, descFont, Brushes.LightGray, 90, y + 26);
        }

        return bmp;
    }

    [Fact]
    public async Task ScanAndMatch_FindsTheRecommendedCard()
    {
        Assert.True(TextScanner.IsAvailable, "Windows OCR engine isn't available on this machine.");

        using var panel = RenderPanel(
            ("Evenhanded Blessings", "Refund Flash Heal cooldown on new targets"),
            ("Power Word: Shield", "Divine Star grants Shields"),
            ("Lightwell", "Activate to summon a healing Lightwell"));

        var lines = await TextScanner.ScanOrThrowAsync(panel);
        Assert.NotEmpty(lines);

        var build = new HeroBuild
        {
            Name = "Test",
            Tiers = new[]
            {
                new TalentTier { Level = 1, OptionCount = 3, PickIndex = 1, PickName = "Power Word: Shield" },
                new TalentTier { Level = 4, OptionCount = 3, PickIndex = 0, PickName = "Divine Star" },
            },
        };

        var match = TalentNameMatcher.Find(lines, build).FirstOrDefault();

        Assert.NotNull(match);
        Assert.Equal(1, match!.Tier.Level);
        // The second card's title was drawn at y=140; the matched box must sit on it.
        Assert.InRange(match.NameBox.Y, 120, 160);
        // And the estimated card box should enclose that card's title and description.
        Assert.True(match.CardBox.Y <= 140 && match.CardBox.Bottom >= 166,
            $"card box {match.CardBox} doesn't enclose the card");
    }
}
