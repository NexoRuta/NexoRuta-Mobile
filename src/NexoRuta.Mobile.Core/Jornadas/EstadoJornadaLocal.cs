namespace NexoRuta.Mobile.Core.Jornadas;

public sealed record EstadoJornadaLocal(
    int Version,
    Guid JornadaId,
    Guid RutaId,
    CuentaMovil Cuenta,
    IReadOnlyList<OperacionLocal> Operaciones,
    IReadOnlyList<Guid> Confirmadas);
