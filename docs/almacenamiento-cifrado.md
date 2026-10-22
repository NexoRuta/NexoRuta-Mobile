# T02.c — Archivos sensibles cifrados

Corte del 17/10/2026. Implementa el adaptador de archivos y su prueba nativa; no completa SQLite, captura de fotos, biometría, sesión ni CU-24.

## Arquitectura y alcance

`NexoRuta.Mobile.Storage` referencia únicamente `Mobile.Core` para `CuentaMovil`. Contiene E/S y cifrado comprobables en .NET sin MAUI. El núcleo conserva las reglas de jornada; no recibe dependencias de disco, SQL, HTTP o tipos nativos. La app compone un repositorio singleton con `IAlmacenClavesProtegidas` implementado mediante `SecureStorage.Default`.

La separación permite probar archivos reales y errores de almacenamiento en Linux y ejecutar el mismo código en Android Release, sin duplicarlo en el proyecto de pruebas. No agrega otro servicio, base de datos o sistema de autenticación.

## Contrato local

- `GuardarAsync(cuenta, archivoId, contenido, ct)` guarda una evidencia inmutable. Solo informa éxito después de escribir, hacer flush y renombrar el temporal en el mismo directorio.
- Repetir el mismo ID con los mismos bytes no modifica el archivo; cambiar su contenido se rechaza. Una evidencia posterior requiere otro ID.
- `LeerAsync(cuenta, archivoId, ct)` devuelve bytes autenticados o un error; no entrega contenido parcial ante corrupción.
- Cuenta = usuario + acceso + operador. Su hash delimita directorio y nombre de clave; la cuenta debe provenir de la futura sesión validada, no de entrada arbitraria de la vista. Este aislamiento local no sustituye permisos de servidor.
- Una instancia por proceso serializa acceso y creación de claves. No se acredita escritura simultánea desde varios procesos o instancias sobre el mismo directorio.

Formato binario `NRF1`: cabecera de 4 bytes, nonce aleatorio de 12 bytes, tag de 16 bytes y ciphertext. Se usa **AES-256-GCM** de .NET; la cuenta, el ID del archivo y la versión forman los datos autenticados adicionales. No se implementa criptografía propia ni se reutiliza un nonce deliberadamente.

La clave aleatoria de 32 bytes se guarda en `SecureStorage`, separada de los archivos. Un comprobante cifrado por cuenta permite rechazar una clave incorrecta antes de escribir nuevas evidencias. Clave ausente, malformada, incorrecta o almacenamiento seguro inaccesible bloquean la operación conservando los archivos. No hay borrado, regeneración sobre datos ni recuperación automática que descarte pendientes. Las copias binarias internas de claves se limpian al terminar; la API de `SecureStorage` necesariamente intercambia strings en memoria.

Los temporales contienen únicamente ciphertext; se renombran sin sobrescribir destinos y se limpian al terminar la operación. Un cierre abrupto puede dejar un temporal cifrado huérfano: no se toma como evidencia confirmada ni se elimina al reconstruir el almacén. Si falta el comprobante con archivos presentes, se bloquea sin modificar datos; la recuperación administrativa sigue pendiente. El test de reinicio no acredita resistencia a pérdida de alimentación o avería del sistema de archivos.

Android deshabilita backup y excluye archivos, bases y preferencias de cloud backup/device transfer mediante reglas de Android 12+. En iOS/MacCatalyst el directorio se marca excluido de backup usando `NSURLIsExcludedFromBackupKey`; ese código **no fue compilado ni ejecutado** por falta de recursos Apple. La captura/compresión de cámara futura debe resolver también sus temporales de origen: este adaptador recibe bytes, no controla archivos creados previamente por otra API.

## Dependencias evaluadas

