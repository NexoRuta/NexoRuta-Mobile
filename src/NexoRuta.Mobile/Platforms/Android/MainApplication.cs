using Android.App;
using Android.Runtime;

namespace NexoRuta.Mobile;

#if NEXORUTA_LOCAL_HTTPS
[Application(NetworkSecurityConfig = "@xml/nexoruta_local_network_security")]
#else
[Application]
#endif
public class MainApplication : MauiApplication
{
	public MainApplication(IntPtr handle, JniHandleOwnership ownership)
		: base(handle, ownership)
	{
	}

	protected override MauiApp CreateMauiApp() => MauiProgram.CreateMauiApp();
}
