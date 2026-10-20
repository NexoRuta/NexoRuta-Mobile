using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.Json;
using NexoRuta.Mobile.Api.Identidad;
using NexoRuta.Mobile.Api.Reparto;
using NexoRuta.Mobile.Application;
using NexoRuta.Mobile.Captura;
using NexoRuta.Mobile.Core.Identidad;
using NexoRuta.Mobile.Core.Jornadas;
using NexoRuta.Mobile.Core.Rutas;
using NexoRuta.Mobile.Storage;
using NexoRuta.Mobile.Ubicacion;
namespace NexoRuta.Mobile.Presentacion;

public sealed class HojaRutaViewModel : INotifyPropertyChanged
{
    private readonly RepositorioJornadasSqlite datos;
    private readonly ClienteSesionMovil? cliente;
    private readonly SincronizadorJornada? sincronizador;
    private readonly ITrackingJornada seguimiento;
    private readonly ServicioAvisosReparto? avisos;
    private HojaRuta? vistaRemota;
    private readonly IInteraccionReparto interaccion;
    private readonly IVerificacionLocal verificacion;
    private readonly Dictionary<Guid, ConfirmacionOperacion> confirmaciones = [];
    private bool ocupado;
    private string mensaje = "";
    public HojaRuta Hoja { get; private set; }
    public CuentaMovil Cuenta { get; }
    public JornadaLocal? Jornada { get; private set; }
    public ObservableCollection<ParadaResumen> Paradas { get; } = [];
    public event PropertyChangedEventHandler? PropertyChanged;
    public bool Ocupado { get => ocupado; private set { ocupado = value; Actualizar(); } }
    public string Mensaje { get => mensaje; private set { mensaje = value; Cambiar(); } }
    public bool Iniciada => Jornada is { } j && confirmaciones.TryGetValue(j.Operaciones[0].Id, out var inicio) && inicio.Resultado == "Aplicada";
    public bool Cerrada => Jornada?.EstaCerrada ?? false;
    public bool Salida => Registrada(TipoOperacionLocal.ConfirmarCarga);
    public bool MostrarInicio => !Iniciada && !Cerrada && Hoja.Estado == "Despachada" && vistaRemota?.Estado is not ("EnCurso" or "Finalizada");
    public bool MostrarCarga => Iniciada && !Salida && !Cerrada;
    public bool MostrarRendicion => Iniciada && !Cerrada && BultosParaRendir().Count > 0;
    public bool MostrarCierre => Iniciada && !Cerrada;
    public string Resumen => Jornada is null ? "Hoja descargada; jornada sin iniciar."
        : $"{Jornada.Operaciones.Count - Jornada.ExportarEstado().Confirmadas.Count} registro(s) por sincronizar · {confirmaciones.Values.Count(x => x.Resultado == "Conflicto")} conflicto(s)";
    public ComandoAsincrono Iniciar { get; }
    public ComandoAsincrono Escanear { get; }
    public ComandoAsincrono ConfirmarCarga { get; }
    public ComandoAsincrono Sincronizar { get; }
    public ComandoAsincrono Rendir { get; }
    public ComandoAsincrono Finalizar { get; }

