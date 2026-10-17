# Arquitectura de la aplicación móvil

## Corte implementado — 17/10/2026

La primera etapa incorpora un núcleo .NET 10 comprobable sin MAUI. El corte siguiente agrega archivos cifrados y una prueba nativa Release en el S21. T03.e.1 agrega cliente HTTP de identidad y adaptador de sesión protegida, todavía sin pantalla conectada ni prueba HTTP real. La app conserva su pantalla de plantilla; no ofrece ingreso, rutas ni operación de reparto. SQLite cifrado y la sincronización operativa siguen pendientes. El backend tiene login JWT central y renovación móvil por 30 días absolutos configurables, con rotación y revocación por sesión, según el [contrato de identidad](contratos/identidad.md). El usuario confirmó reutilizar ese sistema y descartó compatibilidades con versiones anteriores.

La UI MAUI toma Backoffice como referencia de identidad, según la aclaración del usuario. Adaptar presentación y distribución a una UX simple para el repartidor, con pocas acciones, controles táctiles y lectura rápida; Backoffice está incompleto y no debe copiarse literalmente. Mantener coherencia en terminología, formularios y mensajes, centralizando recursos en XAML. El requisito y su aceptación están en D16 del plan.

```text
NexoRuta.Mobile                 aplicación MAUI y composición de adaptadores
    → NexoRuta.Mobile.Core     registro local de jornada y formato de estado
    → NexoRuta.Mobile.Storage  archivos cifrados; clave suministrada por SecureStorage
        → NexoRuta.Mobile.Core identidad local de cuenta
    → NexoRuta.Mobile.Api      HTTP de identidad y contrato del almacén protegido
        → NexoRuta.Mobile.Core identidad local de cuenta

NexoRuta.Mobile.Core.Tests     pruebas del núcleo, sin workloads MAUI
    → NexoRuta.Mobile.Core

NexoRuta.Mobile.Storage.Tests  archivos reales, sin workloads MAUI
    → NexoRuta.Mobile.Storage

NexoRuta.Mobile.Api.Tests      HTTP/almacén controlados, sin workloads MAUI
    → NexoRuta.Mobile.Api

UI MAUI → cliente de identidad → HTTPS → API   conexión y prueba real pendientes
```

`Mobile.Core` no referencia las capas del backend, EF Core, ASP.NET ni MAUI. El servidor conserva las transiciones de envío, permisos, pertenencias, asignaciones y validación definitiva de pruebas. El núcleo móvil registra lo que el dispositivo necesita conservar y reintentar; no convierte una declaración física en un envío validado.

La referencia MAUI → Core ya está en el proyecto y los dos nuevos proyectos están en `NexoRuta.sln`. La app y esa referencia compilan para Android ARM64 en Release tras preparar Ubuntu; el núcleo todavía no tiene una UI operativa conectada.

`Mobile.Storage` y sus pruebas se agregan a la misma solución para verificar E/S y cifrado sin referenciar MAUI desde el núcleo. El [detalle del corte T02.c](almacenamiento-cifrado.md) documenta claves, formato, fallos, backups y evidencia nativa; no sustituye la persistencia operativa de T03.d.

Verificación de este corte: **48 pruebas correctas** (25 de jornada y 23 de archivos), APK diagnóstico y normal Release sin advertencias/errores, y prueba Android real con SecureStorage e imagen recuperada tras cambiar de proceso. El APK normal excluye código y asset de diagnóstico; al alternar la propiedad de compilación se limpiaron los artefactos, como indica el documento del corte. La configuración de backup se inspeccionó; no se ejecutó una restauración.

El corte [T03.e.1](sesion-http.md) añade **42 pruebas controladas** y compilación Android Release. No cambia las dependencias del núcleo. `ClienteSesionMovil` serializa ingreso/renovación/consultas; `AlmacenSesionProtegida` guarda el par en un único valor de SecureStorage. Antes de consumir refresh se persiste un estado sin tokens, conservando cuenta y origen: ante cierre, respuesta perdida o error de guardado, exigir reingreso en lugar de reutilizar un secreto posiblemente consumido. El cliente no ofrece cierre/cambio de cuenta hasta integrar el control real de pendientes. No se instancia desde la plantilla ni sustituye el desbloqueo biométrico.

