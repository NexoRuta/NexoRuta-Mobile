# NexoRuta Mobile

Aplicación del repartidor en .NET MAUI 10. Incluye ingreso central, desbloqueo local, hojas descargadas, carga inicial, entregas y pruebas, intentos fallidos, rendición y cierre. El trabajo se conserva cifrado sin conexión y se sincroniza con versiones e identificadores estables.

El [plan de implementación](docs/plan-implementacion.md) conserva las decisiones de producto y el avance por tareas. La [arquitectura](docs/arquitectura-mobile.md) explica las capas y los límites de confianza. La [ejecución y evidencia](docs/ejecucion-completa.md) distingue lo implementado, lo probado y lo pendiente.

## Estado al 21/10/2026

Backend implementa perfiles, reglas versionadas, rutas, custodia física y sincronización. Backoffice incluye planificación, despacho, recepción y reasignación. La reasignación conserva al custodio actual y encola un aviso dirigido a él; el nuevo despacho exige recepción física en depósito.

El cliente Android registra FID y permisos, recibe avisos genéricos y consulta su vigencia después del desbloqueo. Las referencias se guardan cifradas; un push nunca cambia directamente el estado de un envío. Los cambios remotos se consultan sin reemplazar la hoja original usada para sincronizar trabajo pendiente. Al recuperar Internet o activar la app se reintentan los pendientes, incluidos cierres que ya no tienen GPS activo.

**Entorno local HTTPS/API/Worker/RabbitMQ/Mailpit en ejecución, con base aislada.** Se comprobaron ingreso y descarga en el S21, registro FID, reasignación con el proceso de la app cerrado, envío FCM real y notificación publicada en Android. El usuario completó el desbloqueo local; recepción, apertura y confirmación quedaron persistidas por separado. [Evidencia y límites](docs/validacion-local-s21.md).

La [preparación del mapa offline](docs/mapa-offline.md) conserva el estado por cuenta/hoja, comprueba estilo y teselas y protege la descarga anterior ante fallos. La confirmación de salida exige mapa preparado. Sin token, se informa la dependencia; la recepción/descarga real y el puente offline iOS siguen pendientes.

La geocodificación usa Geoapify Free detrás de interfaz; Comercio elige y confirma/corrige el destino. Falta clave propia de Geoapify y token público Mapbox para validar proveedores reales y mapa offline. El circuito de correo/cancelación se probó localmente en Mailpit, sin SMTP externo. Cierre de jornada con faltantes registra incidencias inmutables y conserva custodia hasta recepción física en depósito. El cliente iOS ya implementa registro FID y recepción/apertura push; falta compilarlo y aceptarlo con Firebase/APNs y recursos Apple.

La [integración parcial de CU-04](docs/reglas-envio.md) fija las reglas al crear cada envío y las descarga por parada; se verificó ingreso, descarga y detalle en una variante separada del S21. Quedan aceptación operativa móvil completa, mapa offline y firma/infraestructura de distribución. No confundir el ensayo FCM con un despliegue de producción ni con la aceptación integral del plan.

La compilación Android Release ARM64 conserva tres advertencias Mapbox sobre páginas nativas de 16 KB para Android 16. La firma actual es de desarrollo; la firma de distribución sigue pendiente.

La [ubicación de jornada](docs/ubicacion-jornada.md) tiene prueba S21 de inicio, diez puntos con intervalo observado de 30,00–30,33 segundos, recuperación tras interrumpir HTTPS y cierre que detiene el servicio. Falta medición prolongada con pantalla apagada y batería.

Para el trabajo diario, la configuración **Debug** omite la verificación local del teléfono (huella o credencial) tanto al reabrir la jornada como al confirmar una rendición. **Release**, incluida CI/distribución, la exige; no hay una variable de entorno de ejecución para desactivarla. El ingreso central y los permisos del sistema siguen siendo obligatorios. La variante diagnóstica **NexoRuta Validación** también puede omitir la verificación para sus ensayos y requiere `ValidarAlmacenamiento=true`, el ID separado `uy.nexoruta.repartidor.validacion` y un directorio de artefactos separado. [Compilación y límites](docs/ci-distribucion.md).

## Estructura

