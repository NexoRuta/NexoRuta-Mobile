using NexoRuta.Mobile.Core.Mapas;

namespace NexoRuta.Mobile.Application.Mapas;

public interface IProveedorMapaOffline
{
    string Proveedor { get; }
    string Estilo { get; }
    bool Disponible { get; }
    Task<RecursosMapa> ConsultarAsync(string regionId, CancellationToken ct);
    Task DescargarAsync(string regionId, ZonaMapa zona, IProgress<ProgresoMapa> progreso, CancellationToken ct);
    Task EliminarAsync(string regionId, CancellationToken ct);
}
public sealed record RecursosMapa(bool Completo, long Bytes, string? Error = null);
public sealed record ProgresoMapa(double Fraccion, long Bytes);
public sealed record PreparacionMapa(bool Preparada, long Bytes, string Mensaje);