## Registro de jornada

`JornadaLocal` registra inicio, declaraciones físicas, incorporación de evidencia y finalización. Cada operación tiene UUIDv7 estable, secuencia, fecha del dispositivo con desplazamiento horario y datos JSON propios. Los datos se copian al registrar la operación para conservarlos aunque se libere el documento JSON original.

- El orden depende de la secuencia, no del reloj del teléfono. Se conserva la fecha realmente informada; no se cambia para forzar el orden.
- Finalizar conserva un único ID y fecha de cierre, incluso si se repite la acción.
- Después de finalizar no se pueden insertar declaraciones o pruebas con una fecha anterior para eludir el plazo.
- `PrepararEnvio()` devuelve primero las operaciones previas sin confirmar. El cierre solo queda disponible cuando todas tienen confirmación.
- `ConfirmarPersistencia()` es idempotente, rechaza IDs ajenos y no permite confirmar el cierre antes del trabajo previo. No elimina operaciones.
- `TienePendientes` permanece verdadero hasta confirmar todo, incluido el cierre. La capa de sesión deberá usar este estado junto con las evidencias pendientes de subir para bloquear cierre/cambio de cuenta.

La confirmación de persistencia solo podrá invocarla el futuro adaptador de sincronización tras verificar la respuesta del servidor y la persistencia de las evidencias asociadas. Una conexión disponible, un HTTP `202` o un ACK de transporte no bastan. El cliente HTTP de identidad no implementa sincronización de jornadas, reintentos de negocio ni confirmaciones reales de entregas.

## Estado local versión 1

`EstadoJornadaLocal` guarda identidad de jornada/ruta, cuenta, registro completo y confirmaciones. `CuentaMovil` distingue `UsuarioId`, `AccesoId` y `OperadorId`; ninguno sustituye a los otros ni autoriza por sí solo una acción del servidor.

`SerializadorJornadaLocal` utiliza generación de código de `System.Text.Json`, sin serialización por reflexión en el núcleo. Las propiedades usan camelCase y los tipos de operación se escriben como texto. Es un formato local versionado, **no un contrato HTTP ya soportado por la API**.

La restauración exige la cuenta actual esperada y rechaza:

- otra identidad, acceso u operador;
- versión desconocida, secuencia rota, IDs duplicados o inicio ausente;
- cierre intermedio o cierre confirmado con operaciones previas pendientes;
- confirmaciones ajenas/repetidas y JSON nulo, truncado o con campos desconocidos.

La serialización produce bytes en memoria. **No implementa persistencia durable ni cifrado.** El adaptador SQLite cifrado de T03 deberá guardar estado/archivos de forma segura y transaccional antes de informar éxito local. No escribir estos bytes directamente en archivos, preferencias o logs. La biblioteca no contiene credenciales ni un mecanismo de autenticación local.

El modelo es síncrono; la composición futura debe serializar las escrituras de la jornada y la coordinación de sincronización. No se ha demostrado acceso concurrente ni recuperación ante fallos de almacenamiento.

## Verificación ejecutada

En contenedor temporal `mcr.microsoft.com/dotnet/sdk:10.0`, con SDK **10.0.401**, runtime **10.0.12** y caché NuGet en `/tmp/nexoruta-mobile-nuget`:

```bash
dotnet test tests/NexoRuta.Mobile.Core.Tests/NexoRuta.Mobile.Core.Tests.csproj \
  --configuration Release --logger 'console;verbosity=normal'
```

Restore, compilación y **25 pruebas pasaron**. Cubren cierre ordenado, confirmaciones parciales y repetidas, evidencia anterior al cierre restaurada después de serializar, cambios de reloj, aislamiento de cuenta, estado corrupto y dependencias de arquitectura.

