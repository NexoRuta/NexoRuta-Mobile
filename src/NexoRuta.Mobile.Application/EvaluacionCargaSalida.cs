using NexoRuta.Mobile.Core.Rutas;

namespace NexoRuta.Mobile.Application;

public sealed record EvaluacionCargaSalida(int BultosTotales, int BultosConfirmados, int BultosPendientes,
    int ParadasAfectadas)
{
    public bool RequiereConfirmacionExplicita => BultosPendientes > 0;
    public bool PuedeIniciar(PoliticaReparto politica) =>
        !politica.BloquearDiscrepanciasCarga || BultosPendientes == 0;

    public static EvaluacionCargaSalida Crear(HojaRuta hoja, IReadOnlySet<Guid> bultosConfirmados)
    {
        ArgumentNullException.ThrowIfNull(hoja);
        ArgumentNullException.ThrowIfNull(bultosConfirmados);
        var paradas = hoja.Paradas.Where(p => p.Activa).ToArray();
        var bultos = paradas.SelectMany(p => p.Bultos).ToArray();
        var confirmados = bultos.Count(b => bultosConfirmados.Contains(b.Id));
        var pendientes = bultos.Length - confirmados;
        var afectadas = paradas.Count(p => p.Bultos.Any(b => !bultosConfirmados.Contains(b.Id)));
        return new(bultos.Length, confirmados, pendientes, afectadas);
    }
}
