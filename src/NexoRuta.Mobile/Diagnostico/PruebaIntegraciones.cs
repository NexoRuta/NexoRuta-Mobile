#if VALIDAR_ALMACENAMIENTO && ANDROID
using System.Security.Cryptography;
using NexoRuta.Mobile.Almacenamiento;
using NexoRuta.Mobile.Application;
using NexoRuta.Mobile.Captura;
using NexoRuta.Mobile.Core.Identidad;
using NexoRuta.Mobile.Core.Jornadas;
using NexoRuta.Mobile.Storage;
namespace NexoRuta.Mobile.Diagnostico;
internal static class PruebaIntegraciones
{
    private const string Tag = "NexoRuta.NativeProof";
    public static async Task EjecutarAsync(string fase)
    {
        await MainThread.InvokeOnMainThreadAsync(async () =>
        {
            byte[]? imagen = null;
            try
            {
                if (fase == "mapa-adaptador")
                {
                    using var estado = System.Text.Json.JsonDocument.Parse(Mapas.MapboxOfflineAndroid.Progress("nexoruta-" + Guid.Empty.ToString("N")));
                    if (estado.RootElement.GetProperty("phase").GetString() != "missing") throw new InvalidDataException();
                    using var fondo = System.Text.Json.JsonDocument.Parse(await Task.Run(() =>
                        Mapas.MapboxOfflineAndroid.Progress("nexoruta-" + Guid.Empty.ToString("N"))));
                    if (fondo.RootElement.GetProperty("phase").GetString() != "missing") throw new InvalidDataException();
                    global::Android.Util.Log.Info(Tag, "MAPA_PUENTE_JNI_FONDO_OK_SIN_DESCARGA");
                    return;
                }
                var servicios = IPlatformApplication.Current!.Services;
                var navegacion = Microsoft.Maui.Controls.Application.Current!.Windows[0].Page!.Navigation;
                if (fase == "biometria")
                {
                    var valida = await servicios.GetRequiredService<IVerificacionLocal>().VerificarAsync("Prueba local de desbloqueo de NexoRuta");
                    global::Android.Util.Log.Info(Tag, valida ? "AUTENTICACION_LOCAL_OK" : "AUTENTICACION_CANCELADA_SIN_AUTORIZAR");
                    return;
                }
                if (fase == "escaner")
                {
                    var scanner = new EscanerPage(); await navegacion.PushAsync(scanner);
                    var codigo = await scanner.Resultado;
                    global::Android.Util.Log.Info(Tag, codigo == "NEXORUTA-PRUEBA-2026" ? "ESCANER_OK" : "ESCANER_CANCELADO_O_CODIGO_DISTINTO");
                    return;
                }
                if (fase == "camara")
                {
                    var foto = new FotoPage(); await navegacion.PushAsync(foto); imagen = await foto.Resultado;
                }
                else if (fase == "firma")
                {
                    var firma = new FirmaPage(); await navegacion.PushAsync(firma); imagen = await firma.Resultado;
                }
                if (imagen is null) { global::Android.Util.Log.Info(Tag, "CAPTURA_CANCELADA"); return; }
                var cuenta = new CuentaMovil(Guid.Parse("00000000-0000-0000-0000-000000000031"),
                    Guid.Parse("00000000-0000-0000-0000-000000000032"), Guid.Parse("00000000-0000-0000-0000-000000000033"));
                using var datos = new RepositorioJornadasSqlite(DirectorioEvidencias.Crear("prueba-integraciones-sql"), new AlmacenClavesProtegidas());
                using var archivos = new RepositorioArchivosCifrados(DirectorioEvidencias.Crear("prueba-integraciones-archivos"), new AlmacenClavesProtegidas());
                var jornada = JornadaLocal.Iniciar(Guid.NewGuid(), Guid.NewGuid(), cuenta, DateTimeOffset.UtcNow);
                await datos.CrearAsync(jornada);
                var guardada = await new EvidenciasLocales(datos, archivos).GuardarAsync(cuenta, jornada.Id, Guid.NewGuid(),
                    fase == "firma" ? "Firma" : "Foto", imagen);
                var recuperada = await archivos.LeerAsync(cuenta, guardada.Id);
                try
                {
                    if (!CryptographicOperations.FixedTimeEquals(imagen, recuperada)) throw new InvalidDataException();
                    global::Android.Util.Log.Info(Tag, fase == "firma" ? "FIRMA_CIFRADA_OK" : "CAMARA_COMPRIMIDA_CIFRADA_OK");
                }
                finally { CryptographicOperations.ZeroMemory(recuperada); }
            }
            catch (Exception ex) { global::Android.Util.Log.Error(Tag, "PRUEBA_FALLO: " + ex.GetType().Name); }
            finally { if (imagen is not null) CryptographicOperations.ZeroMemory(imagen); }
        });
    }
}
#endif
