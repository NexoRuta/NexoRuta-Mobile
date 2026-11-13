# Integración local de NexoRuta Mobile

Este entorno usa su propio proyecto Compose y volúmenes, con PostgreSQL/PostGIS, roles restringidos de API/Worker, RabbitMQ, Mailpit y API HTTPS. No usa la base de otros proyectos ni publica puertos de PostgreSQL. No equivale a infraestructura de producción.

Desde el checkout combinado, con Docker, Python 3 y OpenSSL:

```bash
mobile/tools/local/iniciar.sh
```

`preparar.py` crea y conserva `.local/mobile/local.env`, `bootstrap.json` y certificados. Ese directorio está ignorado por Git; los archivos privados se crean con permisos restringidos. No se imprimen contraseñas. Las cuentas iniciales y sus claves se consultan **localmente** en `bootstrap.json`; no las copies a documentación, logs ni mensajes. Repetir la preparación conserva claves, IDs y cuentas.

- API HTTPS: `https://localhost:5843`; CA pública para herramientas: `.local/mobile/tls/ca.crt`.
- API HTTP del host: `http://127.0.0.1:5841`, solo loopback, para las webs locales.
- Buzón SMTP de prueba: `http://localhost:5855`. No envía correos a Internet.
- Worker RabbitMQ activo. Worker push es optativo: requiere ADC autorizado para `nexoruta-mobile` y una instalación real registrada. El archivo ADC se monta en modo lectura; no se copia a Git ni al APK.

Comprobar HTTPS sin desactivar validación:

```bash
curl --cacert .local/mobile/tls/ca.crt https://localhost:5843/ready
```

Prueba integrada de API, outbox/RabbitMQ, correo en Mailpit y cancelación pública:

```bash
python3 mobile/tools/local/verificar.py
python3 mobile/tools/local/demo.py
```

La segunda orden prepara una hoja ficticia con cuenta de repartidor separada del administrador. Sus credenciales quedan únicamente en `.local/mobile/demo.json`. No crea operaciones de entrega ni inicia la jornada en nombre del repartidor. Repetirla conserva la cuenta y la hoja existente del día. Para una fecha futura de prueba (por ejemplo, para no reutilizar una jornada ya finalizada), se puede ejecutar `NEXORUTA_DEMO_DATE=2026-10-28 python3 mobile/tools/local/demo.py` desde WSL. La fecha no puede ser anterior al día local. Para crear una cuenta y vehículo ficticios adicionales en la misma fecha, usar `NEXORUTA_DEMO_SUFFIX=aceptacion`; cada sufijo conserva sus credenciales en `demo-<sufijo>.json` e identifica un bulto diferente. La hoja futura no se puede iniciar hasta esa fecha.

Para el Worker de push, con ADC ya autorizado y una instalación registrada desde la app:

```bash
docker compose --env-file .local/mobile/local.env -f mobile/tools/local/compose.yaml --profile push up -d worker-push
```

El ensayo ejecutado, sus límites y las dependencias aún externas están en [validación local y S21](../../docs/validacion-local-s21.md).

Para apagar sin eliminar datos:

```bash
docker compose --env-file .local/mobile/local.env -f mobile/tools/local/compose.yaml stop
```

No usar `down -v` para actualizar. Las migraciones, provisión de roles y bootstrap se ejecutan por comandos separados antes de iniciar runtime. El migrador tiene privilegios propios; API y Worker no usan su login. Si una migración requiere corregir datos, el arranque se detiene sin rellenarlos arbitrariamente.

## Contrato de reglas y datos de demostración

La base actual utiliza el volumen `nexoruta-mobile-local_database_cu04`; el volumen anterior se conserva. Los envíos previos carecen de una publicación capturada al crearse y no se les inventa una. Los scripts de demostración publican reglas ficticias expresamente antes del primer alta. La clave de evidencia `EVIDENCE_KEY` se genera separada de seguimiento y push, en el archivo privado de entorno.

## Emulador Windows con la API habitual

Este recorrido reutiliza **la API que ya escucha en `http://localhost:5000`**. No ejecutar este Compose completo: prepara otro entorno con base propia. El helper inicia únicamente un gateway Nginx en `127.0.0.1:5843`, usando el `nginx.conf` existente con upstream `host.docker.internal:5000` y los certificados existentes de `.local/mobile/tls` (o `-TlsDirectory`). No modifica confianza global, claves, base, cuentas ni los portales.

Con Docker y el emulador arrancados, desde la raíz del superproyecto:

```powershell
powershell -NoProfile -File mobile/tools/local/reconectar-emulador.ps1 -Serial emulator-5554 -ProbeApp
```

