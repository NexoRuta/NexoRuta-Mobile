using NexoRuta.Mobile.Core.Rutas;

namespace NexoRuta.Mobile.Application;

public sealed record EvaluacionCargaSalida(int BultosTotales, int BultosConfirmados, int BultosPendientes,
    int ParadasAfectadas)
{
    public bool PermiteSalidaParcial { get; init; }
    public bool RequiereConfirmacionExplicita => BultosPendientes > 0;
    public bool PuedeIniciar(PoliticaReparto politica) =>
        !politica.BloquearDiscrepanciasCarga || (BultosPendientes == 0 || PermiteSalidaParcial);

    public static EvaluacionCargaSalida Crear(HojaRuta hoja, IReadOnlySet<Guid> bultosConfirmados)
    {
        ArgumentNullException.ThrowIfNull(hoja);
        ArgumentNullException.ThrowIfNull(bultosConfirmados);
        var paradas = hoja.Paradas.Where(p => p.Activa && !p.EsRetiro).ToArray();
        var bultos = paradas.SelectMany(p => p.Bultos.Where(b => b.Resultado == "Pendiente")).ToArray();
        var confirmados = bultos.Count(b => bultosConfirmados.Contains(b.Id));
        var pendientes = bultos.Length - confirmados;
        var afectadas = paradas.Count(p => p.Bultos.Any(b => !bultosConfirmados.Contains(b.Id)));
        return new(bultos.Length, confirmados, pendientes, afectadas) { PermiteSalidaParcial = pendientes > 0 && paradas.Count(p => p.Bultos.Any(b => b.Resultado == "Pendiente" && !bultosConfirmados.Contains(b.Id)) && (p.Reglas?.PermiteEntregaParcial != true || !p.Bultos.Any(b => b.Resultado == "Pendiente" && bultosConfirmados.Contains(b.Id)))) == 0 };
    }
}
