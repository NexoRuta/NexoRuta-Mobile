using NexoRuta.Mobile.Api.Reparto;
using NexoRuta.Mobile.Core.Rutas;
namespace NexoRuta.Mobile.Application;

// Solo presentación: nunca sustituye las versiones del sobre de sincronización.
public sealed record VistaParada(string Estado, bool Activa, long Version)
{
    public static VistaParada Obtener(ParadaRuta original, HojaRuta? remota,
        IEnumerable<ConfirmacionOperacion> confirmaciones)
    {
        var vista = new VistaParada(original.Estado, original.Activa, original.EnvioVersion);
        foreach (var ack in confirmaciones.Where(a => a.Resultado == "Aplicada").OrderBy(a => a.Secuencia))
            if (ack.VersionesEnvio.TryGetValue(original.EnvioId, out var version) && version >= vista.Version)
                vista = vista with { Version = version, Estado = ack.EstadosEnvio.GetValueOrDefault(original.EnvioId, vista.Estado) };
        var actual = remota?.Paradas.SingleOrDefault(p => p.EnvioId == original.EnvioId);
        if (actual is not null && actual.EnvioVersion >= vista.Version)
            vista = new(actual.Estado, actual.Activa && remota!.Estado != "Finalizada", actual.EnvioVersion);
        return vista;
    }

    public static string DescribirSuspension(ParadaRuta original, HojaRuta? remota, IReadOnlySet<Guid> cargadosLocales)
    {
        var bultos = (remota?.Paradas.SingleOrDefault(p => p.EnvioId == original.EnvioId) ?? original).Bultos;
        if (bultos.Any(b => cargadosLocales.Contains(b.Id) || b.Custodia == "Repartidor"))
            return "Fuera de esta ruta · devolver en rendición";
        if (bultos.Any(b => b.Custodia == "PendienteDeposito"))
            return "Fuera de esta ruta · recepción en depósito pendiente";
        return bultos.Length > 0 && bultos.All(b => b.Custodia == "Deposito")
            ? "Fuera de esta ruta · en depósito" : "Fuera de esta ruta · revisar custodia";
    }
}
