namespace NexoRuta.Mobile.Core.Identidad;

public interface IVerificacionLocal
{
    Task<bool> VerificarAsync(string motivo, CancellationToken ct = default);
}
