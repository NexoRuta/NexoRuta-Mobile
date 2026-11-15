using NexoRuta.Mobile.Application.Notificaciones;

namespace NexoRuta.Mobile.Notificaciones;

public sealed class AlmacenFidPushMaui : IAlmacenFidPush
{
    public Task<string?> LeerAsync() => SecureStorage.Default.GetAsync(ServicioRegistroPush.ClaveFid);
    public Task GuardarAsync(string fid) => SecureStorage.Default.SetAsync(ServicioRegistroPush.ClaveFid, fid);
}
