using NexoRuta.Mobile.Core.Jornadas;

namespace NexoRuta.Mobile.Application;

public sealed record EstadoJornadaIndicador(string Fase, bool Iniciada, bool RepartoEnCurso,
    bool Finalizada, bool TieneOperacionesPendientes, bool TieneRechazos)
{
    public string EstadoSincronizacion => TieneOperacionesPendientes
        ? "Operaciones pendientes de sincronización"
        : TieneRechazos ? "Hay operaciones rechazadas; revisá el detalle" : "Sin operaciones pendientes";

    public static EstadoJornadaIndicador Crear(JornadaLocal? jornada,
        IReadOnlyDictionary<Guid, string> resultados, string? estadoServidor = null)
    {
        if (jornada is null) return new("Sin jornada iniciada", false, false, false, false, false);

        var operaciones = jornada.Operaciones;
        var pendientes = operaciones.Any(o => !resultados.ContainsKey(o.Id));
        var rechazadas = operaciones.Any(o => resultados.TryGetValue(o.Id, out var resultado) && resultado != "Aplicada");
        var inicio = operaciones.LastOrDefault(o => o.Tipo == TipoOperacionLocal.InicioJornada);
        var iniciada = inicio is not null && resultados.GetValueOrDefault(inicio.Id) == "Aplicada";
        var cierre = operaciones.LastOrDefault(o => o.Tipo == TipoOperacionLocal.FinJornada);
        var cierreResultado = cierre is null ? null : resultados.GetValueOrDefault(cierre.Id);
        var finalizada = cierreResultado == "Aplicada" || estadoServidor == "Finalizada";
        var salida = operaciones.LastOrDefault(o => o.Tipo == TipoOperacionLocal.ConfirmarCarga);
        var reparto = iniciada && (salida is not null && resultados.GetValueOrDefault(salida.Id) == "Aplicada"
            || estadoServidor == "EnCurso");

        var fase = cierre is not null && cierreResultado is null ? "Cierre pendiente de sincronización"
            : cierre is not null && cierreResultado != "Aplicada" ? "Cierre rechazado · revisar jornada"
            : finalizada ? "Jornada finalizada"
            : inicio is null ? "Sin jornada iniciada"
            : !iniciada && resultados.ContainsKey(inicio.Id) ? "Inicio rechazado · revisar jornada"
            : !iniciada ? "Inicio pendiente de sincronización"
            : reparto ? "Reparto en curso"
            : "Jornada iniciada · carga en depósito";

        return new(fase, iniciada, reparto, finalizada, pendientes, rechazadas);
    }
}
