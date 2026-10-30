using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using NexoRuta.Mobile.Api.Identidad;
using NexoRuta.Mobile.Api.Reparto;
using NexoRuta.Mobile.Core.Identidad;
using NexoRuta.Mobile.Core.Jornadas;
using NexoRuta.Mobile.Core.Rutas;
using NexoRuta.Mobile.Storage;
using NexoRuta.Mobile.Application;
using NexoRuta.Mobile.Ubicacion;
using System.Text.Json;
namespace NexoRuta.Mobile.Presentacion;

public sealed class PrincipalViewModel : INotifyPropertyChanged
{
    private readonly ClienteSesionMovil? cliente;
    private readonly RepositorioJornadasSqlite datos;
    private readonly IVerificacionLocal verificacion;
    private readonly Notificaciones.ControlNotificaciones notificaciones;
    private readonly ITrackingJornada seguimiento;
    private readonly ServicioHojasRuta? hojasServicio;
    private bool actualizando, consultaHojaExitosa;
    private bool puedeContinuarSinConexion;
    public bool Actualizando { get => actualizando; private set { actualizando = value; Cambiar(); Descargar?.Actualizar(); } }
    private CuentaMovil? cuenta;
    private bool ocupado, desbloqueada;
    private string mensaje = "", email = "", password = "";
    private DateTime fecha = DateTime.Today;
    public ObservableCollection<VistaHoja> Hojas { get; } = [];
    public event PropertyChangedEventHandler? PropertyChanged;
    public bool Ocupado { get => ocupado; private set { ocupado = value; Cambiar(); Actualizar(); } }
    public bool Desbloqueada { get => desbloqueada; private set { desbloqueada = value; Cambiar(); Actualizar(); } }
    public bool MostrarIngreso => !Desbloqueada;
    public bool PuedeOperar => Desbloqueada && !Ocupado;
    public bool MostrarTituloHoja => string.IsNullOrWhiteSpace(Mensaje);
    public bool MostrarMensajeHoja => !MostrarTituloHoja;
    public bool MostrarSinRuta => Desbloqueada && consultaHojaExitosa && Hojas.Count == 0;
    public bool TieneCuenta => cuenta is not null;
    public bool PuedeContinuarSinConexion => cuenta is not null && puedeContinuarSinConexion;
    public bool ServicioConfigurado => cliente is not null;
    public string Mensaje { get => mensaje; private set { mensaje = value; Cambiar(); Cambiar(nameof(MostrarTituloHoja)); Cambiar(nameof(MostrarMensajeHoja)); } }
    public string Email { get => email; set { email = value; Cambiar(); } }
    public string Password { get => password; set { password = value; Cambiar(); } }
    public DateTime Fecha
    {
        get => fecha;
        set
        {
            if (fecha == value) return;
            fecha = value; consultaHojaExitosa = false;
            Cambiar(); Cambiar(nameof(MostrarSinRuta));
        }
    }
    public ComandoAsincrono Ingresar { get; }
    public ComandoAsincrono Desbloquear { get; }
    public ComandoAsincrono Descargar { get; }
    public ComandoAsincrono ActivarAvisos { get; }
    public ComandoAsincrono Reingresar { get; }
    public ComandoAsincrono CerrarSesion { get; }
    public string EstadoAvisos => notificaciones.Estado;
    public bool PuedeActivarAvisos => notificaciones.PuedeActivar;

