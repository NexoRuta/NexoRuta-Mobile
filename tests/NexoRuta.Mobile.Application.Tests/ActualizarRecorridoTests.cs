using System.Net;
using System.Text.Json;
using NexoRuta.Mobile.Api.Reparto;
using NexoRuta.Mobile.Captura;
using NexoRuta.Mobile.Core.Identidad;
using NexoRuta.Mobile.Core.Rutas;
using NexoRuta.Mobile.Presentacion;
using NexoRuta.Mobile.Ubicacion;

namespace NexoRuta.Mobile.Application.Tests
{
    public sealed partial class SincronizadorJornadaTests
    {
        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task ServerEnCursoWithOnlyStartReceiptKeepsLoadingVisibleAndDeliveryDisabled(bool blockIncompleteLoad)
        {
            using var f = new Escenario(); await f.Preparar(cerrar: false, soloInicio: true, estadoServidor: "EnCurso", bloquearDiscrepanciasCarga: blockIncompleteLoad);
            using var cliente = f.Cliente(async (request, ct) => Confirmar(
                JsonSerializer.Deserialize(await request.Content!.ReadAsByteArrayAsync(ct), ContratosRepartoJson.Default.SobreOperacion)!, f.Envio, f.Bulto));
            using var sync = new SincronizadorJornada(cliente, f.Datos, f.Archivos);
            await sync.SincronizarAsync(f.Cuenta, f.Jornada.Id);
            var hoja = SerializadorHojaRuta.Deserializar((await f.Datos.LeerHojaAsync(f.Cuenta, f.Jornada.RutaId))!, f.Cuenta);
            var vm = new HojaRutaViewModel(hoja, f.Cuenta, f.Datos, new UnusedInteraction(), new UnusedInteraction(), new UnusedInteraction(), cliente, sync);
            await vm.Refrescar();
            Assert.True(vm.Iniciada);
            Assert.False(vm.Salida);
            Assert.True(vm.MostrarCarga);
            Assert.Equal(!blockIncompleteLoad, vm.PuedeConfirmarCarga); // Preserve explicit incomplete-load policy.
            var stop = new EntregaViewModel(vm, hoja.Paradas[0], f.Datos, new EvidenciasLocales(f.Datos, f.Archivos), new UnusedInteraction());
            Assert.False(stop.PuedeRegistrar);
            Assert.False(stop.Entregar.CanExecute(null));
            Assert.False(stop.Intento.CanExecute(null));
            Assert.Single((await f.Datos.LeerAsync(f.Cuenta, f.Jornada.Id))!.Operaciones);
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task RefreshKeepsContentInteractiveAndPendingClosureOnFailureThenRetriesSameOperations(bool timeout)
        {
            using var f = new Escenario(); await f.Preparar();
            var initial = (await f.Datos.LeerAsync(f.Cuenta, f.Jornada.Id))!;
            var hoja = SerializadorHojaRuta.Deserializar((await f.Datos.LeerHojaAsync(f.Cuenta, initial.RutaId))!, f.Cuenta);
            var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var fail = true;
            var requests = 0;
            using var cliente = f.Cliente(async (request, ct) =>
            {
                Interlocked.Increment(ref requests);
                if (fail)
                {
                    started.TrySetResult(); await release.Task.WaitAsync(ct);
                    if (timeout) throw new OperationCanceledException("Controlled timeout", ct);
                    throw new HttpRequestException("Controlled disconnection");
                }
                var command = JsonSerializer.Deserialize(await request.Content!.ReadAsByteArrayAsync(ct), ContratosRepartoJson.Default.SobreOperacion)!;
                return Confirmar(command, f.Envio, f.Bulto);
            });
            using var sync = new SincronizadorJornada(cliente, f.Datos, f.Archivos);
            var vm = new HojaRutaViewModel(hoja, f.Cuenta, f.Datos, new UnusedInteraction(), new UnusedInteraction(), new UnusedInteraction(), cliente, sync);
            await vm.Refrescar();
            var completed = Completion(vm);
            vm.Sincronizar.Execute(null);
            await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.True(vm.Sincronizando);
            Assert.False(vm.Ocupado); // The actual overlay binding must remain off during refresh.
            Assert.NotEmpty(vm.Paradas);
            Assert.False(vm.Cerrada);
            Assert.Contains("Sincronizando", vm.IndicadorSincronizacion);
            vm.Sincronizar.Execute(null); vm.Sincronizar.Execute(null);
            Assert.Equal(1, requests);
            release.TrySetResult(); await completed.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.False(vm.Ocupado);
            Assert.False(vm.Cerrada);
            Assert.Contains(timeout ? "tiempo" : "conectar", vm.Mensaje);
            Assert.DoesNotContain("Recorrido actualizado", vm.Mensaje);
            Assert.True(vm.Sincronizar.CanExecute(null));
            Assert.Equal(initial.Operaciones.Select(o => o.Id), (await f.Datos.LeerAsync(f.Cuenta, initial.Id))!.Operaciones.Select(o => o.Id));
            fail = false; completed = Completion(vm); vm.Sincronizar.Execute(null);
            await completed.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.True(vm.Cerrada);
            Assert.Contains("Cierre confirmado", vm.Mensaje);
            Assert.Equal(initial.Operaciones.Count, (await f.Datos.LeerConfirmacionesAsync(f.Cuenta, initial.Id)).Count);
            Assert.False((await f.Datos.LeerAsync(f.Cuenta, initial.Id))!.TienePendientes);
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task InvalidReceiptShowsRetryGuidanceWithoutExposingDiagnosticsOrConfirmingWork(bool refreshCommand)
        {
            using var f = new Escenario(); await f.Preparar(cerrar: false, soloInicio: true);
            var initial = (await f.Datos.LeerAsync(f.Cuenta, f.Jornada.Id))!;
            var hoja = SerializadorHojaRuta.Deserializar((await f.Datos.LeerHojaAsync(f.Cuenta, initial.RutaId))!, f.Cuenta);
            using var cliente = f.Cliente((request, _) => request.Method == HttpMethod.Post
                ? throw new InvalidDataException("Acuse=technical-id; secuencia=1; versionesAcuse=[NULL]")
                : throw new HttpRequestException("Controlled remote-read failure"));
            using var sync = new SincronizadorJornada(cliente, f.Datos, f.Archivos);
            var vm = new HojaRutaViewModel(hoja, f.Cuenta, f.Datos, new UnusedInteraction(), new UnusedInteraction(), new UnusedInteraction(), cliente, sync);
            await vm.Refrescar();
            if (refreshCommand)
            {
                var completed = Completion(vm); vm.Sincronizar.Execute(null);
                await completed.WaitAsync(TimeSpan.FromSeconds(5));
            }
            else Assert.False(await vm.IntentarSincronizar());
            Assert.Equal("No pudimos actualizar. Conservamos tus datos. Reintent\u00e1 con Actualizar.", vm.Mensaje);
            Assert.DoesNotContain("Acuse=", vm.Mensaje);
            Assert.False(vm.Cerrada);
            Assert.True(vm.Sincronizar.CanExecute(null));
            var preserved = (await f.Datos.LeerAsync(f.Cuenta, initial.Id))!;
            Assert.True(preserved.TienePendientes);
            Assert.Equal(initial.Operaciones.Select(x => x.Id), preserved.Operaciones.Select(x => x.Id));
            Assert.Empty(await f.Datos.LeerConfirmacionesAsync(f.Cuenta, initial.Id));
        }

        private static Task Completion(HojaRutaViewModel vm)
        {
            var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            EventHandler? handler = null;
            handler = (_, _) =>
            {
                if (!vm.Sincronizando && vm.Sincronizar.CanExecute(null))
                { vm.Sincronizar.CanExecuteChanged -= handler; done.TrySetResult(); }
            };
            vm.Sincronizar.CanExecuteChanged += handler;
            return done.Task;
        }

        [Fact]
        public async Task EmptyQueueDoesNotHideFailedRemoteRefreshOrLoseConfirmedClosure()
        {
            using var f = new Escenario(); await f.Preparar();
            using (var accepted = f.Cliente(async (request, ct) => Confirmar(
                JsonSerializer.Deserialize(await request.Content!.ReadAsByteArrayAsync(ct), ContratosRepartoJson.Default.SobreOperacion)!, f.Envio, f.Bulto)))
            using (var sync = new SincronizadorJornada(accepted, f.Datos, f.Archivos))
                await sync.SincronizarAsync(f.Cuenta, f.Jornada.Id);
            var hoja = SerializadorHojaRuta.Deserializar((await f.Datos.LeerHojaAsync(f.Cuenta, f.Jornada.RutaId))!, f.Cuenta);
            using var offline = f.Cliente((_, _) => throw new HttpRequestException("Controlled remote-read failure"));
            using var retry = new SincronizadorJornada(offline, f.Datos, f.Archivos);
            using var avisos = new ServicioAvisosReparto(offline, f.Datos);
            var vm = new HojaRutaViewModel(hoja, f.Cuenta, f.Datos, new UnusedInteraction(), new UnusedInteraction(), new UnusedInteraction(), offline, retry, avisos);
            await vm.Refrescar(); var completed = Completion(vm); vm.Sincronizar.Execute(null);
            await completed.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Contains("No pudimos consultar", vm.Mensaje);
            Assert.DoesNotContain("Recorrido actualizado", vm.Mensaje);
            Assert.True(vm.Cerrada); // The previous durable server confirmation remains authoritative.
            Assert.False(vm.Ocupado);
            Assert.True(vm.Sincronizar.CanExecute(null));
        }

        private sealed class UnusedInteraction : ITrackingJornada, IInteraccionReparto, IVerificacionLocal
        {
            public Task<bool> IniciarAsync() => throw new NotSupportedException();
            public void Detener() => throw new NotSupportedException();
            public Task<bool> VerificarAsync(string motivo, CancellationToken ct = default) => throw new NotSupportedException();
            public Task<string?> EscanearAsync() => throw new NotSupportedException();
            public Task<byte[]?> FotoAsync() => throw new NotSupportedException();
            public Task<byte[]?> FirmaAsync() => throw new NotSupportedException();
            public Task<Guid[]?> SeleccionarRendicionAsync(IReadOnlyList<BultoRuta> bultos) => throw new NotSupportedException();
            public Task<bool> ConfirmarSalidaConDiferenciasAsync(int bultosPendientes, int paradasAfectadas) => throw new NotSupportedException();
            public Task<bool> ConfirmarCierreAsync(bool pruebaPendiente, int bultosSinRendir) => throw new NotSupportedException();
        }
    }
}

// Native platform seams for host-side presentation tests, not Android map/GPS/TLS proof.
// Delivery remains disabled in the custody test; no native location request may occur.
namespace NexoRuta.Mobile.Presentacion
{
    internal enum GeolocationAccuracy { Best }
    internal sealed record GeolocationRequest(GeolocationAccuracy Accuracy, TimeSpan Timeout);
    internal sealed class Location
    {
        public double? Accuracy { get; init; }
        public double Latitude { get; init; }
        public double Longitude { get; init; }
        public DateTimeOffset Timestamp { get; init; }
    }
    internal sealed class Geolocation
    {
        public static Geolocation Default { get; } = new();
        public Task<Location?> GetLocationAsync(GeolocationRequest request, CancellationToken ct) => throw new NotSupportedException("Native GPS must not run in this guard test.");
    }
    internal enum NetworkAccess { Internet }
    internal static class Connectivity
    {
        internal sealed class Connection { public NetworkAccess NetworkAccess => NetworkAccess.Internet; }
        public static Connection Current { get; } = new();
    }
}
