namespace NexoRuta.Mobile.Api.Reparto;

public sealed record EstadoSincronizacionMovilRequest(Guid InstallationId, long Sequence, int PendingOperations);
