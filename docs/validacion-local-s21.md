# Integración local y S21 — 20/10/2026

Se preparó y ejecutó el entorno de [herramientas locales](../tools/local/README.md). Usa volúmenes separados de los demás proyectos, PostgreSQL/PostGIS, API y Worker con roles restringidos, RabbitMQ, Mailpit y nginx con HTTPS. El migrador y el bootstrap tienen comandos/roles separados. Las migraciones vigentes se aplicaron a **esta base local aislada**.

Los certificados, claves y cuentas ficticias están en `.local/mobile`, ignorado por Git y con permisos privados. La app incorpora únicamente la CA pública de desarrollo y solo confía en ella para `localhost`; usa `adb reverse` por Wi-Fi. Se compiló también la variante sin configuración local y se comprobó que el APK y su manifiesto no contienen la CA ni la configuración de confianza local. No se desactivó la validación TLS.

## Evidencia ejecutada

1. `iniciar.sh` terminó con `/health/ready` HTTPS saludable. `verificar.py` recorrió ingreso de comercio, alta con correo obligatorio, confirmación manual del punto, outbox, RabbitMQ, generación de seguimiento, correo SMTP en Mailpit y cancelación pública idempotente con su código. Solo se usaron destinatarios ficticios locales.
2. `demo.py` creó por API un rol con únicamente `reparto.ejecutar`, su usuario/perfil, configuración explícita, vehículo, envío, destino confirmado, recepción, planificación y despacho. Repetirlo conserva la cuenta/hoja; no imprime claves.
3. APK Android Release ARM64 instalado en el **S21 SM-G991U, Android 14**, por ADB inalámbrico, actualizando sin desinstalar. La UI acreditó ingreso contra HTTPS, descarga de la hoja y registro de avisos en el servidor. Firebase FID se conserva cifrado en la base; nunca se incluyó en logs o documentación.
4. Worker push iniciado con ADC del usuario montado en lectura. Se reasignó un envío ficticio de una ruta despachada a una planificación del día siguiente. Se comprobó expresamente **ausencia del proceso Android antes de enviar**, sin `force-stop`. Android volvió a iniciar el receptor Firebase y publicó la notificación de esa reasignación. El registro `EntregasPush` acreditó `FcmAceptada` con ID del proveedor.
5. El título genérico apareció en el panel de Android. Al tocar una notificación, la app abrió el desbloqueo local. El usuario completó la verificación en el teléfono; no se solicitó ni capturó su PIN. Se comprobaron aviso obsoleto de la ruta anterior y aviso vigente del nuevo ensayo, detalle y parada suspendida. El botón `Entendido` produjo recepción, apertura y confirmación persistidas por separado.
6. Cierre de sesión sin pendientes operativos comprobado desde la UI: volvió al ingreso y conservó el almacén cifrado. La protección por operaciones/borradores pendientes mantiene sus pruebas controladas; todavía no se acredita ese caso desde el S21.

La reasignación del ensayo ocurrió **antes de cargar**, con el bulto físicamente en depósito. Esto acredita el transporte FCM real con proceso cerrado y el circuito de consulta/desbloqueo; no reemplaza la aceptación de carga, entrega offline, rendición y recepción durante un reparto. Se corrigió la presentación para no pedir rendición de un bulto nunca cargado: la indicación usa custodia remota y conserva una carga local aún no sincronizada.

## Límites y dependencias

- La API/Worker son locales, con túnel ADB. No se desplegó un servicio accesible fuera de este entorno ni se implementó IaC de producción.
- Correo de prueba en Mailpit; no se enviaron códigos a buzones reales. SMTP y dominio/HTTPS de producción siguen pendientes.
- Geoapify Free está integrado detrás de interfaz y probado con respuestas controladas; falta clave propia para validar resultados reales. Mapbox requiere token público propio para comprobar mapa base offline, tamaño, preparación durable y reapertura sin red. No se usaron credenciales de ejemplos ajenos.
- Android mantiene tres advertencias nativas Mapbox sobre páginas de 16 KB para Android 16. El S21 de prueba usa Android 14. Firma instalada de desarrollo; hay script de distribución, sin keystore definitivo ni ejecución remota de CI acreditados.
- Cámara, scanner, firma, entrega/intento offline completo, rendición biométrica, cierre con faltantes, frecuencia/batería GPS y rotación/baja/reingreso de avisos necesitan completar aceptación en el teléfono.
- iOS requiere binding Firebase con FID compatible, configuración Firebase/APNs, Mac/Xcode y dispositivo/recursos Apple. No se acredita compilación ni recepción iOS.
- La integración parcial de CU-04 fija la publicación al crear el envío y descarga los requisitos y motivos de cada parada. Se retiró la duplicación. [Contrato y alcance](reglas-envio.md). Este corte no acredita CU-04 completo.