Usar el serial del emulador elegido, siempre explícito. El script comprueba la API, inicia/reutiliza únicamente su gateway identificado en la red existente `nexoruta_default`, valida CA/SAN mediante el contexto SSL estándar de Python (igual que los helpers locales existentes), restablece `adb -s SERIAL reverse tcp:5843 tcp:5843` y puede repetirse después de reiniciar Android. Rechaza seriales de teléfonos y contenedores/puertos/montajes ajenos; no los elimina. No inicia ni borra un AVD.

`-ProbeApp` abre **NexoRuta Repartidor** y ejecuta un GET de solo lectura a `/ready` con el mismo `ClienteSesionMovil` y transporte TLS que usa la app. Solo existe en **Debug + CA local**, se activa mediante un identificador de prueba explícito, no inicia sesión ni renueva tokens ni modifica operaciones offline. Un HTTP200 demuestra conectividad, no autenticación. Sin `-ProbeApp`, el helper solo prepara gateway y reverse.

Logs:

```powershell
docker logs --tail 100 nexoruta-mobile-gateway
adb -s emulator-5554 logcat -d -s NexoRuta.Connection:I '*:S'
```

Cada resultado de prueba también queda en `%LOCALAPPDATA%\NexoRuta\mobile-emulator\probe-*.log`; allí se genera la configuración del gateway, **sin claves**. Los certificados y claves existentes no se copian: se montan de solo lectura. No usar `--insecure`, aceptación universal de certificados, `adb uninstall` ni borrado de almacenamiento.

Regresión aislada del rechazo de un gateway ajeno (sin Docker ni emulador reales; deja el directorio TEMP indicado para inspección):

```powershell
powershell -NoProfile -File mobile/tools/local/tests/reconectar-emulador.test.ps1 -HelperPath mobile/tools/local/reconectar-emulador.ps1
```

Para reconstruir desde fuentes, comprobar primero la ABI con `adb -s SERIAL shell getprop ro.product.cpu.abilist`. El AVD `f7a-qa-api36` usa `x86_64` (`android-x64`):

```powershell
powershell -NoProfile -File mobile/tools/local/compilar-emulador.ps1 -Serial emulator-5554
```

El helper incorpora `Mapbox__PublicAccessToken` de la `.env` privada del superproyecto mediante la propiedad de entorno MSBuild `NexoRutaMapboxPublicToken`. No imprime el valor ni lo pone en argumentos, archivos versionados o configuración global; restaura la variable al terminar. Sin ese metadato, el APK no puede mostrar mapas aunque la API y los portales ya tengan Mapbox configurado. No crea tokens ni cambia proveedores, planes o facturación. Los tests automatizados siguen usando proveedores controlados.

La URL efectiva de este target es `https://localhost:5843/`. Verificar la firma del APK construido y del APK instalado antes de actualizar **el mismo paquete** mediante `adb -s SERIAL install -r RUTA_APK`. No cambiar la firma ni desinstalar para resolver incompatibilidades. Conservar cualquier configuración privada de compilación que ya se utilice; no agregar archivos Firebase inexistentes ni cambiar proveedores para este recorrido.

## Actualizar un recorrido con operaciones pendientes

“Mi recorrido → Actualizar” conserva el contenido visible mientras sincroniza. La sincronización tiene un plazo de 45 segundos, incluida la espera del semáforo compartido; la consulta posterior de cambios tiene 2 segundos y la lectura local 5 segundos. Al terminar se informa si quedan operaciones, hubo un error reintentable o el servidor confirmó el cierre. El indicador local de cierre pendiente no equivale a un cierre confirmado.

Si quedan pendientes, volver a usar Actualizar: se conservan los mismos IDs, sobres durables y orden causal. No borrar la cola, adelantar el cierre ni generar otro. El límite existente de cien operaciones por lote se mantiene; las posiciones históricas se envían antes del cierre que las sigue. Un cierre confirmado no sustituye la recepción física ni la liberación manual de disponibilidad en Backoffice.

## Android por Wi-Fi

Con el entorno Android cargado y el S21 ya autorizado en `adb devices`:

```bash
adb reverse tcp:5843 tcp:5843
R8_JAR="$(python3 mobile/tools/preparar-r8.py)"
dotnet build mobile/src/NexoRuta.Mobile/NexoRuta.Mobile.csproj -c Release -f net10.0-android \
  -p:RuntimeIdentifier=android-arm64 -p:AndroidPackageFormats=apk \
  -p:AndroidR8JarPath="$R8_JAR" \
  -p:NexoRutaBackendTarget=Docker \
  -p:NexoRutaAndroidDevelopmentCa="$PWD/.local/mobile/tls/ca.crt" \
  -p:NexoRutaFirebaseAndroidConfig="$PWD/mobile/google-services.json"
```

