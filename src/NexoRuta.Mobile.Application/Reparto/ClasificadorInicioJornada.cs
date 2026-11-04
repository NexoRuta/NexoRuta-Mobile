using NexoRuta.Mobile.Api.Reparto;
using NexoRuta.Mobile.Core.Jornadas;

namespace NexoRuta.Mobile.Application.Reparto;

public enum EstadoInicioJornadaLocal
{
    Pendiente,
    Aplicado,
    Rechazado,
    RequiereRevision
}

public static class ClasificadorInicioJornada
{
    public static bool BloqueaOtraJornada(JornadaLocal jornada, EstadoInicioJornadaLocal estado, bool capturaPendiente)
        => jornada.TienePendientes || capturaPendiente
            || !jornada.EstaCerrada && estado != EstadoInicioJornadaLocal.Rechazado;

    public static EstadoInicioJornadaLocal Clasificar(JornadaLocal jornada, IEnumerable<ConfirmacionOperacion> acuses)
    {
        ArgumentNullException.ThrowIfNull(jornada);
        ArgumentNullException.ThrowIfNull(acuses);

        var estado = jornada.ExportarEstado();
        var inicios = jornada.Operaciones.Where(x => x.Tipo == TipoOperacionLocal.InicioJornada).ToArray();
        if (inicios.Length == 0 || jornada.Operaciones[0].Tipo != TipoOperacionLocal.InicioJornada)
            return EstadoInicioJornadaLocal.RequiereRevision;

        var recibidos = acuses.ToArray();
        var ultimo = inicios[^1];
        var coincidencias = recibidos.Where(x => x.Id == ultimo.Id).ToArray();
        if (coincidencias.Length == 0)
            return estado.Confirmadas.Contains(ultimo.Id)
                ? EstadoInicioJornadaLocal.RequiereRevision
                : EstadoInicioJornadaLocal.Pendiente;
        if (coincidencias.Length != 1 || !estado.Confirmadas.Contains(ultimo.Id)
            || coincidencias[0].Secuencia != ultimo.Secuencia)
            return EstadoInicioJornadaLocal.RequiereRevision;

        var acuse = coincidencias[0];
        if (acuse.Resultado == "Aplicada") return EstadoInicioJornadaLocal.Aplicado;
        if (acuse.Resultado != "Conflicto" || jornada.Operaciones.Any(x => x.Tipo != TipoOperacionLocal.InicioJornada))
            return EstadoInicioJornadaLocal.RequiereRevision;

        foreach (var anterior in inicios[..^1])
        {
            var previos = recibidos.Where(x => x.Id == anterior.Id).ToArray();
            if (!estado.Confirmadas.Contains(anterior.Id) || previos.Length != 1
                || previos[0].Secuencia != anterior.Secuencia || previos[0].Resultado != "Conflicto")
                return EstadoInicioJornadaLocal.RequiereRevision;
        }

        return EstadoInicioJornadaLocal.Rechazado;
    }
}
