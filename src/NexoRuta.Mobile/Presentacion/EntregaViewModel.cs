using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text.Json;
using NexoRuta.Mobile.Application;
using NexoRuta.Mobile.Captura;
using NexoRuta.Mobile.Core.Jornadas;
using NexoRuta.Mobile.Core.Rutas;
using NexoRuta.Mobile.Storage;
namespace NexoRuta.Mobile.Presentacion;
public sealed class EntregaViewModel : INotifyPropertyChanged
{
    private readonly HojaRutaViewModel padre;
    private readonly RepositorioJornadasSqlite datos;
    private readonly EvidenciasLocales evidencias;
    private readonly IInteraccionReparto interaccion;
    private List<CapturaLocal> capturas = [];
    private Guid? fotoId, firmaId;
    private string nombre = "", documento = "", mensaje = "";
    private string? motivo;
    public ParadaRuta Parada { get; }
    public string Nombre { get => nombre; set { nombre = value; Cambiar(); } }
    public string Documento { get => documento; set { documento = value; Cambiar(); } }
    public string? Motivo { get => motivo; set { motivo = value; Cambiar(); } }
    public string Mensaje { get => mensaje; private set { mensaje = value; Cambiar(); } }
    public bool MostrarNombre => padre.Hoja.Politica.RequiereNombre;
    public bool MostrarDocumento => padre.Hoja.Politica.RequiereDocumento;
    public bool MostrarFoto => padre.Hoja.Politica.RequiereFoto || padre.Hoja.Politica.RequiereFotoIntento || fotoId is not null;
    public bool MostrarFirma => padre.Hoja.Politica.RequiereFirma || firmaId is not null;
    public bool Ocupado => padre.Ocupado;
    public string[] Motivos => padre.Hoja.Politica.Motivos;
    public bool PuedeRegistrar => padre.Iniciada && padre.Salida && !padre.Cerrada && padre.Estado(Parada) is not ("Entregado" or "EntregaSinValidacion");
    public bool MostrarIntento => PuedeRegistrar && !padre.EntregaFisica(Parada.EnvioId);
    public string AccionEntrega => padre.EntregaFisica(Parada.EnvioId) ? "Completar prueba de entrega" : "Declarar entrega física";
    public string FotoEstado => fotoId is null ? "Sin foto" : capturas.Any(x => x.Id == fotoId && !x.Lista) ? "Foto interrumpida; volvé a capturarla." : "Foto guardada";
    public string FirmaEstado => firmaId is null ? "Sin firma" : capturas.Any(x => x.Id == firmaId && !x.Lista) ? "Firma interrumpida; volvé a capturarla." : "Firma guardada";
    public bool QuitarFotoVisible => capturas.Any(x => x.Id == fotoId && x.OperacionId is null);
    public bool QuitarFirmaVisible => capturas.Any(x => x.Id == firmaId && x.OperacionId is null);
    public ComandoAsincrono TomarFoto { get; }
    public ComandoAsincrono Firmar { get; }
    public ComandoAsincrono QuitarFoto { get; }
    public ComandoAsincrono QuitarFirma { get; }
    public ComandoAsincrono Entregar { get; }
    public ComandoAsincrono Intento { get; }
    public event PropertyChangedEventHandler? PropertyChanged;
    public EntregaViewModel(HojaRutaViewModel padre, ParadaRuta parada, RepositorioJornadasSqlite datos,
        EvidenciasLocales evidencias, IInteraccionReparto interaccion)
    {
        this.padre = padre; Parada = parada; this.datos = datos; this.evidencias = evidencias; this.interaccion = interaccion;
        TomarFoto = new(() => Ejecutar(async () => await GuardarCaptura("Foto", await interaccion.FotoAsync())),
            () => PuedeRegistrar && !Ocupado && (fotoId is null || capturas.Any(x => x.Id == fotoId && !x.Lista)));
        Firmar = new(() => Ejecutar(async () => await GuardarCaptura("Firma", await interaccion.FirmaAsync())),
            () => PuedeRegistrar && !Ocupado && (firmaId is null || capturas.Any(x => x.Id == firmaId && !x.Lista)));
        QuitarFoto = new(() => Ejecutar(async () => { await datos.DescartarCapturaAsync(padre.Cuenta, fotoId!.Value); fotoId = null; await CargarCapturas(); }),
            () => !Ocupado && QuitarFotoVisible);
        QuitarFirma = new(() => Ejecutar(async () => { await datos.DescartarCapturaAsync(padre.Cuenta, firmaId!.Value); firmaId = null; await CargarCapturas(); }),
            () => !Ocupado && QuitarFirmaVisible);
        Entregar = new(() => Ejecutar(async () =>
        {
            if (capturas.Any(x => (x.Id == fotoId || x.Id == firmaId) && !x.Lista))
                throw new InvalidOperationException("Recapturá o descartá los borradores interrumpidos antes de confirmar.");
            var ubicacion = await UbicacionActual();
            var prueba = new DatosOperacionMovil
            {
                Nombre = Nombre, Documento = Documento, FotoId = fotoId, FirmaId = firmaId,
                Latitud = ubicacion?.Latitude, Longitud = ubicacion?.Longitude,
                PrecisionMetros = ubicacion?.Accuracy, PosicionEnUtc = ubicacion?.Timestamp
            };
            var ids = capturas.Where(x => x.OperacionId is null && (x.Id == fotoId || x.Id == firmaId)).Select(x => x.Id).ToArray();
            await datos.RegistrarConCapturasAsync(padre.Cuenta, padre.Jornada!.Id, ids, j =>
                j.Operaciones.Any(o => o.EnvioId == Parada.EnvioId && o.Tipo == TipoOperacionLocal.EntregaFisica)
                    ? j.AdjuntarEvidencia(Parada.EnvioId, HojaRutaViewModel.Elemento(prueba), DateTimeOffset.UtcNow)
                    : j.RegistrarEntregaFisica(Parada.EnvioId, HojaRutaViewModel.Elemento(prueba), DateTimeOffset.UtcNow));
            await padre.IntentarSincronizar(); await CargarCapturas();
            Mensaje = "La declaración está guardada. Si falta prueba exigida, queda pendiente hasta completar la evidencia o cerrar la jornada.";
        }), () => PuedeRegistrar && !Ocupado);
        Intento = new(() => Ejecutar(async () =>
        {
            if (Motivo is null || !Motivos.Contains(Motivo, StringComparer.Ordinal)) throw new InvalidOperationException("Seleccioná un motivo del operador.");
            if (padre.Hoja.Politica.RequiereFotoIntento && (fotoId is null || capturas.Any(x => x.Id == fotoId && !x.Lista)))
                throw new InvalidOperationException("El operador exige una foto para registrar el intento fallido.");
            var ubicacion = await UbicacionActual();
            var prueba = new DatosOperacionMovil { Motivo = Motivo, FotoId = fotoId, Latitud = ubicacion?.Latitude,
                Longitud = ubicacion?.Longitude, PrecisionMetros = ubicacion?.Accuracy, PosicionEnUtc = ubicacion?.Timestamp };
            var ids = capturas.Where(x => x.OperacionId is null && x.Id == fotoId).Select(x => x.Id).ToArray();
            await datos.RegistrarConCapturasAsync(padre.Cuenta, padre.Jornada!.Id, ids,
                j => j.RegistrarIntento(Parada.EnvioId, HojaRutaViewModel.Elemento(prueba), DateTimeOffset.UtcNow));
            fotoId = null; await padre.IntentarSincronizar(); await CargarCapturas(); Mensaje = "Intento guardado. " + padre.Resumen;
        }), () => MostrarIntento && !Ocupado);
        padre.PropertyChanged += PadreCambio;
    }
    private void PadreCambio(object? sender, PropertyChangedEventArgs e) => Actualizar();
    public async Task InicializarAsync() => await Ejecutar(async () =>
    {
        if (padre.Jornada is null) { Mensaje = "Iniciá la jornada y confirmá la carga antes de registrar entregas."; return; }
        await evidencias.RecuperarAsync(padre.Cuenta, padre.Jornada.Id);
        var registros = padre.Jornada.Operaciones.Where(x => x.EnvioId == Parada.EnvioId && x.Tipo is TipoOperacionLocal.EntregaFisica or TipoOperacionLocal.EvidenciaEntrega);
        foreach (var op in registros)
        {
            var prueba = op.Datos.Deserialize(DatosOperacionJson.Default.DatosOperacionMovil)!;
            Nombre = prueba.Nombre ?? Nombre; Documento = prueba.Documento ?? Documento;
            fotoId = prueba.FotoId ?? fotoId; firmaId = prueba.FirmaId ?? firmaId;
        }
        await CargarCapturas();
    });
    private async Task CargarCapturas()
    {
        if (padre.Jornada is null) return;
        capturas = (await datos.LeerCapturasAsync(padre.Cuenta, padre.Jornada.Id)).Where(x => x.EnvioId == Parada.EnvioId).ToList();
        fotoId = capturas.LastOrDefault(x => x.Tipo == "Foto" && x.OperacionId is null)?.Id ?? fotoId;
        firmaId = capturas.LastOrDefault(x => x.Tipo == "Firma" && x.OperacionId is null)?.Id ?? firmaId;
        Actualizar();
    }
    private async Task GuardarCaptura(string tipo, byte[]? contenido)
    {
        if (contenido is null) return;
        try
        {
            var anterior = capturas.LastOrDefault(x => x.Tipo == tipo && !x.Lista && x.OperacionId is null);
            Guid id;
            if (anterior is null) id = (await evidencias.GuardarAsync(padre.Cuenta, padre.Jornada!.Id, Parada.EnvioId, tipo, contenido)).Id;
            else { await evidencias.CompletarAsync(padre.Cuenta, anterior, contenido); id = anterior.Id; }
            if (tipo == "Foto") fotoId = id; else firmaId = id;
            await CargarCapturas(); Mensaje = "Borrador guardado de forma segura. Confirmá la declaración para asociarlo a la entrega.";
        }
        finally { CryptographicOperations.ZeroMemory(contenido); }
    }
    private static async Task<Location?> UbicacionActual()
    {
        try
        {
            using var limite = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            var punto = await Geolocation.Default.GetLocationAsync(new(GeolocationAccuracy.Best, TimeSpan.FromSeconds(8)), limite.Token);
            return punto is not null && punto.Accuracy is > 0 && double.IsFinite(punto.Latitude) && double.IsFinite(punto.Longitude) ? punto : null;
        }
        catch (Exception) { return null; }
    }
    private async Task Ejecutar(Func<Task> operacion)
    {
        await padre.Ejecutar(async () =>
        {
            try { await operacion(); }
            catch (InvalidOperationException ex) { Mensaje = ex.Message; }
            catch (Exception) { Mensaje = "No se completó la operación. Se conservan los registros y los borradores existentes."; }
        });
        Actualizar();
    }
    public void Desconectar() => padre.PropertyChanged -= PadreCambio;
    private void Actualizar()
    {
        foreach (var propiedad in new[] { nameof(Ocupado), nameof(PuedeRegistrar), nameof(MostrarIntento), nameof(AccionEntrega), nameof(FotoEstado), nameof(FirmaEstado), nameof(QuitarFotoVisible), nameof(QuitarFirmaVisible) }) Cambiar(propiedad);
        TomarFoto?.Actualizar(); Firmar?.Actualizar(); QuitarFoto?.Actualizar(); QuitarFirma?.Actualizar(); Entregar?.Actualizar(); Intento?.Actualizar();
    }
    private void Cambiar([CallerMemberName] string? nombre = null) => PropertyChanged?.Invoke(this, new(nombre));
}
