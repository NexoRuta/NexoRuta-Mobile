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
    public bool PuedeRegistrar => padre.Iniciada && padre.Salida && !padre.Cerrada && (padre.ParadaActiva(Parada) && !padre.EntregaFisica(Parada.EnvioId) && padre.BultosActuales(Parada).Any(b => b.Resultado == "Pendiente") || padre.BultosActuales(Parada).Any(b => b.Resultado == "EntregaPendienteValidacion" && b.EnEstaRuta)) && (!Parada.EsRetiro || padre.ConflictoRetiro(Parada.EnvioId) is null);
    private bool EntregaDeclarada => padre.EntregaFisica(Parada.EnvioId) || padre.BultosActuales(Parada).Any(b => b.Resultado == "EntregaPendienteValidacion" && b.EnEstaRuta);
    public bool MostrarIntento => PuedeRegistrar && !EntregaDeclarada;
    public string AccionEntrega => Parada.EsRetiro ? "Confirmar retiro" : padre.ConflictoEntrega(Parada.EnvioId) is not null ? "Reintentar entrega" : EntregaDeclarada ? "Completar entrega" : "Confirmar entrega";
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
    public ComandoAsincrono ReintentarRetiro { get; }
    public bool MostrarResolucionRetiro => Parada.EsRetiro && padre.ConflictoRetiro(Parada.EnvioId) is not null && !padre.Cerrada;
    public event PropertyChangedEventHandler? PropertyChanged;
    public EntregaViewModel(HojaRutaViewModel padre, ParadaRuta parada, RepositorioJornadasSqlite datos,
        EvidenciasLocales evidencias, IInteraccionReparto interaccion)
    {
        this.padre = padre; Parada = parada; this.datos = datos; this.evidencias = evidencias; this.interaccion = interaccion;
        foreach (var b in padre.BultosActuales(parada).Where(b => parada.EsRetiro || b.Resultado == "Pendiente" || b.Resultado == "EntregaPendienteValidacion" && b.EnEstaRuta)) Seleccion.Add(new(b, parada.Reglas.Motivos) { Escaneado = b.Resultado == "EntregaPendienteValidacion" });
        foreach (var b in Seleccion) b.PropertyChanged += (_, _) => Cambiar(nameof(ResumenSeleccion));
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
            var rechazo = padre.ConflictoEntrega(Parada.EnvioId);
            if (rechazo is not null && !await interaccion.ConfirmarResolucionAsync(false)) return;
            if (capturas.Any(x => (x.Id == fotoId || x.Id == firmaId) && !x.Lista))
                throw new InvalidOperationException("Recapturá o descartá los borradores interrumpidos antes de confirmar.");
            if (Parada.EsRetiro && (fotoId is null || firmaId is null)) throw new InvalidOperationException("El retiro requiere foto del estado y firma de quien entrega.");
            var repetirPrueba = !Parada.EsRetiro && EntregaDeclarada;
            if (!repetirPrueba && (Seleccion.All(b => !b.Escaneado) || Parada.EsRetiro && Seleccion.Any(b => !b.Escaneado)
                || !Parada.EsRetiro && !Parada.Reglas.PermiteEntregaParcial && Seleccion.Any(b => !b.Escaneado)
                || !Parada.EsRetiro && Seleccion.Any(b => !b.Escaneado && b.Motivo is null)))
                throw new InvalidOperationException("Escaneá los bultos que entregás o retirás; cada pendiente requiere un motivo. Revisá si el operador admite entrega parcial.");
            var ubicacion = await UbicacionActual();
            var prueba = new DatosOperacionMovil
            {
                ResuelveOperacionId = rechazo?.Id,
                ResultadosBultos = Seleccion.Where(b => !repetirPrueba || padre.BultosActuales(Parada).Any(actual => actual.Id == b.Bulto.Id && actual.Resultado == "EntregaPendienteValidacion" && actual.EnEstaRuta)).Select(b => new ResultadoBultoMovil(b.Bulto.Id, b.Bulto.Version, repetirPrueba || b.Escaneado, repetirPrueba ? null : b.Motivo?.Codigo)).ToArray(),
                Nombre = Nombre, Documento = Documento, FotoId = fotoId, FirmaId = firmaId,
                Latitud = ubicacion?.Latitude, Longitud = ubicacion?.Longitude,
                PrecisionMetros = ubicacion?.Accuracy, PosicionEnUtc = ubicacion?.Timestamp
            };
            var ids = capturas.Where(x => x.OperacionId is null && (x.Id == fotoId || x.Id == firmaId)).Select(x => x.Id).ToArray();
            var registrada = await datos.RegistrarConCapturasAsync(padre.Cuenta, padre.Jornada!.Id, ids, j =>
                j.RegistrarBultos(Parada.EnvioId, HojaRutaViewModel.Elemento(prueba), DateTimeOffset.UtcNow, Parada.EsRetiro));
            await padre.IntentarSincronizar(); await CargarCapturas();
            Mensaje = padre.ResultadoOperacion(registrada.Operaciones[^1],
                Parada.EsRetiro ? "Retiro confirmado por el servidor. Rendí los bultos al regresar al depósito." : "Resultado de entrega confirmado por el servidor.",
                Parada.EsRetiro ? "Retiro guardado localmente, pendiente de confirmación del servidor. Conservá los bultos y consultá su estado antes de rendir." : "Entrega guardada localmente. Podés completar la prueba sin conexión; la validación definitiva se realiza al sincronizar.");
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
        ReintentarRetiro = new(() => Ejecutar(async () =>
        {
            var conflicto = padre.ConflictoRetiro(Parada.EnvioId) ?? throw new InvalidOperationException("No existe un retiro rechazado para resolver.");
            if (!await interaccion.ConfirmarResolucionAsync(true)) return;
            var original = conflicto.Datos.Deserialize(DatosOperacionJson.Default.DatosOperacionMovil)!;
            var resolucion = new DatosOperacionMovil { ResuelveOperacionId = conflicto.Id, ResultadosBultos = original.ResultadosBultos,
                FotoId = original.FotoId, FirmaId = original.FirmaId, Nombre = original.Nombre, Documento = original.Documento,
                Latitud = original.Latitud, Longitud = original.Longitud, PrecisionMetros = original.PrecisionMetros, PosicionEnUtc = original.PosicionEnUtc };
            var registrada = await datos.RegistrarConCapturasAsync(padre.Cuenta, padre.Jornada!.Id, [],
                j => j.RegistrarBultos(Parada.EnvioId, HojaRutaViewModel.Elemento(resolucion), DateTimeOffset.UtcNow, true));
            await padre.IntentarSincronizar();
            Mensaje = padre.ResultadoOperacion(registrada.Operaciones[^1], "Resolución del retiro confirmada por el servidor.", "Resolución guardada localmente; pendiente de confirmación del servidor.");
        }), () => MostrarResolucionRetiro && !Ocupado);
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
        if (padre.ConflictoRetiro(Parada.EnvioId) is { } conflicto)
            Mensaje = padre.ResultadoOperacion(conflicto, "", "") + " Conservamos el hecho físico y sus pruebas. Coordiná con el depósito; cuando se resuelva la causa, usá Reintentar retiro.";
        else if (padre.ConflictoEntrega(Parada.EnvioId) is { } rechazada)
            Mensaje = padre.ResultadoOperacion(rechazada, "", "") + " Conservamos el registro y sus pruebas. Revisá los bultos y motivos; cuando se resuelva la causa, usá Reintentar entrega.";
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
        foreach (var propiedad in new[] { nameof(MostrarResolucionRetiro), nameof(Ocupado), nameof(PuedeRegistrar), nameof(EstadoActual), nameof(MostrarResultado), nameof(Indicacion), nameof(MostrarIntento), nameof(AccionEntrega), nameof(FotoEstado), nameof(FirmaEstado), nameof(QuitarFotoVisible), nameof(QuitarFirmaVisible) }) Cambiar(propiedad);
        ReintentarRetiro?.Actualizar(); EscanearBulto?.Actualizar(); TomarFoto?.Actualizar(); Firmar?.Actualizar(); QuitarFoto?.Actualizar(); QuitarFirma?.Actualizar(); Entregar?.Actualizar(); Intento?.Actualizar();
    }
    private void Cambiar([CallerMemberName] string? nombre = null) => PropertyChanged?.Invoke(this, new(nombre));
}

public sealed class SeleccionBulto(BultoRuta bulto, MotivoNoEntrega[] motivos) : INotifyPropertyChanged
{
    public BultoRuta Bulto { get; } = bulto;
    public MotivoNoEntrega[] Motivos { get; } = motivos;
    private bool escaneado;
    public bool Escaneado { get => escaneado; set { if (escaneado == value && (!value || motivo is null)) return; escaneado = value; if (value) motivo = null; Notificar(); } }
    public string Estado => Escaneado ? "Identificado por escaneo" : "Pendiente de escaneo";
    private MotivoNoEntrega? motivo;
    public MotivoNoEntrega? Motivo { get => motivo; set { if (motivo == value && (value is null || !escaneado)) return; motivo = value; if (value is not null) escaneado = false; Notificar(); } }
    private void Notificar()
    {
        PropertyChanged?.Invoke(this, new(nameof(Escaneado)));
        PropertyChanged?.Invoke(this, new(nameof(Motivo)));
        PropertyChanged?.Invoke(this, new(nameof(Estado)));
    }
    public event PropertyChangedEventHandler? PropertyChanged;
}
