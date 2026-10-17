# NexoRuta Mobile

Aplicación móvil del proyecto NexoRuta, creada con .NET MAUI para el cliente de repartidores. La app todavía conserva la pantalla de muestra; incorpora un núcleo .NET para registrar jornadas, archivos cifrados comprobados en el S21 y un cliente HTTP de identidad con pruebas controladas. La UI operativa y la conexión real del ingreso siguen pendientes.

El [plan de implementación](docs/plan-implementacion.md) consolida el alcance Android/iOS, las decisiones acordadas, las dependencias de backend, la sincronización sin conexión y el opcional de push en producción. La entrega prevista es un APK Android firmado e iOS preparado, con validación Apple pendiente. El plan identifica las decisiones y pruebas técnicas que deben resolverse antes de cada tarea; sus propuestas pendientes no son decisiones aprobadas.

La [arquitectura móvil](docs/arquitectura-mobile.md) describe los límites del núcleo, la evidencia de la etapa y la preparación necesaria para continuar.

## Estado actual

Verificado el 17/10/2026: `Mobile.Core` y su proyecto de pruebas compilan en Release; **25 pruebas pasaron** con SDK 10.0.401, tanto en Docker como posteriormente en Ubuntu. Cubren el modelo y el estado serializado en memoria, sin MAUI, SQLite ni backend.

El primer intento de compilación Android en Docker falló con **`NETSDK1147`: falta `maui-android`**. Después, el usuario autorizó preparar Ubuntu/VS Code. Se encontró el Android SDK de Android Studio en `~/Android/Sdk`, incluido `adb`; no estaban en el `PATH` al hacer la revisión inicial. No hay todavía ingreso móvil, SQLite cifrado, sincronización HTTP ni push funcionales.

El corte **T02.c** agrega AES-256-GCM para archivos, claves en `SecureStorage`, separación por cuenta y reintentos inmutables. **23 pruebas adicionales pasaron**, y el APK Release del S21 guardó y recuperó una imagen después de reiniciar el proceso; se comprobó bloqueo ante clave ausente sin borrar archivos. [Detalle, comandos y límites](docs/almacenamiento-cifrado.md).

Backend y webs usan JWT y roles por organización. El usuario confirmó reutilizarlos; se agregó en backend renovación móvil por **30 días absolutos configurables**, rotación y revocación por sesión. [Contrato central y evidencia](docs/contratos/identidad.md). La integración MAUI todavía está pendiente.

El corte **T03.e.1** agrega `Mobile.Api`: ingreso/contexto/permisos por HTTPS, renovación serializada y reingreso seguro ante respuesta perdida; la cuenta guardada impide reemplazar accidentalmente el trabajo por otra identidad. **42 pruebas controladas pasaron**. El adaptador de sesión en SecureStorage está registrado y compila para Android; todavía no se usa desde una pantalla, no ofrece cierre/cambio de cuenta y no acredita una sesión offline ni una prueba HTTPS/S21 del flujo. [Comportamiento, comandos y límites](docs/sesion-http.md).

Tras instalar la toolchain, **la app MAUI compila en Release para Android ARM64, con 0 advertencias y 0 errores**. Se generó el APK `src/NexoRuta.Mobile/bin/Release/net10.0-android/android-arm64/com.companyname.nexoruta.mobile-Signed.apk`, con firma Android Debug verificada. Conserva la pantalla y el identificador de la plantilla; no es la entrega funcional ni la firma de distribución definitiva.

**Instalación y arranque comprobados por Wi-Fi en Samsung S21 SM-G991U, Android 14/API 34.** ADB informó instalación correcta y arranque en frío con `Status: ok`; se comprobó la UI visible con sus textos y el contador (`Clicked 2 times`). No se validaron depuración con breakpoints/Hot Reload, integraciones con backend ni iOS.

## Estructura

```text
NexoRuta.sln
src/NexoRuta.Mobile.Core/   # núcleo .NET 10 sin dependencias nativas/backend
  Jornadas/                # operaciones, cierre y estado local versionado
src/NexoRuta.Mobile.Storage/ # archivos cifrados y contrato de claves; sin MAUI
src/NexoRuta.Mobile.Api/     # identidad HTTP y contrato de sesión protegida; sin MAUI
src/NexoRuta.Mobile/
  NexoRuta.Mobile.csproj  # app MAUI single-project y frameworks condicionales
  MauiProgram.cs         # composición de la app
  App.xaml, AppShell.xaml
  MainPage.xaml(.cs)     # pantalla de contador de la plantilla
  Platforms/             # arranque y manifiestos por plataforma
  Resources/             # icono, splash, estilos y recursos
tests/NexoRuta.Mobile.Core.Tests/  # 25 pruebas del núcleo
tests/NexoRuta.Mobile.Storage.Tests/ # 23 pruebas sobre archivos reales
tests/NexoRuta.Mobile.Api.Tests/ # 42 pruebas controladas de identidad/renovación
```

El proyecto configura `net10.0-android`; agrega iOS y MacCatalyst cuando el host no es Linux, y Windows únicamente en Windows. La plataforma destino y sus herramientas disponibles determinan qué framework puede compilarse o ejecutarse.

## Requisitos

