using System.Globalization;
namespace NexoRuta.Mobile.Presentacion;

public sealed class TieneTextoConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => !string.IsNullOrWhiteSpace(value as string);
    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
}
public sealed class ColorEstadoConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => (value as string) switch
    {
        "Entregado" or "Finalizada" => Color.FromArgb("#E7F5ED"),
        "Cancelado" or "EntregaSinValidacion" => Color.FromArgb("#FDEBED"),
        "IntentoFallido" or "EntregaPendienteValidacion" or "EnDevolucion" => Color.FromArgb("#FFF4DA"),
        _ => Color.FromArgb("#EAF2FF")
    };
    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
}
