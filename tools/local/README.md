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
curl --cacert .local/mobile/tls/ca.crt https://localhost:5843/health/ready
```

Prueba integrada de API, outbox/RabbitMQ, correo en Mailpit y cancelación pública:

```bash
python3 mobile/tools/local/verificar.py
python3 mobile/tools/local/demo.py
```

La segunda orden prepara una hoja ficticia con cuenta de repartidor separada del administrador. Sus credenciales quedan únicamente en `.local/mobile/demo.json`. No crea operaciones de entrega ni inicia la jornada en nombre del repartidor. Repetirla conserva la cuenta y la hoja existente del día.

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

## Android por Wi-Fi

Con el entorno Android cargado y el S21 ya autorizado en `adb devices`:

```bash
adb reverse tcp:5843 tcp:5843
dotnet build mobile/src/NexoRuta.Mobile/NexoRuta.Mobile.csproj -c Release -f net10.0-android \
  -p:RuntimeIdentifier=android-arm64 -p:AndroidPackageFormats=apk \
  -p:NexoRutaApiOrigin=https://localhost:5843/ \
  -p:NexoRutaAndroidDevelopmentCa="$PWD/.local/mobile/tls/ca.crt" \
  -p:NexoRutaFirebaseAndroidConfig="$PWD/mobile/google-services.json"
```

El APK local confía en esa CA únicamente para `localhost`. Otros dominios conservan los certificados del sistema y no se habilita HTTP. Nunca incorpora la clave privada de la CA, ni omite la validación TLS. Sin `NexoRutaAndroidDevelopmentCa` el recurso de desarrollo no se agrega. La compilación de distribución usa `tools/apk-distribucion.sh`, sin esta propiedad.

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
