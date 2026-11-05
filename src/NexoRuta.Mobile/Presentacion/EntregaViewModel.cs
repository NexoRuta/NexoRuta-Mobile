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
    private MotivoNoEntrega? motivo;
    public ParadaRuta Parada { get; }
    public System.Collections.ObjectModel.ObservableCollection<SeleccionBulto> Seleccion { get; } = [];
    public ComandoAsincrono EscanearBulto { get; }
    public string ResumenSeleccion => $"{Seleccion.Count(b => b.Escaneado)} de {Seleccion.Count} bultos identificados";
    public string TituloRegistro => Parada.EsRetiro ? "Registrar retiro" : "Registrar entrega";
    public string EtiquetaNombre => Parada.EsRetiro ? "Nombre de quien entrega" : "Nombre del receptor";
    public string EtiquetaFirma => Parada.EsRetiro ? "Firma de quien entrega" : "Firma del receptor";
    public string EtiquetaFoto => Parada.EsRetiro ? "Fotografía del estado de los bultos" : "Foto de entrega";
    public string PreguntaIntento => Parada.EsRetiro ? "¿No pudiste retirar?" : "¿No pudiste entregar?";
    public string NumeroParada => $"PARADA {Parada.Orden}";
    public string Bultos => TextoReparto.Bultos(Parada.Bultos.Length);
    public string EstadoActual => TextoReparto.Estado(padre.EstadoPresentado(Parada));
    public bool MostrarResultado => !PuedeRegistrar;
    public string Indicacion => padre.Cerrada ? "Esta jornada ya finalizó. Podés consultar los datos de la parada." : !padre.Iniciada ? "Iniciá la jornada para comenzar con las entregas." : !padre.Salida ? "Verificá los bultos y comenzá el reparto desde tu hoja de ruta." : "Esta parada ya no requiere una entrega. Revisá las novedades de tu ruta.";
    public string Nombre { get => nombre; set { nombre = value; Cambiar(); } }
    public string Documento { get => documento; set { documento = value; Cambiar(); } }
    public MotivoNoEntrega? Motivo { get => motivo; set { motivo = value; Cambiar(); } }
    public string Mensaje { get => mensaje; private set { mensaje = value; Cambiar(); } }
    public bool MostrarNombre => Parada.Reglas.RequiereNombre;
    public bool MostrarDocumento => Parada.Reglas.RequiereDocumento;
    public bool MostrarFoto => Parada.EsRetiro || Parada.Reglas.RequiereFoto || padre.Hoja.Politica.RequiereFotoIntento || fotoId is not null;
    public bool MostrarFirma => Parada.EsRetiro || Parada.Reglas.RequiereFirma || firmaId is not null;
    public bool Ocupado => padre.Ocupado;
    public MotivoNoEntrega[] Motivos => Parada.Reglas.Motivos;
    public bool PuedeRegistrar => padre.Iniciada && padre.Salida && !padre.Cerrada && (padre.ParadaActiva(Parada) && !padre.EntregaFisica(Parada.EnvioId) || padre.BultosActuales(Parada).Any(b => b.Resultado == "EntregaPendienteValidacion")) && padre.Estado(Parada) is not ("Entregado" or "EntregaSinValidacion");
    public bool MostrarIntento => PuedeRegistrar && !padre.EntregaFisica(Parada.EnvioId);
    public string AccionEntrega => Parada.EsRetiro ? "Confirmar retiro" : padre.EntregaFisica(Parada.EnvioId) ? "Completar entrega" : "Confirmar entrega";
    public string FotoEstado => fotoId is null ? "Pendiente" : capturas.Any(x => x.Id == fotoId && !x.Lista) ? "Foto interrumpida; volvé a capturarla." : "Lista";
    public string FirmaEstado => firmaId is null ? "Pendiente" : capturas.Any(x => x.Id == firmaId && !x.Lista) ? "Firma interrumpida; volvé a capturarla." : "Lista";
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
        foreach (var b in parada.Bultos.Where(b => parada.EsRetiro || b.Resultado is "Pendiente" or "EntregaPendienteValidacion")) Seleccion.Add(new(b, parada.Reglas.Motivos) { Escaneado = b.Resultado == "EntregaPendienteValidacion" });
        EscanearBulto = new(() => Ejecutar(async () =>
        {
            var codigo = await interaccion.EscanearAsync(); if (codigo is null) return;
            var encontrados = Seleccion.Where(b => b.Bulto.Codigo == codigo || b.Bulto.Id.ToString() == codigo).ToArray();
            if (encontrados.Length != 1) throw new InvalidOperationException("El código no identifica un bulto pendiente de esta parada.");
            encontrados[0].Escaneado = true; Cambiar(nameof(ResumenSeleccion));
        }), () => PuedeRegistrar && !Ocupado);
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
            if (Parada.EsRetiro && (fotoId is null || firmaId is null)) throw new InvalidOperationException("El retiro requiere foto del estado y firma de quien entrega.");
            var repetirPrueba = !Parada.EsRetiro && padre.EntregaFisica(Parada.EnvioId);
            if (!repetirPrueba && (Seleccion.All(b => !b.Escaneado) || Parada.EsRetiro && Seleccion.Any(b => !b.Escaneado)
                || !Parada.EsRetiro && !Parada.Reglas.PermiteEntregaParcial && Seleccion.Any(b => !b.Escaneado)
                || !Parada.EsRetiro && Seleccion.Any(b => !b.Escaneado && b.Motivo is null)))
                throw new InvalidOperationException("Escaneá los bultos que entregás o retirás; cada pendiente requiere un motivo. Revisá si el operador admite entrega parcial.");
            var ubicacion = await UbicacionActual();
            var prueba = new DatosOperacionMovil
            {
                ResultadosBultos = Seleccion.Where(b => !repetirPrueba || padre.BultosActuales(Parada).Any(actual => actual.Id == b.Bulto.Id && actual.Resultado == "EntregaPendienteValidacion")).Select(b => new ResultadoBultoMovil(b.Bulto.Id, b.Bulto.Version, b.Escaneado, b.Motivo?.Codigo)).ToArray(),
                Nombre = Nombre, Documento = Documento, FotoId = fotoId, FirmaId = firmaId,
                Latitud = ubicacion?.Latitude, Longitud = ubicacion?.Longitude,
                PrecisionMetros = ubicacion?.Accuracy, PosicionEnUtc = ubicacion?.Timestamp
            };
            var ids = capturas.Where(x => x.OperacionId is null && (x.Id == fotoId || x.Id == firmaId)).Select(x => x.Id).ToArray();
            await datos.RegistrarConCapturasAsync(padre.Cuenta, padre.Jornada!.Id, ids, j =>
                j.RegistrarBultos(Parada.EnvioId, HojaRutaViewModel.Elemento(prueba), DateTimeOffset.UtcNow, Parada.EsRetiro));
            await padre.IntentarSincronizar(); await CargarCapturas();
            Mensaje = Parada.EsRetiro ? "Retiro registrado. Rendí los bultos al regresar al depósito." : padre.Estado(Parada) == "EntregadoParcialmente" ? "Entrega parcial registrada. Los pendientes conservan su motivo y custodia." : padre.Estado(Parada) == "Entregado" ? "Entrega confirmada. Podés continuar con tu próxima parada." : "Entrega registrada. Completá las pruebas pendientes antes de terminar tu jornada.";
        }), () => PuedeRegistrar && !Ocupado);
        Intento = new(() => Ejecutar(async () =>
        {
            if (Motivo is null || !Motivos.Contains(Motivo)) throw new InvalidOperationException("Seleccioná un motivo del operador.");
            if (padre.Hoja.Politica.RequiereFotoIntento && (fotoId is null || capturas.Any(x => x.Id == fotoId && !x.Lista)))
                throw new InvalidOperationException("El operador exige una foto para registrar el intento fallido.");
            var ubicacion = await UbicacionActual();
            var prueba = new DatosOperacionMovil { Motivo = Motivo.Codigo, FotoId = fotoId, Latitud = ubicacion?.Latitude,
                Longitud = ubicacion?.Longitude, PrecisionMetros = ubicacion?.Accuracy, PosicionEnUtc = ubicacion?.Timestamp };
            var ids = capturas.Where(x => x.OperacionId is null && x.Id == fotoId).Select(x => x.Id).ToArray();
            await datos.RegistrarConCapturasAsync(padre.Cuenta, padre.Jornada!.Id, ids,
                j => Parada.EsRetiro ? j.RegistrarRetiroFallido(Parada.EnvioId, HojaRutaViewModel.Elemento(prueba), DateTimeOffset.UtcNow) : j.RegistrarIntento(Parada.EnvioId, HojaRutaViewModel.Elemento(prueba), DateTimeOffset.UtcNow));
            fotoId = null; await padre.IntentarSincronizar(); await CargarCapturas(); Mensaje = "Intento registrado.";
        }), () => MostrarIntento && !Ocupado);
        padre.PropertyChanged += PadreCambio;
    }
    private void PadreCambio(object? sender, PropertyChangedEventArgs e) => Actualizar();
    public async Task InicializarAsync() => await Ejecutar(async () =>
    {
        if (padre.Jornada is null) { Mensaje = "Iniciá la jornada y confirmá la carga antes de registrar entregas."; return; }
        await evidencias.RecuperarAsync(padre.Cuenta, padre.Jornada.Id);
        var registros = padre.Jornada.Operaciones.Where(x => x.EnvioId == Parada.EnvioId && x.Tipo is TipoOperacionLocal.EntregaFisica or TipoOperacionLocal.EvidenciaEntrega or TipoOperacionLocal.EntregaBultos or TipoOperacionLocal.RetirarBultos);
        foreach (var op in registros)
        {
            var prueba = op.Datos.Deserialize(DatosOperacionJson.Default.DatosOperacionMovil)!;
            foreach (var r in prueba.ResultadosBultos ?? [])
            {
                var b = Seleccion.SingleOrDefault(b => b.Bulto.Id == r.BultoId);
                if (b is not null) { b.Escaneado = r.Entregado; b.Motivo = Motivos.SingleOrDefault(m => m.Codigo == r.Motivo); }
            }
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
            await CargarCapturas(); Mensaje = "Prueba lista. Confirmá la entrega para finalizar.";
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
            catch (Exception) { Mensaje = "No pudimos completar la acción. Tu trabajo está guardado; volvé a intentarlo."; }
        });
        Actualizar();
    }
    public void Desconectar() => padre.PropertyChanged -= PadreCambio;
    private void Actualizar()
    {
        foreach (var propiedad in new[] { nameof(Ocupado), nameof(PuedeRegistrar), nameof(EstadoActual), nameof(MostrarResultado), nameof(Indicacion), nameof(MostrarIntento), nameof(AccionEntrega), nameof(FotoEstado), nameof(FirmaEstado), nameof(QuitarFotoVisible), nameof(QuitarFirmaVisible) }) Cambiar(propiedad);
        EscanearBulto?.Actualizar(); TomarFoto?.Actualizar(); Firmar?.Actualizar(); QuitarFoto?.Actualizar(); QuitarFirma?.Actualizar(); Entregar?.Actualizar(); Intento?.Actualizar();
    }
    private void Cambiar([CallerMemberName] string? nombre = null) => PropertyChanged?.Invoke(this, new(nombre));
}

public sealed class SeleccionBulto(BultoRuta bulto, MotivoNoEntrega[] motivos) : INotifyPropertyChanged
{
    public BultoRuta Bulto { get; } = bulto;
    public MotivoNoEntrega[] Motivos { get; } = motivos;
    private bool escaneado;
    public bool Escaneado { get => escaneado; set { escaneado = value; PropertyChanged?.Invoke(this, new(nameof(Estado))); } }
    public string Estado => Escaneado ? "Identificado por escaneo" : "Pendiente de escaneo";
    public MotivoNoEntrega? Motivo { get; set; }
    public event PropertyChangedEventHandler? PropertyChanged;
}
