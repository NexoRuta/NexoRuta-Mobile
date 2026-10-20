using Microsoft.Extensions.Logging;
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
		var builder = MauiApp.CreateBuilder();
        var configuracion = new ConfiguracionAplicacion();
        builder.Services.AddSingleton(configuracion);
        if (configuracion.Api is { } api)
        {
            builder.Services.AddSingleton(s => new ClienteSesionMovil(api, s.GetRequiredService<IAlmacenSesionMovil>()));
            builder.Services.AddSingleton<NexoRuta.Mobile.Application.SincronizadorJornada>();
            builder.Services.AddSingleton<NexoRuta.Mobile.Application.ServicioAvisosReparto>();
            builder.Services.AddSingleton<NexoRuta.Mobile.Application.ServicioSesionTrabajo>();
            builder.Services.AddSingleton<SincronizacionConectividad>();
        }
        if (!string.IsNullOrEmpty(configuracion.MapboxToken)) builder.UseMapbox(configuracion.MapboxToken, enableTelemetry: false);
        builder.Services.AddSingleton<PrincipalViewModel>();
        builder.Services.AddSingleton<Notificaciones.IRegistroPush, Notificaciones.RegistroPushPlataforma>();
        builder.Services.AddSingleton<Notificaciones.ControlNotificaciones>();
        builder.Services.AddSingleton<MainPage>();
        builder.Services.AddSingleton<Captura.IInteraccionReparto, Captura.InteraccionReparto>();
        builder.Services.AddSingleton<Ubicacion.ITrackingJornada, Ubicacion.TrackingJornada>();
        builder.Services.AddSingleton<Ubicacion.RegistroUbicacion>();
        builder.Services.AddSingleton<NexoRuta.Mobile.Application.EvidenciasLocales>();
        builder.Services.AddSingleton<Mapas.IProveedorMapas, Mapas.ProveedorMapbox>();
		builder
			.UseMauiApp<App>()
            .UseMauiCommunityToolkitCamera()
            .UseBarcodeReader()
			.ConfigureFonts(fonts =>
			{
				fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
				fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansSemibold");
			});

#if DEBUG
		builder.Logging.AddDebug();
#endif

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