La prueba de restauración utiliza memoria serializada: no acredita durabilidad de SQLite, cifrado, HTTP, ejecución en Android/iOS ni publicación AOT de la app.

## Entorno nativo Ubuntu preparado

El primer intento en Docker:

```bash
dotnet build src/NexoRuta.Mobile/NexoRuta.Mobile.csproj \
  --configuration Release --framework net10.0-android --no-restore --nologo
```

Resultado inicial: **`NETSDK1147`, falta `maui-android`**. La revisión posterior encontró Android SDK y ADB en `~/Android/Sdk`, fuera del `PATH`. Se corrige el diagnóstico inicial de que no estaban instalados.

El usuario autorizó preparar Ubuntu/VS Code. Se instalaron SDK .NET **10.0.401**, workload **`maui-android`** (`10.0.0/10.0.100`, .NET Android `36.1.2`), extensión **.NET MAUI 1.17.289** y sus dependencias, herramientas Android adicionales mediante `InstallAndroidDependencies` y **Microsoft OpenJDK 21.0.12.1** completo. Java 21 existente era solo un runtime; Java 25 no es compatible con este workload. No se cambiaron los paquetes Java del sistema.

Las variables y rutas están en `~/.config/nexoruta/android-env.sh`, cargado por `.profile`/`.bashrc`. VS Code tiene las rutas Android/JDK en ajustes de usuario, y se guardaron copias de los archivos previos. Detalles y conexión inalámbrica en el [README](../README.md). [Instalación oficial](https://learn.microsoft.com/en-us/dotnet/maui/get-started/installation?view=net-maui-10.0).

Comprobado en Ubuntu, con el entorno cargado:

```bash
dotnet test tests/NexoRuta.Mobile.Core.Tests/NexoRuta.Mobile.Core.Tests.csproj \
  --configuration Release --nologo
dotnet build src/NexoRuta.Mobile/NexoRuta.Mobile.csproj \
  --configuration Release --framework net10.0-android \
  -p:RuntimeIdentifier=android-arm64 -p:AndroidPackageFormats=apk --nologo
```

Resultado: **25 pruebas correctas; build Android Release correcto, 0 advertencias y 0 errores**. APK generado en `src/NexoRuta.Mobile/bin/Release/net10.0-android/android-arm64/com.companyname.nexoruta.mobile-Signed.apk`. La compilación incluye optimización de ensamblados y AOT Mono; no demuestra funcionamiento de adaptadores aún no implementados ni publicación NativeAOT.

El S21 tiene el USB averiado; se validó por ADB inalámbrico después de que el usuario renovara el emparejamiento desde Android Studio. Modelo **SM-G991U, Android 14/API 34**. `adb install -r` informó `Success`; el arranque en frío con `am start -W` informó `Status: ok` y el proceso permaneció activo. Se leyó la jerarquía UI de la app: `Hello, World!`, bienvenida MAUI y botón `Clicked 2 times`, visibles. El XML temporal se eliminó del teléfono; no se guardaron códigos de emparejamiento ni direcciones del dispositivo en el repositorio.

`apksigner` comprobó la firma v1/v2/v3, con certificado **Android Debug**. Las advertencias del esquema v1 sobre entradas `META-INF` no invalidaron la verificación; el APK tiene también firmas v2/v3. Esta firma es local de desarrollo, no la clave definitiva de distribución. El build de MSBuild tuvo 0 advertencias y 0 errores.

Esto comprueba compilación, instalación y UI de la plantilla, no el depurador de VS Code, Hot Reload ni los adaptadores futuros. iOS sigue sin compilación ni pruebas, por falta de Mac/Xcode/iPhone.

La siguiente etapa debe completar los contratos operativos de T01, el perfil repartidor y las pruebas nativas restantes de T02. Para conectar el ingreso MVVM se necesita un origen HTTPS confiable, persistencia operativa y biometría. El backend y el cliente de identidad no acreditan T01 completa ni el ingreso MAUI. La cancelación pública mantiene el bloqueo D15 y la selección de Mapbox todavía requiere evaluación.
