using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using NexoRuta.Mobile.Api.Identidad;
using NexoRuta.Mobile.Api.Notificaciones;
using NexoRuta.Mobile.Core.Jornadas;
using NexoRuta.Mobile.Core.Notificaciones;
using NexoRuta.Mobile.Core.Rutas;
using NexoRuta.Mobile.Storage;
namespace NexoRuta.Mobile.Application;

public sealed class ServicioAvisosReparto(ClienteSesionMovil cliente, RepositorioJornadasSqlite datos) : IDisposable
{
    private readonly SemaphoreSlim acceso = new(1, 1);
    private const string Pendientes = "avisos-push.v1";
    public event Action<bool>? AvisoGuardado;
    public async Task<Guid?> ObtenerInstalacionLocalAsync(CuentaMovil cuenta, CancellationToken ct = default)
    {
        await acceso.WaitAsync(ct);
        try
        {
            var bytes = await datos.LeerMetadatoAsync(cuenta, "instalacion-push.v1", ct);
            if (bytes is null) return null;
            if (!Guid.TryParseExact(Encoding.UTF8.GetString(bytes), "D", out var id) || id == Guid.Empty)
                throw new InvalidDataException("La identidad local de la instalación no es válida.");
            return id;
        }
        finally { acceso.Release(); }
    }

    public async Task<bool> GuardarReferenciaAsync(IReadOnlyDictionary<string, string> payload, bool abierto, CancellationToken ct = default)
    {
        if (!AvisoPush.IntentarLeer(payload, out var aviso)) return false;
        var cuenta = await cliente.LeerCuentaGuardadaAsync(ct);
        if (cuenta is null || !aviso!.CorrespondeA(cuenta)) return false;
        await acceso.WaitAsync(ct);
        try
        {
            var pendientes = (await LeerPendientes(cuenta, ct)).ToList();
            var indice = pendientes.FindIndex(p => p.Aviso.Id == aviso.Id);
            if (indice < 0) pendientes.Add(new(aviso, abierto, false));
            else pendientes[indice] = pendientes[indice] with { Abierto = pendientes[indice].Abierto || abierto };
            await datos.GuardarMetadatoAsync(cuenta, Pendientes,
                JsonSerializer.SerializeToUtf8Bytes(pendientes.TakeLast(100).ToArray(), AvisosLocalesJson.Default.ReferenciaAvisoArray), ct);
        }
        finally { acceso.Release(); }
        AvisoGuardado?.Invoke(abierto); return true;
    }

    public async Task<Guid?> ObtenerAperturaPendienteAsync(CuentaMovil cuenta, CancellationToken ct = default)
    {
        await acceso.WaitAsync(ct);
        try { return (await LeerPendientes(cuenta, ct)).LastOrDefault(p => p.Abierto)?.Aviso.Id; }
        finally { acceso.Release(); }
    }

    public async Task RegistrarInstalacionAsync(CuentaMovil cuenta, string plataforma, string fid, bool permitida, CancellationToken ct = default)
    {
        await acceso.WaitAsync(ct);
        try
        {
            var bytes = await datos.LeerMetadatoAsync(cuenta, "instalacion-push.v1", ct);
            Guid id;
            if (bytes is null)
            {
                id = Guid.CreateVersion7();
                await datos.GuardarMetadatoAsync(cuenta, "instalacion-push.v1", Encoding.UTF8.GetBytes(id.ToString()), ct);
            }
            else if (!Guid.TryParseExact(Encoding.UTF8.GetString(bytes), "D", out id) || id == Guid.Empty)
                throw new InvalidDataException("La identidad local de la instalación no es válida.");
            await cliente.RegistrarInstalacionAsync(cuenta, new(id, plataforma, fid, permitida), ct);
        }
        finally { acceso.Release(); }
    }

    public Task<AvisoReparto[]> ListarAsync(CuentaMovil cuenta, CancellationToken ct = default) => cliente.ObtenerAvisosAsync(cuenta, ct);

