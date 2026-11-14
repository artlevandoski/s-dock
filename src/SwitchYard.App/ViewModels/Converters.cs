using Avalonia;
using Avalonia.Data.Converters;
using Avalonia.Media;
using SwitchYard.App.Controls;

namespace SwitchYard.App.ViewModels;

public static class Converters
{
    /// <summary>Profiles set up for another generation are shown dimmed.</summary>
    public static readonly IValueConverter CompatibleOpacity =
        new FuncValueConverter<bool, double>(compatible => compatible ? 1.0 : 0.55);

    /// <summary>A soft gradient in the game's generation colour, shown where there's no background art.</summary>
    public static readonly IValueConverter GenerationBanner =
        new FuncValueConverter<string?, IBrush>(generation =>
        {
            var c = CoverView.BaseColor(generation);
            return new LinearGradientBrush
            {
                StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
                EndPoint = new RelativePoint(1, 1, RelativeUnit.Relative),
                GradientStops =
                {
                    new GradientStop(Color.FromArgb(255, c.R, c.G, c.B), 0),
                    new GradientStop(Color.FromArgb(255, (byte)(c.R * 0.35), (byte)(c.G * 0.35), (byte)(c.B * 0.35)), 1),
                },
            };
        });
}