    public HojaRutaViewModel(HojaRuta hoja, CuentaMovil cuenta, RepositorioJornadasSqlite datos,
        ITrackingJornada seguimiento, IInteraccionReparto interaccion, IVerificacionLocal verificacion,
        ClienteSesionMovil? cliente = null, SincronizadorJornada? sincronizador = null, ServicioAvisosReparto? avisos = null)
    {
        Hoja = hoja; Cuenta = cuenta; this.datos = datos; this.seguimiento = seguimiento;
        this.avisos = avisos; this.interaccion = interaccion; this.verificacion = verificacion; this.cliente = cliente; this.sincronizador = sincronizador;
        Iniciar = new(() => Ejecutar(async () =>
        {
            if (Jornada is null)
            {
                if (await datos.TienePendientesAsync(Cuenta) || (await datos.LeerJornadasAsync(Cuenta, soloAbiertas: true)).Count > 0)
                    throw new InvalidOperationException("Sincronizá y cerrá la jornada anterior antes de iniciar otra.");
                await cliente!.ExigirPerfilAsync();
                var actual = (await cliente.ObtenerHojasAsync(Hoja.Fecha)).SingleOrDefault(x => x.Id == Hoja.Id);
                if (actual is null || actual.Estado != "Despachada") throw new InvalidOperationException("La hoja ya no está disponible para iniciar. Descargá sus cambios.");
                Hoja = actual; await datos.GuardarHojaAsync(Cuenta, Hoja.Id, SerializadorHojaRuta.Serializar(Hoja));
                Jornada = JornadaLocal.Iniciar(Hoja.JornadaId, Hoja.Id, Cuenta, DateTimeOffset.UtcNow);
                await datos.CrearAsync(Jornada);
            }
            await sincronizador!.SincronizarAsync(Cuenta, Jornada.Id);
            await Refrescar();
            if (!Iniciada) throw new InvalidOperationException("El servidor no autorizó el inicio. Se conserva el registro para revisión.");
            Mensaje = await seguimiento.IniciarAsync() ? "Jornada iniciada. Escaneá todos los bultos antes de salir."
                : "Jornada iniciada, sin permiso o señal de ubicación. Revisá los permisos; las entregas pueden conservar prueba pendiente.";
        }), () => !Ocupado && MostrarInicio && cliente is not null);
        Escanear = new(() => Ejecutar(async () =>
        {
            var codigo = await interaccion.EscanearAsync(); if (codigo is null) return;
            var coincidencias = Hoja.Paradas.Where(ParadaActiva).SelectMany(p => p.Bultos.Select(b => (Parada: p, Bulto: b)))
                .Where(x => x.Bulto.Codigo == codigo || x.Bulto.Id.ToString() == codigo).ToArray();
            if (coincidencias.Length != 1) throw new InvalidOperationException("El código no identifica un único bulto asignado a esta hoja. Consultá al depósito.");
            var elegido = coincidencias[0];
            if (Cargados().Contains(elegido.Bulto.Id)) { Mensaje = "Ese bulto ya está escaneado."; return; }
            Jornada = await datos.ModificarAsync(Cuenta, Jornada!.Id, j => j.RegistrarCarga(elegido.Parada.EnvioId,
                Elemento(new() { BultoId = elegido.Bulto.Id }), DateTimeOffset.UtcNow));
            await IntentarSincronizar(); Mensaje = "Carga registrada en este teléfono. " + Resumen;
        }), () => !Ocupado && MostrarCarga);
        ConfirmarCarga = new(() => Ejecutar(async () =>
        {
            var asignados = Hoja.Paradas.Where(ParadaActiva).SelectMany(p => p.Bultos).Select(b => b.Id).ToArray();
            if (Hoja.Politica.BloquearDiscrepanciasCarga && asignados.Any(id => !Cargados().Contains(id)))
                throw new InvalidOperationException("Faltan bultos por escanear. Resolvé las diferencias con el depósito antes de salir.");
            Jornada = await datos.ModificarAsync(Cuenta, Jornada!.Id, j => j.ConfirmarCarga(DateTimeOffset.UtcNow));
            await IntentarSincronizar(); Mensaje = "Confirmación de carga guardada. " + Resumen;
        }), () => !Ocupado && MostrarCarga);
        Sincronizar = new(() => Ejecutar(async () => { await IntentarSincronizar(); Mensaje = Resumen; }), () => !Ocupado && Jornada is not null && sincronizador is not null);
        Rendir = new(() => Ejecutar(async () =>
        {
            var seleccionados = await interaccion.SeleccionarRendicionAsync(BultosParaRendir()); if (seleccionados is null) return;
            if (!await verificacion.VerificarAsync("Confirmar la declaración de bultos devueltos al depósito")) { Mensaje = "La rendición no fue autorizada."; return; }
            Jornada = await datos.ModificarAsync(Cuenta, Jornada!.Id, j => j.DeclararRendicion(Elemento(new() { BultoIds = seleccionados }), DateTimeOffset.UtcNow));
            await IntentarSincronizar(); Mensaje = "Rendición guardada; falta la recepción física del depósito. " + Resumen;
        }), () => !Ocupado && MostrarRendicion);
        Finalizar = new(() => Ejecutar(async () =>
        {
            if (BultosParaRendir().Count > 0) throw new InvalidOperationException("Declará los bultos que devolvés antes de cerrar la jornada.");
            if ((await datos.LeerCapturasAsync(Cuenta, Jornada!.Id)).Any(x => x.OperacionId is null))
                throw new InvalidOperationException("Confirmá o descartá explícitamente los borradores de evidencia antes de cerrar.");
            var pendientes = Hoja.Paradas.Any(p => EntregaFisica(p.EnvioId) && Estado(p) != "Entregado");
            if (!await interaccion.ConfirmarCierreAsync(pendientes)) return;
            Jornada = await datos.ModificarAsync(Cuenta, Jornada.Id, j => j.Finalizar(DateTimeOffset.UtcNow));
            seguimiento.Detener();
            await IntentarSincronizar(); Mensaje = "Jornada terminada en este teléfono. " + Resumen;
        }), () => !Ocupado && MostrarCierre);
    }
    public async Task InicializarAsync() => await Ejecutar(async () =>
    {
        await ConsultarCambios();
        await Refrescar();
        if (Iniciada && !Cerrada) _ = await seguimiento.IniciarAsync();
    });
    public async Task Refrescar()
    {
        var bytesRemotos = await datos.LeerVistaRemotaAsync(Cuenta, Hoja.Id);
        vistaRemota = bytesRemotos is null ? null : SerializadorHojaRuta.Deserializar(bytesRemotos, Cuenta);
        Jornada = await datos.LeerAsync(Cuenta, Hoja.JornadaId); confirmaciones.Clear();
        if (Jornada is not null)
            foreach (var bytes in await datos.LeerConfirmacionesAsync(Cuenta, Jornada.Id))
            {
                var confirmada = JsonSerializer.Deserialize(bytes, ContratosRepartoJson.Default.ConfirmacionOperacion)!;
                confirmaciones[confirmada.Id] = confirmada;
            }
        Paradas.Clear();
        foreach (var p in Hoja.Paradas.OrderBy(x => x.Orden))
            Paradas.Add(new(p.EnvioId, p.Titulo, p.Direccion, EntregaFisica(p.EnvioId) && Estado(p) is not ("Entregado" or "EntregaPendienteValidacion" or "EntregaSinValidacion")
                ? "Entrega física declarada · pendiente o en conflicto" : !ParadaActiva(p) && Estado(p) is not ("Entregado" or "EntregaPendienteValidacion" or "EntregaSinValidacion")
                    ? "Fuera de esta ruta · devolver en rendición" : Estado(p), $"{p.Bultos.Length} bulto(s)"));
        Actualizar();
    }
    public bool EntregaFisica(Guid envio) => Jornada?.Operaciones.Any(o => o.EnvioId == envio && o.Tipo == TipoOperacionLocal.EntregaFisica) ?? false;
    public string Estado(ParadaRuta parada) => VistaParada.Obtener(parada, vistaRemota, confirmaciones.Values).Estado;
    public bool ParadaActiva(ParadaRuta parada) => VistaParada.Obtener(parada, vistaRemota, confirmaciones.Values).Activa;
    private async Task ConsultarCambios()
    {
        if (avisos is null) return;
        try { await avisos.ActualizarRutaAsync(Cuenta, Hoja.Id); }
        catch (Exception ex) when (ex is HttpRequestException or SesionMovilException or OperationCanceledException)
        { /* La hoja y los comandos originales siguen disponibles sin red. */ }
    }
    private bool Registrada(TipoOperacionLocal tipo) => Jornada?.Operaciones.Any(o => o.Tipo == tipo && (!confirmaciones.TryGetValue(o.Id, out var r) || r.Resultado == "Aplicada")) ?? false;
    private HashSet<Guid> Cargados()
    {
        var cargados = new HashSet<Guid>();
        if (Jornada is null) return cargados;
        foreach (var op in Jornada.Operaciones)
        {
            if (confirmaciones.TryGetValue(op.Id, out var r) && r.Resultado != "Aplicada") continue;
            if (op.Tipo == TipoOperacionLocal.CargarBulto && op.Datos.TryGetProperty("bultoId", out var bulto) && bulto.TryGetGuid(out var id)) cargados.Add(id);
            if (op.Tipo == TipoOperacionLocal.Rendicion && op.Datos.TryGetProperty("bultoIds", out var ids))
                foreach (var valor in ids.EnumerateArray()) cargados.Remove(valor.GetGuid());
        }
        return cargados;
    }
    public IReadOnlyList<BultoRuta> BultosParaRendir()
    {
        var cargados = Cargados();
        foreach (var p in Hoja.Paradas.Where(p => EntregaFisica(p.EnvioId))) foreach (var b in p.Bultos) cargados.Remove(b.Id);
        if (Jornada is not null)
            foreach (var op in Jornada.Operaciones.Where(o => o.Tipo == TipoOperacionLocal.Rendicion
                && (!confirmaciones.TryGetValue(o.Id, out var r) || r.Resultado == "Aplicada")))
                if (op.Datos.TryGetProperty("bultoIds", out var ids)) foreach (var id in ids.EnumerateArray()) cargados.Remove(id.GetGuid());
        return Hoja.Paradas.SelectMany(x => x.Bultos).Where(x => cargados.Contains(x.Id)).ToArray();
    }
    public async Task IntentarSincronizar()
    {
        if (sincronizador is null || Jornada is null) return;
        try { await sincronizador.SincronizarAsync(Cuenta, Jornada.Id); }
        catch (Exception ex) when (ex is HttpRequestException or SesionMovilException or OperationCanceledException) { Mensaje = "Se conserva el trabajo pendiente de sincronizar. Reingresá con la misma cuenta si se solicita."; }
        await ConsultarCambios();
        await Refrescar();
    }
    public async Task Ejecutar(Func<Task> operacion)
    {
        if (Ocupado) return; Ocupado = true;
        try { await operacion(); }
        catch (InvalidOperationException ex) { Mensaje = ex.Message; }
        catch (SesionMovilException ex) { Mensaje = ex.Message; }
        catch (HttpRequestException) { Mensaje = "No se pudo conectar. El trabajo guardado se conserva."; }
        catch (Exception) { Mensaje = "No se completó la operación. Se conserva el trabajo guardado para recuperarlo."; }
        finally { await RefrescarSeguro(); Ocupado = false; }
    }
    private async Task RefrescarSeguro() { try { await Refrescar(); } catch (Exception) { Mensaje = "No se pudo leer el estado local. Los archivos existentes se conservan."; } }
    public static JsonElement Elemento(DatosOperacionMovil datos) => JsonSerializer.SerializeToElement(datos, DatosOperacionJson.Default.DatosOperacionMovil);
    private void Actualizar()
    {
        foreach (var propiedad in new[] { nameof(Hoja), nameof(Jornada), nameof(Ocupado), nameof(Iniciada), nameof(Cerrada), nameof(Salida), nameof(MostrarInicio), nameof(MostrarCarga), nameof(MostrarCierre), nameof(MostrarRendicion), nameof(Resumen) }) Cambiar(propiedad);
        Iniciar?.Actualizar(); Escanear?.Actualizar(); ConfirmarCarga?.Actualizar(); Sincronizar?.Actualizar(); Rendir?.Actualizar(); Finalizar?.Actualizar();
    }
    private void Cambiar([CallerMemberName] string? nombre = null) => PropertyChanged?.Invoke(this, new(nombre));
}
