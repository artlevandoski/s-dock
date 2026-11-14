using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using SDock.App.Controls;

namespace SDock.App.ViewModels;

// The backdrop behind everything: the game's artwork, a gradient in the game's own colour, or plain.
public sealed partial class MainViewModel
{
    public const string BackgroundKey = "background_style";
    public const string BackgroundArt = "art", BackgroundColor = "color", BackgroundPlain = "plain";

    public string BackgroundStyle
    {
        get => _library.GetSetting(BackgroundKey) is { } s && (s == BackgroundColor || s == BackgroundPlain) ? s : BackgroundArt;
        set
        {
            if (value == BackgroundStyle) return;
            _library.SetSetting(BackgroundKey, value);
            OnPropertyChanged();
            OnPropertyChanged(nameof(BackgroundIsArt));
            OnPropertyChanged(nameof(BackgroundIsColor));
            OnPropertyChanged(nameof(BackgroundIsPlain));
            OnPropertyChanged(nameof(BackdropBrush));
        }
    }

    /// <summary>
    /// Settings → Background. Avalonia's check and radio menu items only draw the tick, so each item runs this
    /// command and the tick follows the setting.
    /// </summary>
    [CommunityToolkit.Mvvm.Input.RelayCommand]
    private void SetBackground(string? style) =>
        BackgroundStyle = style is BackgroundColor or BackgroundPlain ? style : BackgroundArt;

    // For the three radio items in Settings → Background.
    public bool BackgroundIsArt { get => BackgroundStyle == BackgroundArt; set { if (value) BackgroundStyle = BackgroundArt; } }
    public bool BackgroundIsColor { get => BackgroundStyle == BackgroundColor; set { if (value) BackgroundStyle = BackgroundColor; } }
    public bool BackgroundIsPlain { get => BackgroundStyle == BackgroundPlain; set { if (value) BackgroundStyle = BackgroundPlain; } }

    /// <summary>The selected game's colour: picked from its cover, or its generation's colour until the cover is ready.</summary>
    public Color SelectedColor =>
        SelectedItem is { } item ? (item.Cover is { } cover && item.CoverPath is { } path ? CoverColors.Get(path, cover) : null)
                                   ?? CoverView.BaseColor(item.Generation)
                                 : Color.Parse("#222439");

    /// <summary>
    /// The colour layer under the backdrop: the generation tint under artwork, the game's colour as a gradient,
    /// or nothing (plain).
    /// </summary>
    public IBrush? BackdropBrush
    {
        get
        {
            if (SelectedItem is null || BackgroundStyle == BackgroundPlain) return null;
            if (BackgroundStyle == BackgroundArt)
                return Converters.GenerationBanner.Convert(SelectedItem.Generation, typeof(IBrush), null,
                    System.Globalization.CultureInfo.InvariantCulture) is IBrush b ? WithOpacity(b, 0.22) : null;
            var c = SelectedColor;
            return new LinearGradientBrush
            {
                StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
                EndPoint = new RelativePoint(0.85, 1, RelativeUnit.Relative),
                GradientStops =
                {
                    new GradientStop(c, 0),
                    new GradientStop(Scale(c, 0.45), 0.55),
                    new GradientStop(Color.Parse("#10111E"), 1),
                },
            };
        }
    }

    private static IBrush WithOpacity(IBrush brush, double opacity)
    {
        if (brush is LinearGradientBrush g) g.Opacity = opacity;
        return brush;
    }

    private static Color Scale(Color c, double f) => Color.FromRgb((byte)(c.R * f), (byte)(c.G * f), (byte)(c.B * f));

    private EditionItem? _coverWatched;

    /// <summary>Re-colour when the selected cover finishes decoding.</summary>
    private void WatchSelectedCover(EditionItem? item)
    {
        if (_coverWatched is not null) _coverWatched.PropertyChanged -= SelectedCoverChanged;
        _coverWatched = item;
        if (item is not null) item.PropertyChanged += SelectedCoverChanged;
    }

    private void SelectedCoverChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(EditionItem.Cover) || sender != SelectedItem) return;
        OnPropertyChanged(nameof(SelectedCover));
        OnPropertyChanged(nameof(SelectedColor));
        OnPropertyChanged(nameof(BackdropBrush));
    }
}

/// <summary>Picks a pleasant main colour from a cover (vivid pixels count most), remembered per file.</summary>
public static class CoverColors
{
    private static readonly Dictionary<string, Color> Known = new(StringComparer.OrdinalIgnoreCase);

    public static Color? Get(string path, Bitmap bitmap)
    {
        if (Known.TryGetValue(path, out var known)) return known;
        var color = Pick(bitmap);
        if (color is { } c)
        {
            if (Known.Count > 4096) Known.Clear();
            Known[path] = c;
        }
        return color;
    }

    public static Color? Pick(Bitmap bitmap)
    {
        var size = bitmap.PixelSize;
        if (size.Width <= 0 || size.Height <= 0) return null;
        var stride = size.Width * 4;
        var bytes = new byte[stride * size.Height];
        var handle = GCHandle.Alloc(bytes, GCHandleType.Pinned);
        try
        {
            bitmap.CopyPixels(new PixelRect(size), handle.AddrOfPinnedObject(), bytes.Length, stride);
        }
        catch (Exception ex) when (ex is NotSupportedException or InvalidOperationException or ArgumentException)
        {
            return null;
        }
        finally
        {
            handle.Free();
        }
        var bgra = bitmap.Format is not { } f || f == Avalonia.Platform.PixelFormat.Bgra8888;
        double r = 0, g = 0, b = 0, total = 0;
        for (var y = 0; y < size.Height; y += 3)
        for (var x = 0; x < size.Width; x += 3)
        {
            var i = y * stride + x * 4;
            double pr = bytes[i + (bgra ? 2 : 0)], pg = bytes[i + 1], pb = bytes[i + (bgra ? 0 : 2)];
            var max = Math.Max(pr, Math.Max(pg, pb)) / 255;
            var min = Math.Min(pr, Math.Min(pg, pb)) / 255;
            var sat = max <= 0 ? 0 : (max - min) / max;
            var weight = sat * sat * max + 0.02; // vivid, bright pixels lead; greys barely count
            r += pr * weight; g += pg * weight; b += pb * weight; total += weight;
        }
        if (total <= 0) return null;
        var avg = Color.FromRgb((byte)(r / total), (byte)(g / total), (byte)(b / total));
        // Keep it rich but not glaring behind white text.
        var hsv = avg.ToHsv();
        return HsvColor.ToRgb(hsv.H, Math.Min(0.75, hsv.S * 1.15), Math.Clamp(hsv.V, 0.38, 0.62));
    }
}
