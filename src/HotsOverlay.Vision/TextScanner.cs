using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices.WindowsRuntime;
using HotsOverlay.Core;
using Windows.Globalization;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;
using Windows.Storage.Streams;

namespace HotsOverlay.Vision;

/// <summary>
/// Runs the OCR engine built into Windows over a screen capture and returns every recognized
/// line with per-word bounding boxes (in the capture's pixel coordinates).
/// </summary>
public static class TextScanner
{
    private static readonly OcrEngine? Engine =
        OcrEngine.TryCreateFromLanguage(new Language("en-US")) ?? OcrEngine.TryCreateFromUserProfileLanguages();

    /// <summary>One OcrEngine instance can't run two recognitions at once ("Another RecognizeAsync
    /// operation is already running"), so every scan takes its turn.</summary>
    private static readonly SemaphoreSlim OneAtATime = new(1, 1);

    public static bool IsAvailable => Engine is not null;

    /// <summary>Returns the recognized lines, or an empty list if OCR is unavailable or fails —
    /// this runs against a live game every second, so a bad frame must never throw.</summary>
    public static async Task<IReadOnlyList<OcrLineBox>> ScanAsync(Bitmap capture)
    {
        try
        {
            return await ScanOrThrowAsync(capture);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("[HotsOverlay] OCR scan failed: " + ex);
            return Array.Empty<OcrLineBox>();
        }
    }

    /// <summary>Same as <see cref="ScanAsync"/> but lets failures propagate (used by tests/diagnostics).</summary>
    public static async Task<IReadOnlyList<OcrLineBox>> ScanOrThrowAsync(Bitmap capture)
    {
        if (Engine is null)
            return Array.Empty<OcrLineBox>();

        using var softwareBitmap = await ToSoftwareBitmapAsync(capture);

        Windows.Media.Ocr.OcrResult result;
        await OneAtATime.WaitAsync();
        try
        {
            result = await Engine.RecognizeAsync(softwareBitmap);
        }
        finally
        {
            OneAtATime.Release();
        }

        return result.Lines
            .Select(line => new OcrLineBox(line.Words
                .Select(w => new OcrWordBox(w.Text,
                    new BoxF(w.BoundingRect.X, w.BoundingRect.Y, w.BoundingRect.Width, w.BoundingRect.Height)))
                .ToList()))
            .Where(l => l.Words.Count > 0)
            .ToList();
    }

    private static async Task<SoftwareBitmap> ToSoftwareBitmapAsync(Bitmap capture)
    {
        // Encode to PNG in a plain MemoryStream and adapt it for WinRT. (Wrapping a WinRT stream
        // with AsStream() and disposing the wrapper would dispose the underlying stream too.)
        using var png = new MemoryStream();
        capture.Save(png, ImageFormat.Png);
        png.Position = 0;
        using IRandomAccessStream stream = png.AsRandomAccessStream();

        var decoder = await BitmapDecoder.CreateAsync(stream);
        SoftwareBitmap raw = await decoder.GetSoftwareBitmapAsync();

        if (raw.BitmapPixelFormat != BitmapPixelFormat.Bgra8 || raw.BitmapAlphaMode != BitmapAlphaMode.Premultiplied)
        {
            SoftwareBitmap converted = SoftwareBitmap.Convert(raw, BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied);
            raw.Dispose();
            return converted;
        }

        return raw;
    }
}