    public PrincipalViewModel(ConfiguracionAplicacion config, IAlmacenSesionMovil almacen,
        RepositorioJornadasSqlite datos, IVerificacionLocal verificacion, Notificaciones.ControlNotificaciones notificaciones,
        ITrackingJornada seguimiento, ClienteSesionMovil? cliente = null, ServicioSesionTrabajo? salida = null, ServicioHojasRuta? hojasServicio = null)
    {
        this.datos = datos; this.verificacion = verificacion; this.hojasServicio = hojasServicio;
        this.cliente = cliente;
        this.notificaciones = notificaciones;
        this.seguimiento = seguimiento;
        CerrarSesion = new(() => Ejecutar(async () =>
        {
            try { await salida!.CerrarAsync(Cuenta); }
            catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException)
            {
                notificaciones.SalidaIncompleta(); Cambiar(nameof(EstadoAvisos));
                throw new SesionMovilException(ErrorSesionMovil.CierreNoConfirmado);
            }
            catch { notificaciones.SalidaIncompleta(); Cambiar(nameof(EstadoAvisos)); throw; }
            cuenta = null; Email = ""; puedeContinuarSinConexion = false; Bloquear();
            try { seguimiento.Detener(); } catch { /* Sin cuenta, el callback GPS tampoco admite nuevos registros. */ }
            try { notificaciones.SesionCerrada(); } catch { /* No se restaura una sesión ya revocada si falla la limpieza nativa. */ }
            Cambiar(nameof(EstadoAvisos));
            Mensaje = "Sesión cerrada.";
            Actualizar();
        }), () => Desbloqueada && !Ocupado && salida is not null);
        Reingresar = new(() =>
        {
            Bloquear();
            Mensaje = "Ingresá con esta misma cuenta para recuperar la sesión del servidor. Tu trabajo local se conserva.";
            return Task.CompletedTask;
        }, () => Desbloqueada && !Ocupado);
        ActivarAvisos = new(() => Ejecutar(async () =>
        {
            await notificaciones.ActivarAsync(); Cambiar(nameof(EstadoAvisos));
        }), () => Desbloqueada && ServicioConfigurado && !Ocupado);
        Ingresar = new(() => Ejecutar(async () =>
        {
            if (string.IsNullOrWhiteSpace(Email) || string.IsNullOrEmpty(Password)) { Mensaje = "Ingresá tu correo y contraseña."; return; }
            var clave = Password; Password = "";
            var identidadAnterior = cuenta ?? await cliente!.ObtenerCuentaLocalAsync();
            var permitirCambioDeIdentidad = false;
            if (identidadAnterior is not null)
            {
                var pendientes = await datos.TienePendientesAsync(identidadAnterior);
                var jornadas = await datos.LeerJornadasAsync(identidadAnterior);
                var hojas = await datos.LeerHojasAsync(identidadAnterior);
                permitirCambioDeIdentidad = !pendientes && jornadas.Count == 0 && hojas.Count == 0;
                RegistrarDiagnostico($"session.login.local_replacement_allowed={permitirCambioDeIdentidad.ToString().ToLowerInvariant()}");
            }
            var identidadAutenticada = await cliente!.IngresarAsync(Email, clave, permitirCambioDeIdentidad);
            cuenta = identidadAutenticada;
            await cliente.ExigirPerfilAsync();
            await datos.ReanudarCuentaAsync(cuenta);
            Desbloqueada = true; await CargarLocales();
            Mensaje = identidadAnterior is not null && identidadAnterior != identidadAutenticada
                ? "Acceso recuperado. Los datos locales de la identidad anterior permanecen separados y no se transfirieron."
                : "";
        }), () => cliente is not null && !Ocupado);
        Desbloquear = new(() => Ejecutar(async () =>
        {
            if (await verificacion.VerificarAsync("Abrir tu jornada de reparto"))
            {
                Desbloqueada = true; await CargarLocales();
                Mensaje = cliente is not null && !await cliente.TieneTokensGuardadosAsync()
                    ? "Modo sin conexión: solo se muestran datos guardados. Ingresá para actualizar la hoja."
                    : "";
            }
            else Mensaje = "No se verificó la identidad. Podés volver a intentar.";
        }), () => PuedeContinuarSinConexion && !Ocupado);
        Descargar = new(() => ActualizarHojaAsync(informar: true), () => cliente is not null && Desbloqueada && !Ocupado && !Actualizando);
        if (cliente is null) Mensaje = "El servicio aún no está configurado. Contactá al administrador.";
    }
    public async Task InicializarAsync() => await Ejecutar(async () =>
    {
        cuenta = cliente is null ? null : await cliente.ObtenerCuentaLocalAsync();
        RegistrarDiagnostico(cuenta is null ? "session.local_account=absent" : "session.local_account=present");
        if (cuenta is not null)
        {
            try
            {
                var pendientes = await datos.TienePendientesAsync(cuenta);
                var jornadas = await datos.LeerJornadasAsync(cuenta);
                var hojas = await datos.LeerHojasAsync(cuenta);
                var tokens = cliente is not null && await cliente.TieneTokensGuardadosAsync();
                puedeContinuarSinConexion = pendientes || jornadas.Count > 0 || hojas.Count > 0 || tokens;
                RegistrarDiagnostico($"local_work.pending={pendientes}");
                RegistrarDiagnostico($"local_work.journeys={jornadas.Count}");
                RegistrarDiagnostico($"local_work.cached_sheets={hojas.Count}");
                RegistrarDiagnostico($"session.local_tokens_present={tokens.ToString().ToLowerInvariant()}");
#if ANDROID && DEBUG
                await RegistrarResumenJornadasAsync(cuenta, jornadas, hojas);
#endif
            }
            catch (Exception ex)
            {
                puedeContinuarSinConexion = false;
                RegistrarDiagnostico($"local_work.inspect_error={ex.GetType().Name}");
            }
        }
        Actualizar();
        if (cuenta is not null) Mensaje = "";
    });
    public Task RefrescarLocalesAsync() => Desbloqueada && !Ocupado ? Ejecutar(CargarLocales) : Task.CompletedTask;
    public async Task ActualizarHojaAsync(bool informar = false)
    {
        if (!Desbloqueada || Ocupado || Actualizando || hojasServicio is null) return;
        if (cliente is null || !await cliente.TieneTokensGuardadosAsync())
        {
            consultaHojaExitosa = false; Cambiar(nameof(MostrarSinRuta));
            Mensaje = "La sesión del servidor requiere ingreso. No se consultaron rutas; los datos guardados siguen disponibles.";
            return;
        }
        var esperada = Cuenta; Actualizando = true;
        consultaHojaExitosa = false; Cambiar(nameof(MostrarSinRuta));
        try
        {
            using var limite = new CancellationTokenSource(TimeSpan.FromSeconds(12));
            if (Connectivity.Current.NetworkAccess == NetworkAccess.Internet)
            {
                await hojasServicio.ActualizarAsync(esperada, DateOnly.FromDateTime(Fecha), limite.Token);
                consultaHojaExitosa = true;
            }
            else Mensaje = "Sin conexión: no consultamos rutas para esta fecha. Si hay una hoja guardada, sigue disponible.";
            if (Desbloqueada && cuenta == esperada)
            {
                await CargarLocales();
                if (Connectivity.Current.NetworkAccess == NetworkAccess.Internet)
                    Mensaje = informar ? "Hoja actualizada." : "";
            }
        }
        catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException)
        {
            consultaHojaExitosa = false;
            if (cuenta == esperada) Mensaje = "No pudimos consultar rutas para esta fecha. Revisá la conexión e intentá nuevamente.";
        }
        catch (SesionMovilException ex)
        {
            consultaHojaExitosa = false;
            if (cuenta == esperada) Mensaje = ex.Message;
        }
        catch (Exception)
        {
            consultaHojaExitosa = false;
            if (cuenta == esperada) Mensaje = "No pudimos consultar rutas para esta fecha. Volvé a intentarlo.";
        }
        finally { Actualizando = false; Cambiar(nameof(MostrarSinRuta)); }
    }
    public void Bloquear()
    {
        Desbloqueada = false; consultaHojaExitosa = false; Password = ""; Hojas.Clear();
        Cambiar(nameof(MostrarSinRuta));
        if (cuenta is not null) Mensaje = "";
    }
    public CuentaMovil Cuenta => cuenta ?? throw new InvalidOperationException("No hay una cuenta local.");
    private async Task CargarLocales()
    {
        var esperada = Cuenta;
        var fechaEsperada = Fecha;
        var hojas = await datos.LeerHojasAsync(esperada);
        var jornadas = await datos.LeerJornadasAsync(esperada);
        var vistas = new List<VistaHoja>();
        foreach (var original in hojas.Select(bytes => SerializadorHojaRuta.Deserializar(bytes, esperada))
            .Where(h => h.Fecha == DateOnly.FromDateTime(fechaEsperada)).OrderByDescending(h => h.Version))
        {
            var actual = await datos.LeerVistaRemotaAsync(esperada, original.Id);
            var remota = actual is null ? null : SerializadorHojaRuta.Deserializar(actual, esperada);
            var jornada = jornadas.SingleOrDefault(j => j.RutaId == original.JornadaId);
            var recibos = new Dictionary<Guid, string>();
            if (jornada is not null)
                foreach (var bytes in await datos.LeerConfirmacionesAsync(esperada, jornada.Id))
                {
                    var recibo = JsonSerializer.Deserialize(bytes, ContratosRepartoJson.Default.ConfirmacionOperacion);
                    if (recibo is not null) recibos[recibo.Id] = recibo.Resultado;
                }
            var indicador = EstadoJornadaIndicador.Crear(jornada, recibos, remota?.Estado ?? original.Estado);
            vistas.Add(VistaHoja.Crear(original, remota, jornada is null ? null : indicador));
        }
        if (!Desbloqueada || cuenta != esperada || Fecha != fechaEsperada) return;
        for (var destino = 0; destino < vistas.Count; destino++)
        {
            var siguiente = vistas[destino];
            if (destino < Hojas.Count && MismoContenidoVisible(Hojas[destino], siguiente)) continue;
            var existente = -1;
            for (var i = destino + 1; i < Hojas.Count; i++)
                if (Hojas[i].Original.Id == siguiente.Original.Id) { existente = i; break; }
            if (existente >= 0) Hojas.Move(existente, destino);
            else if (destino < Hojas.Count) Hojas[destino] = siguiente;
            else Hojas.Add(siguiente);
        }
        while (Hojas.Count > vistas.Count) Hojas.RemoveAt(Hojas.Count - 1);
        Cambiar(nameof(MostrarSinRuta));
    }
    private static bool MismoContenidoVisible(VistaHoja actual, VistaHoja siguiente) =>
        actual.Original.Id == siguiente.Original.Id && actual.Estado == siguiente.Estado &&
        actual.Fecha == siguiente.Fecha && actual.Matricula == siguiente.Matricula &&
        actual.Paradas == siguiente.Paradas && actual.Bultos == siguiente.Bultos &&
        actual.FaseJornada == siguiente.FaseJornada && actual.EstadoSincronizacion == siguiente.EstadoSincronizacion &&
        actual.TieneJornada == siguiente.TieneJornada;
    private async Task Ejecutar(Func<Task> accion)
    {
        Ocupado = true;
        try { await accion(); }
        catch (SesionMovilException ex) { Mensaje = ex.Message; }
        catch (HttpRequestException) { Mensaje = "No pudimos conectar. Podés continuar con tu hoja de ruta."; }
        catch (OperationCanceledException) { Mensaje = "La acción se interrumpió. Volvé a intentarlo."; }
        catch (InvalidOperationException ex) { Mensaje = ex.Message; }
        catch (Exception) { Mensaje = "No pudimos completar la acción. Si el problema continúa, contactá a tu operador."; }
        finally { Ocupado = false; }
    }
    private void Actualizar()
    {
        Cambiar(nameof(MostrarIngreso)); Cambiar(nameof(TieneCuenta)); Cambiar(nameof(PuedeContinuarSinConexion)); Cambiar(nameof(PuedeOperar));
        Ingresar?.Actualizar(); Desbloquear?.Actualizar(); Descargar?.Actualizar(); ActivarAvisos?.Actualizar(); Reingresar?.Actualizar();
        CerrarSesion?.Actualizar();
    }
    private void Cambiar([CallerMemberName] string? nombre = null) => PropertyChanged?.Invoke(this, new(nombre));

    private static void RegistrarDiagnostico(string evento)
    {
#if ANDROID && DEBUG
        Android.Util.Log.Info("NexoRuta.Session", evento);
#endif
    }

