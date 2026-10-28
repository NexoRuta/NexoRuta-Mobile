#!/usr/bin/env bash
set -euo pipefail
umask 077
cd -- "$(dirname -- "${BASH_SOURCE[0]}")/.."
: "${NEXORUTA_ANDROID_KEYSTORE:?Indicá la ruta al keystore privado fuera del repositorio.}"
: "${NEXORUTA_ANDROID_KEY_ALIAS:?Indicá el alias de la clave de distribución.}"
: "${NEXORUTA_ANDROID_STORE_PASS_FILE:?Indicá el archivo privado con la contraseña del keystore.}"
: "${NEXORUTA_ANDROID_KEY_PASS_FILE:?Indicá el archivo privado con la contraseña de la clave.}"
: "${NEXORUTA_GCP_API_ORIGIN:?Indicá el origen HTTPS público actual del backend GCP.}"
: "${NEXORUTA_FIREBASE_ANDROID_CONFIG:?Indicá google-services.json fuera de Git.}"
: "${NEXORUTA_MAPBOX_PUBLIC_TOKEN:?Indicá el token público propio para preparar el mapa offline obligatorio.}"
[[ $NEXORUTA_MAPBOX_PUBLIC_TOKEN == pk.* ]] || { echo 'El mapa requiere un token público Mapbox (pk.).' >&2; exit 2; }
[[ -f $NEXORUTA_ANDROID_KEYSTORE && -f $NEXORUTA_ANDROID_STORE_PASS_FILE && -f $NEXORUTA_ANDROID_KEY_PASS_FILE && -f $NEXORUTA_FIREBASE_ANDROID_CONFIG ]] || {
    echo 'Falta un archivo de firma o configuración.' >&2; exit 2;
}
R8_JAR="$(python3 tools/preparar-r8.py)"
# file: evita exponer las contraseñas en argumentos, historial y logs de MSBuild.
dotnet publish src/NexoRuta.Mobile/NexoRuta.Mobile.csproj -c Release -f net10.0-android \
    -p:ValidarAlmacenamiento=false \
    -p:RuntimeIdentifier=android-arm64 -p:AndroidPackageFormats=apk -p:AndroidKeyStore=true \
    -p:AndroidSigningKeyStore="$NEXORUTA_ANDROID_KEYSTORE" -p:AndroidSigningKeyAlias="$NEXORUTA_ANDROID_KEY_ALIAS" \
    -p:AndroidSigningStorePass="file:$NEXORUTA_ANDROID_STORE_PASS_FILE" -p:AndroidSigningKeyPass="file:$NEXORUTA_ANDROID_KEY_PASS_FILE" \
    -p:AndroidR8JarPath="$R8_JAR" \
    -p:NexoRutaBackendTarget=GcpDemo -p:NexoRutaGcpApiOrigin="$NEXORUTA_GCP_API_ORIGIN" \
    -p:NexoRutaFirebaseAndroidConfig="$NEXORUTA_FIREBASE_ANDROID_CONFIG" \
    -p:NexoRutaMapboxPublicToken="$NEXORUTA_MAPBOX_PUBLIC_TOKEN"
find src/NexoRuta.Mobile/bin/Release/net10.0-android/android-arm64/publish -name '*-Signed.apk' -exec sha256sum {} \;
