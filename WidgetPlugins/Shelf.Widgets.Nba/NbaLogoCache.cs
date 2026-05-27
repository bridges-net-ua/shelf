using System;
using System.Collections.Concurrent;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Shelf.Widgets.Nba;

// Two-level cache for team logo PNGs:
//   1) In-memory BitmapImage per abbreviation - resolved synchronously after first hit.
//   2) On-disk PNG at %LOCALAPPDATA%\Shelf\nba-logos\<abbr>.png so we don't hit
//      ESPN every time the host restarts.
// One download per (abbr, url) combo; subsequent calls hit memory then disk.
//
// Grayscale variants live only in memory - they're cheap to recompute from the
// already-cached color source, so persisting them on disk would just bloat the
// cache directory.
public static class NbaLogoCache
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(10) };
    private static readonly ConcurrentDictionary<string, BitmapImage> Memory = new(StringComparer.OrdinalIgnoreCase);
    private static readonly ConcurrentDictionary<string, BitmapSource> GrayMemory = new(StringComparer.OrdinalIgnoreCase);
    private static readonly ConcurrentDictionary<string, Task<BitmapImage?>> InFlight = new(StringComparer.OrdinalIgnoreCase);

    private static readonly string CacheDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Shelf",
        "nba-logos");

    // Returns the cached color BitmapImage immediately if it's in memory; otherwise null.
    public static BitmapImage? TryGet(string? abbreviation)
    {
        if (string.IsNullOrWhiteSpace(abbreviation)) return null;
        return Memory.TryGetValue(abbreviation, out var bmp) ? bmp : null;
    }

    // Returns the cached grayscale variant if it's in memory; otherwise null.
    public static BitmapSource? TryGetGray(string? abbreviation)
    {
        if (string.IsNullOrWhiteSpace(abbreviation)) return null;
        return GrayMemory.TryGetValue(abbreviation, out var bmp) ? bmp : null;
    }

    // Returns the logo, loading from disk if available, downloading otherwise.
    // Safe to call repeatedly for the same abbreviation - in-flight downloads
    // are coalesced.
    public static Task<BitmapImage?> GetAsync(string? abbreviation, string? url)
    {
        if (string.IsNullOrWhiteSpace(abbreviation)) return Task.FromResult<BitmapImage?>(null);

        if (Memory.TryGetValue(abbreviation, out var cached))
            return Task.FromResult<BitmapImage?>(cached);

        return InFlight.GetOrAdd(abbreviation, key => LoadCoreAsync(key, url));
    }

    // Returns the grayscale variant of the logo. First loads the color logo
    // (re-using GetAsync's path - disk → network), then luminance-converts it
    // while preserving alpha so transparent PNG backgrounds stay transparent.
    public static async Task<BitmapSource?> GetGrayAsync(string? abbreviation, string? url)
    {
        if (string.IsNullOrWhiteSpace(abbreviation)) return null;

        if (GrayMemory.TryGetValue(abbreviation, out var gray)) return gray;

        var color = await GetAsync(abbreviation, url);
        if (color == null) return null;

        var converted = MakeGrayscale(color);
        if (converted != null) GrayMemory[abbreviation] = converted;
        return converted;
    }

    private static BitmapSource? MakeGrayscale(BitmapSource src)
    {
        try
        {
            // Force Bgra32 so we can read 4 bytes per pixel deterministically.
            BitmapSource bgra = src.Format == System.Windows.Media.PixelFormats.Bgra32
                ? src
                : new FormatConvertedBitmap(src, System.Windows.Media.PixelFormats.Bgra32, null, 0);

            int width = bgra.PixelWidth;
            int height = bgra.PixelHeight;
            int stride = width * 4;
            var pixels = new byte[height * stride];
            bgra.CopyPixels(pixels, stride, 0);

            for (int i = 0; i < pixels.Length; i += 4)
            {
                byte b = pixels[i];
                byte g = pixels[i + 1];
                byte r = pixels[i + 2];
                // alpha (pixels[i + 3]) preserved.
                byte y = (byte)Math.Clamp((int)Math.Round(0.299 * r + 0.587 * g + 0.114 * b), 0, 255);
                pixels[i] = y;
                pixels[i + 1] = y;
                pixels[i + 2] = y;
            }

            var wb = new WriteableBitmap(width, height, bgra.DpiX, bgra.DpiY, System.Windows.Media.PixelFormats.Bgra32, null);
            wb.WritePixels(new System.Windows.Int32Rect(0, 0, width, height), pixels, stride, 0);
            wb.Freeze();
            return wb;
        }
        catch
        {
            return null;
        }
    }

    private static async Task<BitmapImage?> LoadCoreAsync(string abbreviation, string? url)
    {
        try
        {
            Directory.CreateDirectory(CacheDir);
            string path = Path.Combine(CacheDir, abbreviation + ".png");

            byte[]? bytes = null;
            if (File.Exists(path))
            {
                try { bytes = await File.ReadAllBytesAsync(path); }
                catch { bytes = null; }
            }

            if (bytes == null && !string.IsNullOrWhiteSpace(url))
            {
                try
                {
                    bytes = await Http.GetByteArrayAsync(url);
                    try { await File.WriteAllBytesAsync(path, bytes); } catch { /* best-effort */ }
                }
                catch
                {
                    bytes = null;
                }
            }

            if (bytes == null || bytes.Length == 0) return null;

            var bmp = new BitmapImage();
            using (var ms = new MemoryStream(bytes))
            {
                bmp.BeginInit();
                bmp.CacheOption = BitmapCacheOption.OnLoad;
                bmp.StreamSource = ms;
                bmp.EndInit();
                bmp.Freeze();
            }
            Memory[abbreviation] = bmp;
            return bmp;
        }
        catch
        {
            return null;
        }
        finally
        {
            InFlight.TryRemove(abbreviation, out _);
        }
    }
}
