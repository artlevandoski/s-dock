using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using SwitchYard.Core;

namespace SwitchYard.App.Controls;

/// <summary>Generated placeholder cover (no third-party artwork is bundled).</summary>
public sealed class CoverView : Control
{
    public static readonly StyledProperty<string?> TitleProperty =
        AvaloniaProperty.Register<CoverView, string?>(nameof(Title));

    public static readonly StyledProperty<string?> GenerationProperty =
        AvaloniaProperty.Register<CoverView, string?>(nameof(Generation));

    public static readonly StyledProperty<bool> IsMissingProperty =
        AvaloniaProperty.Register<CoverView, bool>(nameof(IsMissing));

    /// <summary>Cover artwork; when set it replaces the generated placeholder.</summary>
    public static readonly StyledProperty<IImage?> ImageProperty =
        AvaloniaProperty.Register<CoverView, IImage?>(nameof(Image));

    static CoverView()
    {
        AffectsRender<CoverView>(TitleProperty, GenerationProperty, IsMissingProperty, ImageProperty);
    }

    public IImage? Image { get => GetValue(ImageProperty); set => SetValue(ImageProperty, value); }
    public string? Title { get => GetValue(TitleProperty); set => SetValue(TitleProperty, value); }
    public string? Generation { get => GetValue(GenerationProperty); set => SetValue(GenerationProperty, value); }
    public bool IsMissing { get => GetValue(IsMissingProperty); set => SetValue(IsMissingProperty, value); }

    public static Color BaseColor(string? generation) => generation switch
    {
        Generations.Switch1 => Color.Parse("#A3343C"),
        Generations.Switch2 => Color.Parse("#2F58A3"),
        _ => Color.Parse("#56606D"),
    };

    private static Color Scale(Color c, double f) => Color.FromRgb(
        (byte)Math.Clamp(c.R * f, 0, 255), (byte)Math.Clamp(c.G * f, 0, 255), (byte)Math.Clamp(c.B * f, 0, 255));

    private static readonly Typeface Bold = new(FontFamily.Default, FontStyle.Normal, FontWeight.Bold);

    private static FormattedText Text(string text, double size, IBrush brush, double maxWidth, double maxHeight = double.PositiveInfinity) =>
        new(text, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, Bold, size, brush)
        {
            MaxTextWidth = Math.Max(1, maxWidth),
            MaxTextHeight = maxHeight,
            TextAlignment = TextAlignment.Center,
            Trimming = TextTrimming.CharacterEllipsis,
        };

    public override void Render(DrawingContext ctx)
    {
        double w = Bounds.Width, h = Bounds.Height;
        if (w < 4 || h < 4) return;
        var rect = new Rect(0, 0, w, h);
        var shape = new RoundedRect(rect, w * 0.06);
        var baseColor = IsMissing ? Color.Parse("#3A3D42") : BaseColor(Generation);

        var fill = new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
            EndPoint = new RelativePoint(0, 1, RelativeUnit.Relative),
            GradientStops =
            {
                new GradientStop(Scale(baseColor, 1.35), 0),
                new GradientStop(Scale(baseColor, 0.6), 1),
            },
        };

        using (ctx.PushClip(shape))
        {
            var image = Image;
            if (image is not null)
            {
                // Artwork fills the cover, cropped to the cover's shape (uniform-to-fill).
                var size = image.Size;
                var scale = Math.Max(w / size.Width, h / size.Height);
                var src = new Rect((size.Width - w / scale) / 2, (size.Height - h / scale) / 2, w / scale, h / scale);
                ctx.DrawImage(image, src, rect);
                if (IsMissing)
                    ctx.DrawRectangle(new SolidColorBrush(Color.FromArgb(150, 20, 20, 24)), null, rect);
            }
            else
            {
                ctx.DrawRectangle(fill, null, rect);
            }

            // Generation band: slimmer and translucent over artwork.
            var bandH = h * (image is null ? 0.13 : 0.10);
            var band = new Rect(0, h - bandH, w, bandH);
            ctx.DrawRectangle(new SolidColorBrush(Color.FromArgb(image is null ? (byte)110 : (byte)160, 0, 0, 0)), null, band);
            var label = Text(Generations.Label(Generation ?? Generations.Unclassified).ToUpperInvariant(),
                Math.Max(9, h * (image is null ? 0.05 : 0.042)), new SolidColorBrush(Color.FromArgb(220, 255, 255, 255)), w);
            ctx.DrawText(label, new Point(0, band.Y + (bandH - label.Height) / 2));

            if (image is null)
            {
                var area = new Rect(w * 0.08, h * 0.08, w * 0.84, h - bandH - h * 0.12);
                var title = Text(Title ?? "", Math.Max(10, h * 0.075), Brushes.White, area.Width, area.Height);
                ctx.DrawText(title, new Point(area.X, area.Y + Math.Max(0, (area.Height - title.Height) / 2)));
            }

            if (IsMissing)
            {
                var badgeH = h * 0.11;
                var badge = new Rect(w * 0.12, h * 0.06, w * 0.76, badgeH);
                ctx.DrawRectangle(new SolidColorBrush(Color.Parse("#D9822B")), null, new RoundedRect(badge, badgeH / 2));
                var txt = Text("FILE MISSING", Math.Max(9, badgeH * 0.45), Brushes.Black, badge.Width);
                ctx.DrawText(txt, new Point(badge.X, badge.Y + (badgeH - txt.Height) / 2));
            }
        }
        ctx.DrawRectangle(null, new Pen(new SolidColorBrush(Color.FromArgb(40, 255, 255, 255)), 1), new RoundedRect(rect.Deflate(0.5), w * 0.06));
    }
}