    public async Task RegistrarRecepcionesAsync(CuentaMovil cuenta, CancellationToken ct = default)
    {
        ReferenciaAviso[] referencias;
        await acceso.WaitAsync(ct);
        try { referencias = await LeerPendientes(cuenta, ct); }
        finally { acceso.Release(); }
        foreach (var referencia in referencias.Where(r => !r.Recibido))
        {
            try { await cliente.ConfirmarAvisoAsync(cuenta, referencia.Aviso.Id, "Recibido", ct); }
            catch (SesionMovilException ex) when (ex.Error == ErrorSesionMovil.RecursoNoDisponible)
            { await QuitarReferenciaAsync(cuenta, referencia.Aviso.Id, ct); continue; }
            await acceso.WaitAsync(ct);
            try
            {
                var actuales = await LeerPendientes(cuenta, ct);
                var indice = Array.FindIndex(actuales, r => r.Aviso.Id == referencia.Aviso.Id);
                if (indice < 0) continue;
                actuales[indice] = actuales[indice] with { Recibido = true };
                await datos.GuardarMetadatoAsync(cuenta, Pendientes,
                    JsonSerializer.SerializeToUtf8Bytes(actuales, AvisosLocalesJson.Default.ReferenciaAvisoArray), ct);
            }
            finally { acceso.Release(); }
        }
    }

    public async Task<(AvisoReparto Aviso, HojaRuta? Hoja)> AbrirAsync(CuentaMovil cuenta, Guid id, CancellationToken ct = default)
    {
        AvisoReparto aviso;
        try { aviso = await cliente.ObtenerAvisoAsync(cuenta, id, ct); }
        catch (SesionMovilException ex) when (ex.Error == ErrorSesionMovil.RecursoNoDisponible)
        { await QuitarReferenciaAsync(cuenta, id, ct); throw; }
        HojaRuta? hoja = null;
        if (aviso.Vigente)
        {
            hoja = await ActualizarRutaAsync(cuenta, aviso.RutaId, ct);
            var parada = hoja.Paradas.SingleOrDefault(p => p.EnvioId == aviso.EnvioId);
            if (hoja.Estado == "Finalizada" || parada is null || parada.EnvioVersion != aviso.VersionEnvio)
                aviso = aviso with { Vigente = false };
        }
        await cliente.ConfirmarAvisoAsync(cuenta, id, "Recibido", ct);
        await cliente.ConfirmarAvisoAsync(cuenta, id, "Abierto", ct);
        await QuitarReferenciaAsync(cuenta, id, ct);
        return (aviso, hoja);
    }

    public async Task ConfirmarAsync(CuentaMovil cuenta, Guid id, CancellationToken ct = default)
    {
        await cliente.ConfirmarAvisoAsync(cuenta, id, "Confirmado", ct);
        await QuitarReferenciaAsync(cuenta, id, ct);
    }

    public async Task<HojaRuta> ActualizarRutaAsync(CuentaMovil cuenta, Guid id, CancellationToken ct = default)
    {
        var hoja = await cliente.ObtenerHojaAsync(cuenta, id, ct);
        await datos.GuardarVistaRemotaAsync(cuenta, id, hoja.Version, SerializadorHojaRuta.Serializar(hoja), ct);
        return hoja;
    }

    private async Task QuitarReferenciaAsync(CuentaMovil cuenta, Guid id, CancellationToken ct)
    {
        await acceso.WaitAsync(ct);
        try
        {
            var pendientes = (await LeerPendientes(cuenta, ct)).Where(p => p.Aviso.Id != id).ToArray();
            await datos.GuardarMetadatoAsync(cuenta, Pendientes, JsonSerializer.SerializeToUtf8Bytes(pendientes, AvisosLocalesJson.Default.ReferenciaAvisoArray), ct);
        }
        finally { acceso.Release(); }
    }
    private async Task<ReferenciaAviso[]> LeerPendientes(CuentaMovil cuenta, CancellationToken ct)
    {
        var bytes = await datos.LeerMetadatoAsync(cuenta, Pendientes, ct);
        if (bytes is null) return [];
        var lista = JsonSerializer.Deserialize(bytes, AvisosLocalesJson.Default.ReferenciaAvisoArray);
        if (lista is null || lista.Length > 100 || lista.Any(p => p is null || p.Aviso is null || p.Aviso.Id == Guid.Empty || !p.Aviso.CorrespondeA(cuenta)))
            throw new InvalidDataException("Las referencias locales de avisos no son válidas.");
        return lista;
    }
    public void Dispose() => acceso.Dispose();
}
internal sealed record ReferenciaAviso(AvisoPush Aviso, bool Abierto, bool Recibido);
[JsonSerializable(typeof(ReferenciaAviso[]))]
internal partial class AvisosLocalesJson : JsonSerializerContext;
