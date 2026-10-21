# CI y distribución de Mobile

`global.json` fija SDK 10.0.401. La CI prueba Core, Api, Storage y Application por separado, y compila Android Release ARM64 con Java 21 y el workload del SDK sin actualizar manifiestos. Publica un APK **de verificación**, su checksum y resultados; no despliega ni instala. Ese APK usa firma de desarrollo y no lleva configuraciones externas ficticias. El workflow se ejecuta sobre el submódulo mobile aislado.

`tools/apk-distribucion.sh` genera el APK de distribución local con un keystore externo y contraseñas leídas desde archivos privados mediante `file:`. Requiere origen HTTPS, JSON Firebase y token público Mapbox propios: sin mapa configurado no se puede preparar la salida de la jornada. Configurar las variables que el script exige; guardar keystore y contraseñas fuera del checkout, con una copia recuperable bajo control del propietario. No crear otra clave al actualizar una instalación existente. El script no publica en tiendas ni despliega servicios.

Ejemplo de **rutas**, sin contraseñas ni claves:

```bash
export NEXORUTA_ANDROID_KEYSTORE=/ruta/privada/repartidor.keystore
export NEXORUTA_ANDROID_KEY_ALIAS=repartidor
export NEXORUTA_ANDROID_STORE_PASS_FILE=/ruta/privada/store-pass.txt
export NEXORUTA_ANDROID_KEY_PASS_FILE=/ruta/privada/key-pass.txt
export NEXORUTA_API_ORIGIN=https://api.su-dominio/
export NEXORUTA_FIREBASE_ANDROID_CONFIG=/ruta/privada/google-services.json
# Cargar también NEXORUTA_MAPBOX_PUBLIC_TOKEN desde la configuración privada.
mobile/tools/apk-distribucion.sh
```

La instalación debe verificar firma, versión y checksum; no borrar pendientes para cambiar la identidad de firma. El entorno HTTPS local se prepara mediante [herramientas locales](../tools/local/README.md). Los objetivos del proyecto son Android e iOS; no se incluyen builds de escritorio por herencia de la plantilla.

No se agregó un runner macOS facturable sin disponer de recursos/autorización Apple. iOS debe compilarse y probarse en esos recursos, con sus configuraciones y firma. Un workflow escrito o un APK compilado no acredita ejecución remota de CI ni aceptación funcional en un teléfono; la evidencia ejecutada se registra en `ejecucion-completa.md`.


La variante de desarrollo puede compilarse con `-p:ValidarAlmacenamiento=true -p:ApplicationId=uy.nexoruta.repartidor.validacion` y un directorio de artefactos separado. Incluye diagnósticos y, temporalmente por solicitud del usuario, omite la huella mediante el bool `OmitirVerificacionLocalParaDesarrollo` de `Seguridad/VerificacionLocal.cs`. Su TODO exige devolverlo a `false` para repetir la aceptación real de desbloqueo/rendición. Compilar ese modo bajo el ID habitual falla; el script de distribución lo desactiva expresamente.
