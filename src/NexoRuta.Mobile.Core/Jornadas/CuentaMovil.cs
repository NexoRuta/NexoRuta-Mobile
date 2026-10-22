namespace NexoRuta.Mobile.Core.Jornadas;

public sealed record CuentaMovil
{
    public Guid UsuarioId { get; }
    public Guid AccesoId { get; }
    public Guid OperadorId { get; }

    public CuentaMovil(Guid usuarioId, Guid accesoId, Guid operadorId)
    {
        if (usuarioId == Guid.Empty || accesoId == Guid.Empty || operadorId == Guid.Empty)
            throw new ArgumentException("La cuenta requiere usuario, acceso y operador.");

        UsuarioId = usuarioId;
        AccesoId = accesoId;
        OperadorId = operadorId;
    }
}
