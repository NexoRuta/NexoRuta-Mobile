using System.Net.Http.Json;
using NexoRuta.Mobile.Api.Notificaciones;
using NexoRuta.Mobile.Core.Jornadas;
using NexoRuta.Mobile.Core.Rutas;
namespace NexoRuta.Mobile.Api.Identidad;

public sealed partial class ClienteSesionMovil
{
    public async Task RegistrarInstalacionAsync(CuentaMovil cuenta, RegistroInstalacion registro, CancellationToken ct = default)
    {
        if (registro.InstalacionId == Guid.Empty || registro.Plataforma is not ("Android" or "iOS")
            || registro.FirebaseInstallationId is not { Length: >= 20 and <= 128 }
            || registro.FirebaseInstallationId.Any(c => !char.IsAsciiLetterOrDigit(c) && c is not ('-' or '_')))
            throw new ArgumentException("La instalación y el identificador push no son válidos.");
        using var respuesta = await EnviarAutorizadoAsync(() => new(HttpMethod.Put, new Uri(api, "api/mobile/instalaciones"))
        { Content = JsonContent.Create(registro, NotificacionesJson.Default.RegistroInstalacion) }, ct, cuenta);
    }
    public async Task<AvisoReparto[]> ObtenerAvisosAsync(CuentaMovil cuenta, CancellationToken ct = default)
    {
        var avisos = await ConsultarAsync("api/mobile/notificaciones", NotificacionesJson.Default.AvisoRepartoArray, ct, cuenta);
        if (avisos is null || avisos.Length > 100 || avisos.Any(a => a is null)) throw new InvalidDataException("La lista de avisos no es válida.");
        foreach (var a in avisos) a.Validar();
        return avisos;
    }
    public async Task<AvisoReparto> ObtenerAvisoAsync(CuentaMovil cuenta, Guid id, CancellationToken ct = default)
    {
        if (id == Guid.Empty) throw new ArgumentException("El aviso requiere un identificador.");
        var aviso = await ConsultarAsync($"api/mobile/notificaciones/{id}", NotificacionesJson.Default.AvisoReparto, ct, cuenta);
        aviso.Validar();
        if (aviso.Id != id) throw new InvalidDataException("El servidor devolvió un aviso diferente.");
        return aviso;
    }
    public async Task ConfirmarAvisoAsync(CuentaMovil cuenta, Guid id, string tipo, CancellationToken ct = default)
    {
        if (id == Guid.Empty || tipo is not ("Recibido" or "Abierto" or "Confirmado")) throw new ArgumentException("La confirmación no es válida.");
        using var respuesta = await EnviarAutorizadoAsync(() => new(HttpMethod.Post, new Uri(api, $"api/mobile/notificaciones/{id}/confirmaciones"))
        { Content = JsonContent.Create(new ConfirmarAviso(tipo), NotificacionesJson.Default.ConfirmarAviso) }, ct, cuenta);
    }
    public async Task<HojaRuta> ObtenerHojaAsync(CuentaMovil cuenta, Guid id, CancellationToken ct = default)
    {
        if (id == Guid.Empty) throw new ArgumentException("La ruta requiere un identificador.");
        var hoja = await ConsultarAsync($"api/mobile/rutas/{id}", HojaRutaJson.Default.HojaRuta, ct, cuenta);
        hoja.Validar(cuenta);
        if (hoja.Id != id) throw new InvalidDataException("El servidor devolvió una ruta diferente.");
        return hoja;
    }
}
