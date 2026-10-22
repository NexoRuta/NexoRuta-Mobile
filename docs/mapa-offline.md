# Preparación del mapa de la jornada

Estado al 20/10/2026: preparación durable y adaptador Android implementados; falta token Mapbox propio para comprobar descarga, renderizado y reapertura real en modo avión. El usuario confirmó que todavía no lo tiene. No se acredita la aceptación completa de T02.g/T10.

## Comportamiento

La hoja muestra el estado del mapa y permite abrir «Preparar mapa de la zona» antes de salir. La confirmación de carga/salida exige que el mapa de esa hoja esté preparado. Sin proveedor configurado, las direcciones siguen disponibles, se informa la dependencia y se mantiene bloqueada esa confirmación; no hay una omisión silenciosa del requisito.

La preparación comprende estilo, fuentes/sprites y teselas de la zona de las paradas, con zoom 6–14. La vista muestra el destino con un marcador y no incorpora indicaciones de giro. La huella de cobertura depende del proveedor, estilo, límites y zoom; cambios de orden o estado de los envíos no invalidan la descarga. Una zona o estilo diferente exige preparar el mapa nuevamente.

La descarga muestra avance y bytes informados por el SDK. Ese tamaño representa recursos de estilo más región; puede incluir recursos compartidos y no equivale al incremento físico del archivo ni a bytes nuevos transferidos. La app no inventa una estimación previa ni declara lista una descarga incompleta.

## Persistencia y recuperación

`ZonaMapa` pertenece a Core. `ServicioPreparacionMapa` coordina `IProveedorMapaOffline` y el repositorio existente de SQLite cifrado, sin migración nueva de esquema. La representación visual continúa detrás de `IProveedorMapas`/`IMapaDestino`.

El manifiesto se guarda en `Metadatos`, por cuenta y hoja. Solo contiene IDs aleatorios de región, huella, tamaño y fecha; las paradas/direcciones siguen en la hoja cifrada. El SDK conserva sus recursos cartográficos en su almacén nativo; no se afirma que ese almacén de mapas públicos use SQLite3MC ni que contenga las evidencias personales de entrega.

Cada intento usa una región nueva. El manifiesto registra el intento antes de iniciarlo; la región vigente solo se reemplaza después de comprobar estilo y teselas en el SDK. Una cancelación o falta de espacio no reemplaza el mapa anterior. Tras reinicio se limpia el intento interrumpido. Una región retirada cuya eliminación falle queda registrada para repetir la limpieza; se conserva el mapa nuevo verificado y se impiden descargas adicionales hasta aclarar esa limpieza. No se borran estilos compartidos con otra hoja válida.

La publicación local termina su escritura antes de atender una cancelación tardía. Al volver a consultar se interroga al SDK: una marca persistida por sí sola no acredita que sigan presentes todos los recursos.

## Adaptador Android y límite iOS

El paquete fijado es `Indiko.Maui.Controls.MapBox` 1.8.0, commit `f2a0af42f5fbefceeeadb4726b9aaafa193f03e5`, con Maps 11.30.1/Common 24.30.1. Su callback de finalización de región ignora el resultado de la descarga del estilo. Por eso el control visual se conserva, pero la preparación usa un facade Java propio compilado contra los mismos SDK: espera ambos recursos, informa bytes, conserva los cancelables y consulta región/estilo después de reiniciar. La limpieza espera confirmación nativa y acepta «no existe» como resultado idempotente.

El puente JNI solo intercambia escalares y estado JSON breve; no registra token ni dirección. Las consultas/borrados que esperan callbacks se ejecutan fuera del hilo UI y tienen un máximo de diez segundos. La clase se conserva en el APK mediante configuración ProGuard. El ensayo de fondo del 21/10 detectó `ClassNotFoundException`: se corrigió usando explícitamente el cargador de clases de la aplicación. La repetición en S21 pasó en UI y fondo. [Sobrecarga con ClassLoader](https://learn.microsoft.com/en-us/dotnet/api/java.lang.class.forname?view=net-android-36.0).

En iOS están las interfaces, coordinación, persistencia, pantallas y representación visual compartidas. **Falta el puente nativo equivalente de descarga/verificación** y su compilación/prueba en Mac/iPhone. El código no presenta una preparación iOS como verificada con los callbacks incompletos del adaptador actual.

Fuentes revisadas: [implementación Android del paquete fijado](https://github.com/0xc3u/Indiko.Maui.Controls.MapBox/blob/f2a0af42f5fbefceeeadb4726b9aaafa193f03e5/native/android/indikomapboxkit/src/main/kotlin/ch/indiko/mapbox/IKMapView.kt), [implementación iOS](https://github.com/0xc3u/Indiko.Maui.Controls.MapBox/blob/f2a0af42f5fbefceeeadb4726b9aaafa193f03e5/native/ios/IndikoMapboxKit/Sources/IndikoMapboxKit/IKMapView.swift) y [documentación oficial de mapas offline](https://docs.mapbox.com/android/maps/guides/offline/). Las firmas Java se contrastaron con los AAR realmente incluidos en NuGet.

## Evidencia y aceptación pendiente

Core: 34 pruebas correctas; Application: 52 al repetir la batería el 21/10, incluidos diez casos posteriores de ubicación. Los 13 casos añadidos para mapas cubren huella/cultura/cobertura, cuenta y cifrado, reapertura, falta de recursos en el SDK, descarga parcial, cancelación antes del commit, intentos interrumpidos y limpieza diferida sin perder el mapa válido.

El puente fue empaquetado y llamado realmente en S21 mediante la variante `uy.nexoruta.repartidor.validacion`, con el diagnóstico condicionado a `ValidarAlmacenamiento=true`. Ese ensayo no usa token ni descarga y solo acredita interoperabilidad Java/JNI. El APK habitual excluye ese punto de diagnóstico.

Para cerrar la aceptación real, configurar un token público propio en `MAPBOX_PUBLIC_TOKEN` de `.local/mobile/local.env` y pasarlo al build como `NexoRutaMapboxPublicToken`, sin imprimirlo. Descargar una zona real, comprobar recursos/tamaño, cancelar una actualización conservando la anterior, terminar el proceso, desactivar red, volver a desbloquear y verificar mapa base/marcador en varias paradas y zoom 6–14. También falta provocar espacio insuficiente en un entorno controlado y contrastar el resultado nativo. No se usaron tokens de ejemplos ajenos ni se activó facturación.
