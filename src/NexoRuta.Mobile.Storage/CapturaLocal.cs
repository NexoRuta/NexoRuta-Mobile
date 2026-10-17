namespace NexoRuta.Mobile.Storage;
public sealed record CapturaLocal(Guid Id, Guid JornadaId, Guid EnvioId, string Tipo, bool Lista, Guid? OperacionId);