- `src/NexoRuta.Mobile.Core`: jornada inmutable, cuenta y contratos locales; sin MAUI ni backend.
- `src/NexoRuta.Mobile.Api`: identidad, rutas, operaciones, evidencia y avisos por HTTPS.
- `src/NexoRuta.Mobile.Storage`: SQLite y archivos cifrados, separados por cuenta.
- `src/NexoRuta.Mobile.Application`: sincronización durable, captura y consulta segura de avisos.
- `src/NexoRuta.Mobile`: pantallas MVVM, biometría y adaptadores Android/iOS.
- `tests/`: pruebas portables de las cuatro capas anteriores.

Android requiere API 30 (Android 11); iOS, 15. La app se llama **NexoRuta Repartidor**, identificador `uy.nexoruta.repartidor`. Ubuntu compila Android; iOS necesita un Mac con Xcode. Las pantallas comparten la identidad de Backoffice, adaptada al uso del repartidor.

## Requisitos

- .NET SDK 10 y workloads de .NET MAUI para el destino elegido. En el checkout combinado, `global.json` de la raíz selecciona SDK `10.0.401`; el submódulo mobile conserva el mismo selector para su CI aislada.
- Android: Android SDK y emulador/dispositivo configurados para ejecutar.
- iOS: macOS y herramientas Apple/Xcode compatibles.

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

## Entorno local y CI

[Arranque HTTPS, correo de prueba y S21 por Wi-Fi](tools/local/README.md). [CI y comandos de firma](docs/ci-distribucion.md).

## Compilar y comprobar

Desde la raíz de este repositorio:

```bash
dotnet test tests/NexoRuta.Mobile.Core.Tests/NexoRuta.Mobile.Core.Tests.csproj --configuration Release
dotnet test tests/NexoRuta.Mobile.Storage.Tests/NexoRuta.Mobile.Storage.Tests.csproj --configuration Release
dotnet test tests/NexoRuta.Mobile.Api.Tests/NexoRuta.Mobile.Api.Tests.csproj --configuration Release
dotnet test tests/NexoRuta.Mobile.Application.Tests/NexoRuta.Mobile.Application.Tests.csproj --configuration Release
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

## Configuración de servicios

El backend se selecciona al compilar. Docker local (predeterminado): `-p:NexoRutaBackendTarget=Docker`, origen `https://localhost:5843/` y, en Android, `-p:NexoRutaAndroidDevelopmentCa=/ruta/ca.crt`. VM pública: `-p:NexoRutaBackendTarget=GcpDemo -p:NexoRutaGcpApiOrigin=https://api.<IP-actual>.sslip.io/`, sin CA local. El origen GCP actual se consulta con `tofu -chdir=infra/gcp/demo output -json endpoints` y se toma la propiedad `api`. Ambos destinos exigen HTTPS válido; no se desactiva la validación TLS.

Para Android, usar `-p:NexoRutaFirebaseAndroidConfig=/ruta/externa/google-services.json`, correspondiente a `uy.nexoruta.repartidor`. La compilación falla si se indicó una ruta inexistente. El archivo se mantiene fuera del repositorio; sin configuración, la app informa la dependencia y conserva la operación local.

Para iOS, usar `-p:NexoRutaFirebaseIosConfig=/ruta/externa/GoogleService-Info.plist`, correspondiente al mismo bundle ID. El archivo externo se incorpora como recurso sin copiarlo al repositorio. Los entitlements seleccionan APNs de desarrollo en Debug y producción en Release; la firma requiere un perfil Apple con Push Notifications habilitado. El alta solo activa FCM después de que el repartidor concede permiso.

Mapbox usa `-p:NexoRutaMapboxPublicToken=pk.SU_TOKEN_PUBLICO`. El token público forma parte del cliente; las credenciales administrativas de Firebase y las claves de cifrado pertenecen exclusivamente a backend/Worker. Revisar [push y reasignación](docs/push-reasignacion.md) antes de configurar o demostrar notificaciones remotas.

## Una sola aplicación instalada

Usar **NexoRuta Repartidor** (`uy.nexoruta.repartidor`) tanto en desarrollo como en las pruebas del teléfono. Actualizar la instalación existente; no crear ni instalar aplicaciones auxiliares con otro ID. La configuración de entorno puede cambiar sin cambiar la identidad de la app. Las referencias históricas a «NexoRuta Validación» describen ensayos anteriores, no el procedimiento vigente.
