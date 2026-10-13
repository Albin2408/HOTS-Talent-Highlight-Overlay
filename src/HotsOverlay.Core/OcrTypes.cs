namespace HotsOverlay.Core;

/// <summary>An axis-aligned box in pixel coordinates of whatever image it was scanned from.</summary>
public readonly record struct BoxF(double X, double Y, double Width, double Height)
{
    public double Right => X + Width;
    public double Bottom => Y + Height;

    public static BoxF Union(BoxF a, BoxF b)
    {
        double x = Math.Min(a.X, b.X);
        double y = Math.Min(a.Y, b.Y);
        return new BoxF(x, y, Math.Max(a.Right, b.Right) - x, Math.Max(a.Bottom, b.Bottom) - y);
    }
}

public sealed record OcrWordBox(string Text, BoxF Box);

/// <summary>One line of recognized text, as reported by the OCR engine.</summary>
public sealed record OcrLineBox(IReadOnlyList<OcrWordBox> Words)
{
    public string Text => string.Join(' ', Words.Select(w => w.Text));

    public BoxF Box => Words.Count == 0
        ? default
        : Words.Skip(1).Aggregate(Words[0].Box, (acc, w) => BoxF.Union(acc, w.Box));
}
