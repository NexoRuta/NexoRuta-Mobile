using Microsoft.Extensions.Logging;
using System.Globalization;
using Serilog;
using Serilog.Events;
using Serilog.Formatting.Json;
using CommunityToolkit.Maui;
using ZXing.Net.Maui.Controls;
using Indiko.Maui.Controls.MapBox;
using NexoRuta.Mobile.Presentacion;
using NexoRuta.Mobile.Almacenamiento;
using NexoRuta.Mobile.Storage;
using NexoRuta.Mobile.Api.Identidad;
using NexoRuta.Mobile.Core.Identidad;
using NexoRuta.Mobile.Seguridad;

namespace NexoRuta.Mobile;

public static class MauiProgram
{
	public static MauiApp CreateMauiApp()
	{
        var samplingText = Environment.GetEnvironmentVariable("NEXORUTA_OTEL_SAMPLING_RATIO");
#if DEBUG
        var defaultSamplingRatio = 1d;
#else
        var defaultSamplingRatio = 0.1d;
#endif
        var samplingRatio = string.IsNullOrWhiteSpace(samplingText) ? defaultSamplingRatio
            : double.TryParse(samplingText, NumberStyles.Float, CultureInfo.InvariantCulture, out var configuredRatio)
                ? configuredRatio
                : throw new InvalidOperationException("NEXORUTA_OTEL_SAMPLING_RATIO must be a decimal between 0 and 1.");
        TelemetryMovil.Configure(samplingRatio);
        var cultura = System.Globalization.CultureInfo.GetCultureInfo("es-UY");
        System.Globalization.CultureInfo.DefaultThreadCurrentCulture = cultura;
        System.Globalization.CultureInfo.DefaultThreadCurrentUICulture = cultura;
		var builder = MauiApp.CreateBuilder();
        var configuracion = new ConfiguracionAplicacion();
        builder.Services.AddSingleton(configuracion);
#if ANDROID && DEBUG
        Action<string>? registrarDiagnostico = mensaje => Android.Util.Log.Info("NexoRuta.Session", mensaje);
#else
        Action<string>? registrarDiagnostico = null;
#endif
        if (configuracion.Api is { } api)
        {
            builder.Services.AddSingleton(s => new ClienteSesionMovil(api, s.GetRequiredService<IAlmacenSesionMovil>(),
                registrarDiagnostico: registrarDiagnostico,
                logger: s.GetRequiredService<ILogger<ClienteSesionMovil>>()));
            builder.Services.AddSingleton<NexoRuta.Mobile.Application.SincronizadorJornada>();
            builder.Services.AddSingleton<NexoRuta.Mobile.Application.ServicioAvisosReparto>();
            builder.Services.AddSingleton<NexoRuta.Mobile.Application.ServicioSesionTrabajo>();
            builder.Services.AddSingleton<NexoRuta.Mobile.Application.ServicioHojasRuta>();
            builder.Services.AddSingleton<SincronizacionConectividad>();
        }
        if (!string.IsNullOrEmpty(configuracion.MapboxToken)) builder.UseMapbox(configuracion.MapboxToken, enableTelemetry: false);
        builder.Services.AddSingleton<PrincipalViewModel>();
        builder.Services.AddSingleton<Notificaciones.IRegistroPush, Notificaciones.RegistroPushPlataforma>();
        builder.Services.AddSingleton<Notificaciones.ControlNotificaciones>();
        builder.Services.AddSingleton<MainPage>();
        builder.Services.AddSingleton<Captura.IInteraccionReparto, Captura.InteraccionReparto>();
        builder.Services.AddSingleton<Ubicacion.ITrackingJornada, Ubicacion.TrackingJornada>();
        builder.Services.AddSingleton(s => new NexoRuta.Mobile.Application.Ubicacion.RegistroUbicacion(
            s.GetRequiredService<IAlmacenSesionMovil>(), s.GetRequiredService<RepositorioJornadasSqlite>(),
            configuracion.Api!.AbsoluteUri, s.GetService<NexoRuta.Mobile.Application.SincronizadorJornada>()));
        builder.Services.AddSingleton<NexoRuta.Mobile.Application.EvidenciasLocales>();
        builder.Services.AddSingleton<Mapas.ProveedorMapbox>();
        builder.Services.AddSingleton<Mapas.IProveedorMapas>(s => s.GetRequiredService<Mapas.ProveedorMapbox>());
        builder.Services.AddSingleton<NexoRuta.Mobile.Application.Mapas.IProveedorMapaOffline>(s => s.GetRequiredService<Mapas.ProveedorMapbox>());
        builder.Services.AddSingleton<NexoRuta.Mobile.Application.Mapas.ServicioPreparacionMapa>();
		builder
			.UseMauiApp<App>()
            .UseMauiCommunityToolkitCamera()
            .UseBarcodeReader()
			.ConfigureFonts(fonts =>
			{
				fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
				fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansSemibold");
			});

		var rutaLogs = Path.Combine(FileSystem.AppDataDirectory, "logs");
		Directory.CreateDirectory(rutaLogs);
		var logger = new LoggerConfiguration()
			.MinimumLevel.Information()
			.MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
			.Enrich.FromLogContext()
			.Enrich.WithProperty("service", "NexoRuta.Mobile")
			.Enrich.With<MobileActivityLogEnricher>()
			.WriteTo.File(new JsonFormatter(renderMessage: true), Path.Combine(rutaLogs, "nexoruta-.json"),
				rollingInterval: RollingInterval.Day, fileSizeLimitBytes: 4 * 1024 * 1024,
				rollOnFileSizeLimit: true, retainedFileCountLimit: 7, flushToDiskInterval: TimeSpan.FromSeconds(2))
			.CreateLogger();
		builder.Logging.ClearProviders();
		builder.Logging.AddSerilog(logger, dispose: true);

		builder.Services.AddSingleton<IAlmacenClavesProtegidas, AlmacenClavesProtegidas>();
		builder.Services.AddSingleton<IAlmacenSesionMovil, AlmacenSesionProtegida>();
		builder.Services.AddSingleton<IVerificacionLocal, VerificacionLocal>();
		builder.Services.AddSingleton(services => new RepositorioJornadasSqlite(
			DirectorioEvidencias.Crear("operacion"),
			services.GetRequiredService<IAlmacenClavesProtegidas>()));
		builder.Services.AddSingleton(services => new RepositorioArchivosCifrados(
			DirectorioEvidencias.Crear(),
			services.GetRequiredService<IAlmacenClavesProtegidas>()));
		return builder.Build();
	}
}