#if ANDROID && DEBUG
    private async Task RegistrarResumenJornadasAsync(CuentaMovil cuenta, IReadOnlyList<JornadaLocal> jornadas, IReadOnlyList<byte[]> hojas)
    {
        var rutas = hojas.Select(bytes => SerializadorHojaRuta.Deserializar(bytes, cuenta)).ToDictionary(x => x.Id);
        foreach (var jornada in jornadas)
        {
            var confirmaciones = (await datos.LeerConfirmacionesAsync(cuenta, jornada.Id))
                .Select(bytes => JsonSerializer.Deserialize(bytes, ContratosRepartoJson.Default.ConfirmacionOperacion))
                .Where(x => x is not null).Cast<ConfirmacionOperacion>().ToArray();
            var ruta = rutas.GetValueOrDefault(jornada.RutaId);
            var operaciones = string.Join("|", jornada.Operaciones.Select(x =>
                $"{x.Id:D}:{x.Secuencia}:{x.Tipo}:confirmed={jornada.ExportarEstado().Confirmadas.Contains(x.Id)}"));
            var recibos = string.Join("|", confirmaciones.Select(x =>
                $"{x.Id:D}:{x.Secuencia}:{x.Resultado}:detail={x.Detalle ?? "-"}"));
            RegistrarDiagnostico($"local_work.journey.id={jornada.Id:D};route_id={jornada.RutaId:D};date={ruta?.Fecha.ToString("yyyy-MM-dd") ?? "uncached"};route_state={ruta?.Estado ?? "uncached"};local_closed={jornada.EstaCerrada};local_pending={jornada.TienePendientes};operations={operaciones};receipts={recibos}");
        }
    }
#endif
}
