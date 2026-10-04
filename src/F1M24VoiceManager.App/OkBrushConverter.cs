using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace F1M24VoiceManager.App;

/// <summary>true → green status dot, false → amber.</summary>
public sealed class OkBrushConverter : IValueConverter
{
    private static readonly Brush Ok = Freeze(new SolidColorBrush(Color.FromRgb(0x1E, 0x9E, 0x5A)));
    private static readonly Brush Warn = Freeze(new SolidColorBrush(Color.FromRgb(0xE0, 0x8A, 0x00)));

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is true ? Ok : Warn;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();

    private static Brush Freeze(Brush b)
    {
        b.Freeze();
        return b;
    }
}
