using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using NexoRuta.Mobile.Core.Rutas;

namespace NexoRuta.Mobile.Core.Mapas;

public sealed record ZonaMapa(string Proveedor, string Estilo, double LatitudMinima, double LatitudMaxima,
    double LongitudMinima, double LongitudMaxima, int ZoomMinimo, int ZoomMaximo)
{
    public string Huella => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(
        string.Create(CultureInfo.InvariantCulture,
            $"{Proveedor}|{Estilo}|{LatitudMinima:R}|{LatitudMaxima:R}|{LongitudMinima:R}|{LongitudMaxima:R}|{ZoomMinimo}|{ZoomMaximo}"))));

    public static ZonaMapa Para(HojaRuta hoja, string proveedor, string estilo)
    {
        ArgumentNullException.ThrowIfNull(hoja);
        return Para(hoja.Id, hoja.Paradas, proveedor, estilo);
    }

    public static ZonaMapa Para(Guid hojaId, IReadOnlyCollection<ParadaRuta> paradas, string proveedor, string estilo)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(proveedor); ArgumentException.ThrowIfNullOrWhiteSpace(estilo);
        ArgumentNullException.ThrowIfNull(paradas);
        if (hojaId == Guid.Empty || paradas.Count == 0
            || paradas.Any(p => p is null || !double.IsFinite(p.Latitud) || !double.IsFinite(p.Longitud)
                || p.Latitud is < -85 or > 85 || p.Longitud is < -180 or > 180))
            throw new InvalidOperationException("La hoja no tiene destinos representables en este mapa.");
        return new(proveedor, estilo,
            Math.Max(-85, paradas.Min(p => p.Latitud) - 0.015), Math.Min(85, paradas.Max(p => p.Latitud) + 0.015),
            Math.Max(-180, paradas.Min(p => p.Longitud) - 0.015), Math.Min(180, paradas.Max(p => p.Longitud) + 0.015), 6, 14);
    }
}
