using System.Globalization;
using System.Windows.Data;
using CodeDock.Core.Models;

namespace CodeDock.App.Converters;

public sealed class DevelopmentEnvironmentNameConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is DevelopmentEnvironment environment
            ? environment.GetDisplayName()
            : "Visual Studio Code";

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
