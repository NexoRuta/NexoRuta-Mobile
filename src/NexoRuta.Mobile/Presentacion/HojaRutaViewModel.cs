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
    public string EstadoMapa { get; private set; } = "Mapa offline pospuesto; operación online.";
    private readonly Dictionary<Guid, ConfirmacionOperacion> confirmaciones = [];
    private bool ocupado;
    private string mensaje = "";
    public HojaRuta Hoja { get; private set; }
    public CuentaMovil Cuenta { get; }
    public JornadaLocal? Jornada { get; private set; }
    public ObservableCollection<ParadaResumen> Paradas { get; } = [];
    public ObservableCollection<BultoCargaResumen> Carga { get; } = [];
    private int Entregadas => Hoja.Paradas.Count(p => EntregaFisica(p.EnvioId) || Estado(p) is "Entregado" or "EntregaPendienteValidacion" or "EntregaSinValidacion");
    public string Etapa => Cerrada ? "JORNADA FINALIZADA" : !Iniciada ? "ANTES DE SALIR" : !Salida ? "CARGA EN DEPÓSITO" : "EN REPARTO";
    public string Indicacion => Cerrada ? "Tu recorrido terminó. Gracias por el trabajo de hoy." : !Iniciada ? "Revisá tus paradas e iniciá la jornada en el depósito." : !Salida ? "Escaneá los bultos asignados antes de comenzar el reparto." : "Elegí tu próxima parada para registrar la entrega.";
    public string CantidadParadas => $"{Hoja.Paradas.Length} en tu recorrido";
    public string EstadoInformativo => !string.IsNullOrWhiteSpace(IncidenciasCierre) ? IncidenciasCierre
        : !string.IsNullOrWhiteSpace(Mensaje) ? Mensaje : Indicacion;
    public string ResumenCarga => $"{Carga.Count(b => b.Cargado)} de {Carga.Count} bultos verificados";
    public double Progreso => Hoja.Paradas.Length == 0 ? 0 : (double)Entregadas / Hoja.Paradas.Length;
    public event PropertyChangedEventHandler? PropertyChanged;
    public bool Ocupado { get => ocupado; private set { ocupado = value; Actualizar(); } }
    public string Mensaje { get => mensaje; private set { mensaje = value; Cambiar(); Cambiar(nameof(EstadoInformativo)); } }
    public bool Iniciada => Jornada is { } j && confirmaciones.TryGetValue(j.Operaciones[0].Id, out var inicio) && inicio.Resultado == "Aplicada";
    public bool Cerrada => Jornada?.EstaCerrada ?? false;
    public bool Salida => Registrada(TipoOperacionLocal.ConfirmarCarga);
    public bool MostrarInicio => !Iniciada && !Cerrada && Hoja.Estado == "Despachada" && vistaRemota?.Estado is not ("EnCurso" or "Finalizada");
    public bool MostrarCarga => Iniciada && !Salida && !Cerrada;
    public bool MostrarRendicion => Iniciada && !Cerrada && BultosParaRendir().Count > 0;
    public bool MostrarCierre => Iniciada && !Cerrada;
    public string IncidenciasCierre => (vistaRemota ?? Hoja).IncidenciasCierre is { Length: > 0 } incidencias
        ? "Cierre con incidencia por bultos sin entrega ni rendición: " + string.Join(", ", incidencias.Select(i => i.Codigo))
            + ". Se conserva el historial y la custodia hasta la recepción física en depósito." : "";
    public string Resumen => Iniciada || Cerrada ? $"{Entregadas} de {Hoja.Paradas.Length} entregas registradas"
        : $"{Hoja.Paradas.Length} paradas · {TextoReparto.Bultos(Hoja.Paradas.Sum(p => p.Bultos.Length))}";
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
            Mensaje = await seguimiento.IniciarAsync() ? "Jornada iniciada. Verificá tu carga para comenzar."
                : "Jornada iniciada. Activá la ubicación para registrar el recorrido.";
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
            await IntentarSincronizar(); Mensaje = "Bulto verificado.";
        }), () => !Ocupado && MostrarCarga);
        ConfirmarCarga = new(() => Ejecutar(async () =>
        {
            var asignados = Hoja.Paradas.Where(ParadaActiva).SelectMany(p => p.Bultos).Select(b => b.Id).ToArray();
            if (Hoja.Politica.BloquearDiscrepanciasCarga && asignados.Any(id => !Cargados().Contains(id)))
                throw new InvalidOperationException("Faltan bultos por escanear. Resolvé las diferencias con el depósito antes de salir.");
            Jornada = await datos.ModificarAsync(Cuenta, Jornada!.Id, j => j.ConfirmarCarga(DateTimeOffset.UtcNow));
            await IntentarSincronizar(); Mensaje = "Carga confirmada. ¡Buen reparto!";
        }), () => !Ocupado && MostrarCarga);
        Sincronizar = new(() => Ejecutar(async () => { await IntentarSincronizar(); Mensaje = "Recorrido actualizado."; }), () => !Ocupado && Jornada is not null && sincronizador is not null);
        Rendir = new(() => Ejecutar(async () =>
        {
            var seleccionados = await interaccion.SeleccionarRendicionAsync(BultosParaRendir()); if (seleccionados is null) return;
            if (!await verificacion.VerificarAsync("Confirmar la declaración de bultos devueltos al depósito")) { Mensaje = "La rendición no fue autorizada."; return; }
            Jornada = await datos.ModificarAsync(Cuenta, Jornada!.Id, j => j.DeclararRendicion(Elemento(new() { BultoIds = seleccionados }), DateTimeOffset.UtcNow));
            await IntentarSincronizar(); Mensaje = "Bultos declarados. El depósito confirmará la recepción.";
        }), () => !Ocupado && MostrarRendicion);
        Finalizar = new(() => Ejecutar(async () =>
        {
            if ((await datos.LeerCapturasAsync(Cuenta, Jornada!.Id)).Any(x => x.OperacionId is null))
                throw new InvalidOperationException("Confirmá o descartá explícitamente los borradores de evidencia antes de cerrar.");
            var pendientes = Hoja.Paradas.Any(p => EntregaFisica(p.EnvioId) && Estado(p) != "Entregado");
            var sinRendir = BultosParaRendir().Count;
            if (!await interaccion.ConfirmarCierreAsync(pendientes, sinRendir)) return;
            Jornada = await datos.ModificarAsync(Cuenta, Jornada.Id, j => j.Finalizar(DateTimeOffset.UtcNow));
            seguimiento.Detener();
            await IntentarSincronizar(); Mensaje = "Jornada finalizada. "
                + (sinRendir > 0 ? $"Cierre con incidencia por {sinRendir} bulto(s) sin entrega ni rendición; se conserva la custodia. " : "") + Resumen;
        }), () => !Ocupado && MostrarCierre);
    }
    public async Task InicializarAsync() => await Ejecutar(async () =>
    {
        // Hidratar desde SQLite primero permite abrir la ruta con contenido completo sin
        // esperar a la red ni a una consulta de mapa que no es necesaria para operar online.
        await Refrescar();
        if (Iniciada && !Cerrada) _ = await seguimiento.IniciarAsync();
    });

    public async Task ActualizarDesdeServidorAsync() => await Ejecutar(async () =>
    {
        await ConsultarCambios();
        await Refrescar();
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
        Reconciliar(Paradas, Hoja.Paradas.OrderBy(x => x.Orden)
            .Select(p => new ParadaResumen(p.EnvioId, p.Orden, p.Destinatario, p.Direccion, EstadoPresentado(p), TextoReparto.Bultos(p.Bultos.Length))).ToArray());
        var cargados = Cargados();
        Reconciliar(Carga, Hoja.Paradas.Where(ParadaActiva).SelectMany(p => p.Bultos)
            .Select(bulto => new BultoCargaResumen(bulto.Codigo, cargados.Contains(bulto.Id))).ToArray());
        Actualizar();
    }

    private static void Reconciliar<T>(ObservableCollection<T> actual, IReadOnlyList<T> deseado) where T : notnull
    {
        for (var destino = 0; destino < deseado.Count; destino++)
        {
            if (destino < actual.Count && EqualityComparer<T>.Default.Equals(actual[destino], deseado[destino])) continue;
            var existente = -1;
            for (var i = destino + 1; i < actual.Count; i++)
                if (EqualityComparer<T>.Default.Equals(actual[i], deseado[destino])) { existente = i; break; }
            if (existente >= 0) actual.Move(existente, destino);
            else if (destino < actual.Count) actual[destino] = deseado[destino];
            else actual.Add(deseado[destino]);
        }
        while (actual.Count > deseado.Count) actual.RemoveAt(actual.Count - 1);
    }
    public string EstadoPresentado(ParadaRuta parada) =>
        EntregaFisica(parada.EnvioId) && Estado(parada) is not ("Entregado" or "EntregaPendienteValidacion" or "EntregaSinValidacion")
            ? "Entrega registrada · en revisión"
            : !ParadaActiva(parada) && Estado(parada) is not ("Entregado" or "EntregaPendienteValidacion" or "EntregaSinValidacion")
                ? VistaParada.DescribirSuspension(parada, vistaRemota, Cargados()) : Estado(parada);
    public bool EntregaFisica(Guid envio) => Jornada?.Operaciones.Any(o => o.EnvioId == envio && o.Tipo == TipoOperacionLocal.EntregaFisica) ?? false;
    public string Estado(ParadaRuta parada) => VistaParada.Obtener(parada, vistaRemota, confirmaciones.Values).Estado;
    public bool ParadaActiva(ParadaRuta parada) => VistaParada.Obtener(parada, vistaRemota, confirmaciones.Values).Activa;
    private async Task ConsultarCambios()
    {
        if (avisos is null || Connectivity.Current.NetworkAccess != NetworkAccess.Internet) return;
        using var limite = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        try { await avisos.ActualizarRutaAsync(Cuenta, Hoja.Id, limite.Token); }
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
        catch (Exception ex) when (ex is HttpRequestException or SesionMovilException or OperationCanceledException) { Mensaje = "Tus cambios están guardados. Se enviarán al recuperar la conexión."; }
        await ConsultarCambios();
        await Refrescar();
    }
    public async Task Ejecutar(Func<Task> operacion)
    {
        if (Ocupado) return; Ocupado = true;
        try { await operacion(); }
        catch (InvalidOperationException ex) { Mensaje = ex.Message; }
        catch (SesionMovilException ex) { Mensaje = ex.Message; }
        catch (HttpRequestException) { Mensaje = "No pudimos actualizar. Podés continuar con tu recorrido."; }
        catch (Exception) { Mensaje = "No pudimos completar la acción. Volvé a intentarlo."; }
        finally { await RefrescarSeguro(); Ocupado = false; }
    }
    private async Task RefrescarSeguro() { try { await Refrescar(); } catch (Exception) { Mensaje = "No pudimos cargar tu recorrido. Volvé a intentarlo."; } }
    public static JsonElement Elemento(DatosOperacionMovil datos) => JsonSerializer.SerializeToElement(datos, DatosOperacionJson.Default.DatosOperacionMovil);
    private void Actualizar()
    {
        foreach (var propiedad in new[] { nameof(Hoja), nameof(Jornada), nameof(Ocupado), nameof(Iniciada), nameof(Cerrada), nameof(Salida), nameof(MostrarInicio), nameof(MostrarCarga), nameof(MostrarCierre), nameof(MostrarRendicion), nameof(Resumen), nameof(Etapa), nameof(Indicacion), nameof(CantidadParadas), nameof(ResumenCarga), nameof(Progreso), nameof(IncidenciasCierre) }) Cambiar(propiedad);
        Iniciar?.Actualizar(); Escanear?.Actualizar(); ConfirmarCarga?.Actualizar(); Sincronizar?.Actualizar(); Rendir?.Actualizar(); Finalizar?.Actualizar();
    }
    private void Cambiar([CallerMemberName] string? nombre = null) => PropertyChanged?.Invoke(this, new(nombre));
}