Este corte no agrega paquetes NuGet ni proveedor SQLite. `SQLitePCLRaw.bundle_e_sqlcipher` 2.1.11 y los bundles gratuitos de cifrado anteriores están deprecados. La selección de una distribución mantenida, compatible Android/iOS y su licencia queda pendiente de la prueba T02.b; no se instala una dependencia obsoleta ni se presenta cifrado de archivos como cifrado de SQLite. [Estado del paquete](https://www.nuget.org/packages/SQLitePCLRaw.bundle_e_sqlcipher), [explicación del mantenedor](https://github.com/ericsink/SQLitePCL.raw/wiki/SQLite-encryption-options-for-use-with-SQLitePCLRaw).

`SecureStorage` protege claves; no exige por sí mismo biometría por operación. D05 sigue pendiente. [Documentación MAUI](https://learn.microsoft.com/en-us/dotnet/maui/platform-integration/storage/secure-storage?view=net-maui-10.0).

## Evidencia ejecutada

Ubuntu, SDK .NET 10.0.401:

```bash
dotnet test tests/NexoRuta.Mobile.Storage.Tests/NexoRuta.Mobile.Storage.Tests.csproj \
  --configuration Release --nologo
```

**23 pruebas correctas**, con archivos reales y almacén de claves simulado: lectura desde otra instancia, ausencia de texto sensible en disco, reintento inmutable, nonce distinto, alteración de cabecera/nonce/tag/contenido, truncado, cuentas diferentes, sustitución de ID, clave perdida/malformada/incorrecta, fallo al guardar clave, comprobante perdido, concurrencia en la instancia y cancelación previa.

Prueba nativa con el S21 SM-G991U, Android 14/API 34, por Wi-Fi:

```bash
dotnet build src/NexoRuta.Mobile/NexoRuta.Mobile.csproj \
  --configuration Release --framework net10.0-android \
  -p:RuntimeIdentifier=android-arm64 -p:AndroidPackageFormats=apk \
  -p:ValidarAlmacenamiento=true --nologo
```

Build con **0 advertencias y 0 errores**. APK instalado con `adb install -r`. La prueba usa una imagen PNG de la plantilla, un directorio y claves separados de los datos operativos; no simula una entrega real. Las fases se invocan sobre la actividad resuelta por `adb shell cmd package resolve-activity --brief com.companyname.nexoruta.mobile`:

```bash
adb -s DISPOSITIVO shell am start -W -n ACTIVIDAD \
  --es nexoruta.prueba.almacenamiento guardar
adb -s DISPOSITIVO shell am force-stop com.companyname.nexoruta.mobile
adb -s DISPOSITIVO shell am start -W -n ACTIVIDAD \
  --es nexoruta.prueba.almacenamiento leer
adb -s DISPOSITIVO logcat -d -s NexoRuta.StorageProof:I '*:S'
```

Resultados observados, en procesos diferentes:

```text
GUARDADO_OK: imagen cifrada; clave en SecureStorage.
REAPERTURA_OK: imagen recuperada; clave ausente bloqueada; archivos conservados; sin temporales.
```

La fase de clave ausente usa un adaptador de prueba que oculta la clave; no destruye claves ni evidencia real del teléfono. El diagnóstico solo se compila al pasar `ValidarAlmacenamiento=true`; el APK normal no incluye esas clases ni responde al extra de prueba. No se añade una pantalla de diagnóstico al flujo del repartidor. La imagen cifrada de prueba queda en su directorio aislado.

Al pasar del APK de diagnóstico al normal, limpiar los artefactos antes de recompilar: MAUI puede conservar el asset de prueba en una compilación incremental. La limpieza afecta únicamente `bin`/`obj`, no datos del teléfono:

```bash
dotnet clean src/NexoRuta.Mobile/NexoRuta.Mobile.csproj \
  --configuration Release --framework net10.0-android -p:RuntimeIdentifier=android-arm64
dotnet build src/NexoRuta.Mobile/NexoRuta.Mobile.csproj \
  --configuration Release --framework net10.0-android \
  -p:RuntimeIdentifier=android-arm64 -p:AndroidPackageFormats=apk \
  -p:ValidarAlmacenamiento=false --nologo
```

Tras limpiar, el build normal también terminó con **0 advertencias y 0 errores**. Se comprobó la ausencia del asset de prueba en el APK y de la clase/extra del diagnóstico en los ensamblados normal y recortado. El manifiesto generado tiene `allowBackup=false`, `fullBackupContent=false` y `dataExtractionRules=@xml/data_extraction_rules`. Configuración de backup inspeccionada; no se ejecutó un backup/restauración de Android ni una transferencia entre teléfonos.

La regresión del núcleo ejecutó **25 pruebas correctas**, adicionales a las 23 del almacenamiento: **48 pruebas en total**. No se ejecutaron pruebas de backend/frontend porque no se modificaron esos submódulos.

Al terminar se sustituyó el APK de diagnóstico por el normal mediante instalación conservando datos; su arranque en frío en el S21 informó `Status: ok` y proceso activo.

## Próxima integración

SQLite deberá asociar evidencia y comando de forma recuperable; si falla el commit, no anunciar entrega guardada. Coordinar archivos huérfanos, migración, conservación de pendientes y recuperación de claves. Este repositorio de archivos no guarda todavía jornadas ni registros de sincronización. La UI continúa siendo la plantilla.
