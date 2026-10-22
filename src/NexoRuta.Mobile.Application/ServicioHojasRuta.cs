using NexoRuta.Mobile.Api.Identidad;
using NexoRuta.Mobile.Core.Jornadas;
using NexoRuta.Mobile.Core.Rutas;
using NexoRuta.Mobile.Storage;

namespace NexoRuta.Mobile.Application;

public sealed class ServicioHojasRuta(ClienteSesionMovil cliente, RepositorioJornadasSqlite datos)
{
    public async Task ActualizarAsync(CuentaMovil cuenta, DateOnly fecha, CancellationToken ct = default)
    {
        if (await cliente.ObtenerCuentaLocalAsync(ct) != cuenta) throw new InvalidOperationException("La sesión no corresponde a esta cuenta.");
        var hojas = await cliente.ObtenerHojasAsync(fecha, ct);
        if (await cliente.ObtenerCuentaLocalAsync(ct) != cuenta) throw new InvalidOperationException("La cuenta cambió durante la actualización.");
        foreach (var hoja in hojas) hoja.Validar(cuenta);
        foreach (var hoja in hojas)
        {
            var bytes = SerializadorHojaRuta.Serializar(hoja);
            if (await datos.LeerAsync(cuenta, hoja.JornadaId, ct) is null)
                await datos.GuardarHojaAsync(cuenta, hoja.Id, bytes, ct);
            await datos.GuardarVistaRemotaAsync(cuenta, hoja.Id, hoja.Version, bytes, ct);
        }
    }
}
