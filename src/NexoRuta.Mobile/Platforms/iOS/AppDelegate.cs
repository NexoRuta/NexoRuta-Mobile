using Foundation;
#if IOS
using Firebase.CloudMessaging;
using NexoRuta.Mobile.Platforms.iOS;
using UIKit;
using UserNotifications;
#endif

namespace NexoRuta.Mobile;

#if IOS
[Register("AppDelegate")]
public class AppDelegate : MauiUIApplicationDelegate, IMessagingDelegate
#else
[Register("AppDelegate")]
public class AppDelegate : MauiUIApplicationDelegate
#endif
{
	protected override MauiApp CreateMauiApp() => MauiProgram.CreateMauiApp();

#if IOS
	private static readonly RecepcionPushIos RecepcionPush = new();

	public override bool FinishedLaunching(UIApplication application, NSDictionary? launchOptions)
	{
		var firebaseConfigurado = NSBundle.MainBundle.PathForResource("GoogleService-Info", "plist") is not null;
		if (firebaseConfigurado)
		{
			Firebase.Core.App.Configure();
			Firebase.CloudMessaging.Messaging.SharedInstance.AutoInitEnabled = false;
			Firebase.CloudMessaging.Messaging.SharedInstance.Delegate = this;
			UNUserNotificationCenter.Current.Delegate = RecepcionPush;
		}

		var iniciado = base.FinishedLaunching(application, launchOptions);
		if (firebaseConfigurado)
		{
			if (launchOptions?[UIApplication.LaunchOptionsRemoteNotificationKey] is NSDictionary datos)
				_ = RecepcionPush.RecibirAsync(datos, abierto: true);
			_ = RestaurarRegistroPushAsync();
		}
		return iniciado;
	}

	public override void RegisteredForRemoteNotifications(UIApplication application, NSData deviceToken)
	{
		Firebase.CloudMessaging.Messaging.SharedInstance.ApnsToken = deviceToken;
	}

	[Export("messaging:didReceiveRegistrationToken:")]
	public void DidReceiveRegistrationToken(Firebase.CloudMessaging.Messaging messaging, string? token)
	{
		if (!string.IsNullOrWhiteSpace(token)) _ = NexoRuta.Mobile.Notificaciones.RegistroPushPlataforma.ActualizarFidAsync();
	}

	private static async Task RestaurarRegistroPushAsync()
	{
		try
		{
			var fid = await SecureStorage.Default.GetAsync(Notificaciones.ControlNotificaciones.ClaveFid);
			Firebase.CloudMessaging.Messaging.SharedInstance.AutoInitEnabled = fid is not null;
			if (fid is not null && await NexoRuta.Mobile.Notificaciones.RegistroPushPlataforma.PermisoIosAsync())
				UIApplication.SharedApplication.RegisterForRemoteNotifications();
		}
		catch { /* El registro se recupera al activar los avisos con la app desbloqueada. */ }
	}
#endif
}
