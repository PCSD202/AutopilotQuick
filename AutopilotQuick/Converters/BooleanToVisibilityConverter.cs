using System.Windows;
using System.Windows.Data;

namespace AutopilotQuick.Converters;

public class BooleanToVisibilityConverter : BooleanConverter<Visibility>
{
    public BooleanToVisibilityConverter() : base(Visibility.Visible, Visibility.Collapsed) {}
}