El APK local apunta al backend Docker (`https://localhost:5843/`) y confía en esa CA únicamente para `localhost`. Otros dominios conservan los certificados del sistema y no se habilita HTTP. Nunca incorpora la clave privada de la CA, ni omite la validación TLS. El SDK Android incluido trae R8 8.11, que no entiende metadata Kotlin 2.4 presente en dependencias Android actuales; `tools/preparar-r8.py` descarga R8 9.1.31 desde Maven de Google y verifica SHA-256 antes de usarlo. Para probar el backend público de la demo GCP, compilá con `-p:NexoRutaBackendTarget=GcpDemo -p:NexoRutaGcpApiOrigin=https://api.<IP-actual>.sslip.io/` y omití la CA local. La dirección actual se obtiene de `tofu -chdir=infra/gcp/demo output -json endpoints` (propiedad `api`). La compilación de distribución ya selecciona `GcpDemo`.

Al compilar un APK `Debug` para instalar manualmente con `adb install`, agregá `-p:EmbedAssembliesIntoApk=true`. Sin esta propiedad, Android puede intentar Fast Deployment y cerrar la app cuando el APK se instala fuera del despliegue del IDE. Las compilaciones `Release` de distribución empaquetan los assemblies normalmente.

Se utiliza una sola aplicación: **NexoRuta Repartidor**, identificador `uy.nexoruta.repartidor`. Actualizar esa instalación con `adb install -r`; no instalar variantes con otro identificador. Las antiguas instalaciones de validación y prototipo se retiraron del S21 conservando sus datos. Las pruebas futuras deben usar la aplicación habitual y cuentas ficticias, sin borrar trabajo pendiente.

La firma debe coincidir con la app instalada para actualizar con `adb install -r`. No desinstales una app con pendientes ni cambies de firma para sortear un rechazo de actualización.

## Webs del host

En terminales separadas, desde la raíz combinada:

```bash
ASPNETCORE_ENVIRONMENT=Development ASPNETCORE_URLS=http://127.0.0.1:5851 Api__BaseAddress=http://127.0.0.1:5841/ \
  dotnet run --project frontend/src/NexoRuta.Backoffice -c Release --no-launch-profile
ASPNETCORE_ENVIRONMENT=Development ASPNETCORE_URLS=http://127.0.0.1:5852 Api__BaseAddress=http://127.0.0.1:5841/ Tracking__BaseAddress=http://127.0.0.1:5853/ \
  dotnet run --project frontend/src/NexoRuta.Commerce -c Release --no-launch-profile
ASPNETCORE_ENVIRONMENT=Development ASPNETCORE_URLS=http://127.0.0.1:5853 Api__BaseAddress=http://127.0.0.1:5841/ \
  dotnet run --project frontend/src/NexoRuta.Tracking -c Release --no-launch-profile
```

## Dependencias externas

- Geoapify: cuenta Free y clave backend en `GEOAPIFY_API_KEY` de `local.env`. Sin clave, la búsqueda informa 503 y deja confirmar el punto manualmente. La clave no se envía al navegador ni al APK.
- Mapbox: token público `pk.` de su proyecto. Configurar `Mapbox__PublicAccessToken` en Commerce/Backoffice y `NexoRutaMapboxPublicToken` al compilar MAUI. No usar tokens privados `sk.` ni credenciales ajenas de ejemplos públicos.
- Firebase Android: JSON del proyecto correcto y ADC en la ruta de `NEXORUTA_GOOGLE_ADC_FILE`. Activar el perfil push solo al probar destinatarios reales autorizados; aceptación FCM no demuestra visualización del aviso.
- Producción: dominio/HTTPS, SMTP real, despliegue e IaC propios. Este entorno no aprovisiona recursos facturables ni abre puertos hacia la LAN/Internet.
- iOS: Mac/Xcode, cuenta/equipo Apple, configuración Firebase iOS y APNs. No se afirma compilación ni recepción iOS desde Ubuntu.

## Datos descartables de desarrollo

Este entorno local es de desarrollo: sus cuentas y envíos de demostración, base local y datos móviles pueden reiniciarse o romperse a conciencia para probar cambios. No se implementan compatibilidades con APIs, bases ni sesiones de versiones anteriores. Mantener secretos fuera del repositorio y no reutilizar datos de producción.