Logs temporales de esta sesión: `/tmp/nexoruta-local-start-final.log`, `/tmp/nexoruta-local-smoke-final.log`, `/tmp/nexoruta-push-cerrado-real.log`, `/tmp/nexoruta-push-cerrado-ledger.log` y `/tmp/nexoruta-push-cerrado-confirmaciones.log`. No son artefactos permanentes ni contienen credenciales.

## Reactivación de avisos tras reingresar

El ensayo posterior encontró `Java.Lang.IllegalStateException`: la asignación `FirebaseMessaging.AutoInitEnabled` ejecutaba `fetchFid` y `Tasks.await` en el hilo principal. Se comprobó con frames de métodos, sin registrar mensajes, FID ni credenciales. Se movieron tanto esa asignación como `Register()` al hilo de trabajo. La build corregida se instaló en el S21; ingreso y dos activaciones consecutivas mostraron «Registro de avisos enviado al servidor» sin el error anterior. La instrumentación temporal de diagnóstico se retiró.


## Comprobación de la integración parcial CU-04

En `uy.nexoruta.repartidor.validacion`, con datos ficticios de la base aislada nueva: ingreso HTTPS, descarga, apertura de hoja/parada y visualización de nombre/foto exigidos por la publicación capturada, sin error de contrato. El usuario dejó disponible el S21 para la prueba. No se inició jornada ni se declararon entregas por él; la variante no contiene Firebase y no acredita push.

La primera compilación incremental conservó el ID habitual en el manifiesto pese al nombre de archivo de variante. Se detectó antes de ingresar credenciales, se repuso el APK habitual con Firebase y se repitió en un directorio de artefactos limpio. Se verificaron ID y etiqueta del APK antes de instalar. `pm list packages --user 0` comprobó ambas aplicaciones. Los datos de la app habitual no se borraron. El APK habitual también se reconstruyó limpio y se comprobó su ID/etiqueta.

Logs de este corte: `/tmp/nexoruta-cu04-native-login.log`, `...-native-download.log`, `...-native-route.log`, `...-native-stop.log`, `/tmp/nexoruta-cu04-android-canonico-final.log` y `/tmp/nexoruta-cu04-cierre-dirigido.log`. La evidencia anterior de push corresponde a la base previa, cuyo volumen se conserva; no se atribuye a la variante.


## Mapa, firma y ubicación — 21/10/2026

**Registro histórico del 21/10, reemplazado el 27/10.** En ese corte, el bypass temporal era un bool limitado a la variante diagnóstica; se comprobó el puente Java del mapa en UI y fondo y la firma sintética cifrada con igualdad binaria. El comportamiento vigente se decide en compilación: Debug habitual omite la verificación local; Release habitual/CI la exige. La variante diagnóstica Release conserva su bypass bajo ID separado. Ninguna prueba con bypass se registra como biometría real. Ver [configuración vigente](ci-distribucion.md).

Se reingresó con la misma cuenta ficticia y se descargó la hoja del día. La pantalla de mapa informa el token pendiente y deshabilita la descarga. Inicio de jornada por UI, servicio de ubicación en segundo plano, diez puntos aplicados a intervalos observados de 30,00–30,33 segundos y cierre/detención del servicio quedaron comprobados. El cierre terminó sin registros pendientes. No hubo carga ni entrega de bultos. La interrupción controlada de HTTPS local se restauró saludable, con dos puntos diferidos más de treinta segundos, demora máxima de 60,76 segundos y posterior confirmación de las diez posiciones sin duplicados. Se probó falta de acceso a la API; la Wi-Fi del teléfono permaneció encendida. [Detalles y aceptación restante](ubicacion-jornada.md).

La preparación durable del mapa está implementada y probada con proveedor controlado; su descarga/renderizado/reapertura reales siguen pendientes de token propio. iOS requiere también completar el puente nativo offline, además del binding push y recursos Apple. El ensayo corto de ubicación no acredita una jornada con pantalla apagada, consumo ni precisión. Siguen pendientes cámara/lector y recorrido operativo integral en el teléfono.


Se corrigió el estado desactualizado del listado al regresar desde una jornada cerrada: MainPage vuelve a leer la vista local. El APK de variante final mostró `Finalizada` y permitió reabrir el detalle sin reactivar el servicio de ubicación. La consulta SQL final confirmó un inicio, diez posiciones y un cierre aplicados, sin duplicados. La firma de desarrollo de ambas variantes fue verificada; siguen sin firma definitiva de distribución.