- .NET SDK 10 y workloads de .NET MAUI para el destino elegido. En el checkout combinado, `global.json` de la raíz selecciona SDK `10.0.401`; el submódulo aislado no trae ese selector.
- Android: Android SDK y emulador/dispositivo configurados para ejecutar.
- iOS/MacCatalyst: macOS y herramientas Apple/Xcode compatibles.
- Windows: Windows y el workload/SDK de Windows correspondiente.

### Ubuntu y VS Code

Se instaló el SDK .NET **10.0.401** en `~/.dotnet`, el workload **`maui-android`** (manifiesto `10.0.0/10.0.100`, Android SDK .NET `36.1.2`) y la extensión oficial **.NET MAUI 1.17.289**, con C#, C# Dev Kit y .NET Install Tool. Se reutiliza Android SDK en `~/Android/Sdk`; el target `InstallAndroidDependencies` agregó las herramientas de línea de comandos que faltaban. Se instaló **Microsoft OpenJDK 21.0.12.1** completo en `~/.local/share/nexoruta/jdk-21`; la instalación Java 21 previa solo contenía el runtime. Las instalaciones Java del sistema se conservan.

La configuración local está en `~/.config/nexoruta/android-env.sh`, cargada por `.profile` y `.bashrc`; establece `DOTNET_ROOT`, `ANDROID_HOME`, `JAVA_HOME` y agrega .NET/ADB al `PATH`. Se guardaron copias de los archivos anteriores. Las rutas Android/Java también están configuradas en los ajustes de usuario de VS Code; esas rutas particulares no se incluyen en el proyecto.

Para usar el entorno desde una terminal que ya estaba abierta:

```bash
source ~/.config/nexoruta/android-env.sh
dotnet --version
dotnet workload list
adb devices -l
```

Para iniciar VS Code con el entorno actualizado, cerrar sus ventanas y abrirlo desde esa terminal con `code .` en el checkout deseado.

### S21 por Wi-Fi

El USB del S21 está averiado. La instalación y depuración se harán mediante **depuración inalámbrica**, con Ubuntu y el teléfono en la misma Wi-Fi. Activarla en las opciones de desarrollador del teléfono. Puede reutilizarse el emparejamiento de Android Studio; no se necesita conectar antes por USB en Android 11 o posterior. [Documentación de Android](https://developer.android.com/tools/adb#connect-to-a-device-over-wi-fi).

Si hace falta emparejar de nuevo, usar **Pair Devices Using Wi-Fi** en Android Studio o ejecutar:

```bash
adb pair IP:PUERTO_DE_EMPAREJAMIENTO
# Introducir el código temporal del teléfono cuando lo solicite ADB.
adb connect IP:PUERTO_DE_DEPURACION
adb devices -l
```

El puerto de conexión está en la pantalla principal de depuración inalámbrica y puede ser distinto del de emparejamiento. No guardar direcciones ni códigos temporales en el repositorio. Antes de ejecutar, el dispositivo debe figurar como `device` en `adb devices -l`.

## Compilar y comprobar

Desde la raíz de este repositorio:

```bash
dotnet test tests/NexoRuta.Mobile.Core.Tests/NexoRuta.Mobile.Core.Tests.csproj --configuration Release
dotnet test tests/NexoRuta.Mobile.Storage.Tests/NexoRuta.Mobile.Storage.Tests.csproj --configuration Release
```

Esta comprobación solo necesita SDK .NET 10. Para compilar la solución completa hacen falta también los workloads y SDKs nativos de cada destino:

```bash
dotnet restore NexoRuta.sln --nologo
dotnet build NexoRuta.sln --no-restore --nologo
dotnet test NexoRuta.sln --no-build --no-restore --nologo
```

Compilación Release ARM64 comprobada en Ubuntu:

```bash
dotnet build src/NexoRuta.Mobile/NexoRuta.Mobile.csproj \
  --configuration Release --framework net10.0-android \
  -p:RuntimeIdentifier=android-arm64 -p:AndroidPackageFormats=apk --nologo
```

Registro histórico del 7 de octubre de 2026: la plantilla restauró y compiló en el entorno de esa revisión y ejecutó cero pruebas. Ese registro no acredita la compilación MAUI actual ni ejecución en hardware; la verificación actual figura arriba.

## Ejecutar

Para iniciar la app en un destino Android ya configurado, el comando de desarrollo es:

```bash
dotnet build src/NexoRuta.Mobile/NexoRuta.Mobile.csproj -t:Run -f net10.0-android
```

Este comando requiere un emulador iniciado o dispositivo Android conectado y reconocido por las herramientas locales. La validación del S21 se hizo instalando el APK Release con `adb install -r` y abriendo su actividad con `adb shell am start -W`; no se comprobó todavía una sesión del depurador de VS Code. Para iOS, MacCatalyst o Windows hay que elegir el framework correspondiente al host y disponer de sus herramientas nativas. Esta app no expone una URL ni un puerto HTTP.

## Pendiente

Integrar el cliente de identidad con el ingreso MAUI, configurar un origen HTTPS confiable, completar perfil repartidor y los contratos operativos, y reemplazar la pantalla de muestra por los flujos del repartidor. **El diseño toma Backoffice como referencia de identidad y se adapta a una UX simple para el repartidor**, sin copiar su distribución de escritorio ni sus partes incompletas. Siguen pendientes SQLite cifrado, biometría, captura de cámara, sincronización operativa HTTP, mapas offline y push. El cifrado de archivos es una base comprobada; no acredita todavía una jornada ni entrega durable sin conexión.
