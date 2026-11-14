using Avalonia.Data.Converters;

namespace SwitchYard.App.ViewModels;

public static class Converters
{
    /// <summary>Profiles set up for another generation are shown dimmed.</summary>
    public static readonly IValueConverter CompatibleOpacity =
        new FuncValueConverter<bool, double>(compatible => compatible ? 1.0 : 0.55);
}
