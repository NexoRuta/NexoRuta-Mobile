# Avisos remotos y reasignación

Estado: 20/10/2026. **Push FCM real comprobado en el S21 con el proceso de la app ausente antes de enviar.** API/Worker usan el entorno local HTTPS, PostgreSQL restringido y ADC del usuario. Android publicó el aviso; se verificaron desbloqueo local, consulta de vigencia, parada suspendida y confirmaciones de recepción/apertura/lectura explícita. [Evidencia y límites](validacion-local-s21.md). Falta aceptación operativa durante un reparto con carga y sin red; iOS mantiene dependencias de SDK y recursos Apple.

## Recorrido

1. Backoffice permite crear una planificación, incluso un borrador explícito sin paradas para una fecha futura. Un borrador vacío no puede despacharse ni descargarse al móvil.
2. La postergación envía las versiones del envío y de la ruta destino. El servidor valida operador, estado, fecha, capacidad y repartidor; suspende la parada anterior y asigna a la nueva ruta en una transacción. La custodia física no cambia.
3. Si hay un repartidor actual en reparto, se registra un aviso dirigido solo a él y un mensaje `RepartoModificado/PushFcm` en el outbox. El repartidor futuro no recibe aviso urgente.
4. El Worker reserva el mensaje y obtiene sus destinos mediante funciones SQL que verifican la reserva, el aviso, las pertenencias, el permiso y las versiones de sesión/credencial. No tiene permiso de lectura general sobre las tablas operativas. Descifra los FID únicamente para enviar.
5. Se conserva cada resultado por aviso, instalación y hash de FID. Los destinos aceptados ya persistidos no se repiten al reintentar una falla parcial. Un FID inválido no desactiva otro que haya rotado mientras se enviaba. Una caída entre aceptación por FCM y persistencia puede producir un duplicado; el cliente agrupa por aviso y vuelve a consultar vigencia.
6. Android registra permisos y FID; recibe un título/texto genérico y datos mínimos: `v`, `notificacionId`, `operadorId`, `accesoId`. Nunca recibe una orden de cambio de estado ni datos del destinatario. Guarda la referencia cifrada y rechaza otra cuenta.
7. Una recepción observada puede acusarse como `Recibido` sin abrir la pantalla. Al tocar el aviso se exige desbloqueo local, se consulta la API y, si sigue vigente, se descarga la vista actual de la ruta. `Abierto` y la confirmación explícita `Confirmado` se guardan por separado.
8. La vista remota se guarda aparte de la hoja original y de los sobres pendientes. Una parada suspendida impide una nueva entrega; los hechos/evidencias registrados antes sin red se conservan. Una entrega física offline concurrente con cancelación/reasignación se concilia automáticamente si el servidor comprueba carga y custodia original: conserva la entrega, suspende la parada futura y valida su prueba con la política de la jornada. El aviso anterior deja de estar vigente y el Worker omite el envío pendiente. Una devolución posterior es otro circuito; el despachador no aprueba/rechaza la entrega (D04/D09 del plan). Las incoherencias de custodia siguen como conflicto, sin forzar una entrega.

La cancelación autenticada está implementada desde Comercio/Backoffice: `POST /api/envios/{envioId}/cancelacion` con versión y motivo. Genera auditoría inmutable y el mismo outbox `RepartoModificado`, dirigido al custodio actual aunque ya exista una ruta futura. No notifica al repartidor futuro ni cambia la custodia. El aviso Android distingue el paquete todavía no entregado del ya recibido sin conexión. La cancelación pública desde Tracking también está implementada con código por correo, probado localmente en Mailpit. [Contrato](../../backend/docs/cu/cancelacion-envios.md).
9. El repartidor declara la rendición; depósito confirma recepción. El nuevo despacho permanece bloqueado hasta que los bultos estén físicamente recibidos. Una asignación lógica no mueve paquetes entre vehículos.

`Publicado` en el outbox significa que terminó el procesamiento: aceptación del proveedor, aviso obsoleto o ausencia de destinos. No acredita recepción, apertura ni lectura. Un aviso confirmado, una ruta finalizada o una versión de envío superada no generan una instrucción urgente vigente.

## Configuración local necesaria

No guardar credenciales, registros FID, tokens de sesión ni archivos de servicios en Git o bitácoras. Los archivos Firebase del cliente están ignorados en mobile; mantenerlos fuera del checkout.

- **Firebase Android:** registrar `uy.nexoruta.repartidor` y compilar con `-p:NexoRutaFirebaseAndroidConfig=/ruta/externa/google-services.json`. Si se indica una ruta inexistente, la compilación falla. Sin configuración, la app explica la dependencia.
- **API HTTPS:** compilar con `-p:NexoRutaBackendTarget=GcpDemo -p:NexoRutaGcpApiOrigin=https://api.su-dominio/`. El teléfono debe confiar en su certificado y tener acceso a ese origen. No se permite eludir TLS. Para Docker local, usar `NexoRutaBackendTarget=Docker` y la CA local documentada en `tools/local/README.md`.
- **API y Worker:** proporcionar la misma clave AES de 32 bytes en Base64 por configuración `Push:RegistrationEncryptionKey`. Debe persistir en el gestor de secretos del despliegue; perderla impide descifrar registros existentes. Las pruebas generan claves temporales aisladas.
- **Worker de push:** usar `Messaging:Transport=Push`, `Push:ProjectId` y credenciales de aplicación de Google mediante ADC, con autorización de envío FCM. Reutiliza la conexión PostgreSQL de Worker con su rol restringido. No requiere lectura directa de datos comerciales ni de instalaciones. Las credenciales administrativas nunca forman parte del APK.
- **Base de datos:** aplicar mediante el procedimiento de migración del repositorio, incluyendo `20261019185305_IntegratePushOutboxDispatch` después de las migraciones de sesiones y permisos móviles. Se verificó sobre bases temporales y se aplicó al entorno local aislado del 20/10.

La creación de infraestructura y sus secretos sigue pendiente de IaC. El proyecto Firebase del cliente y la identidad de envío del Worker son configuraciones distintas; descargar el JSON Android no autentica al Worker.

## Android e iOS

Android usa `Xamarin.Firebase.Messaging` 125.1.3, registro explícito por FID y canal `nexoruta.avisos`. La recepción en primer plano crea el aviso local; en segundo plano, FCM entrega la notificación del sistema y sus datos al tocarla. El callback no realiza trabajo operativo ni consultas largas. [Registro y recepción oficiales](https://firebase.google.com/docs/cloud-messaging/android/get-started), [comportamiento en segundo plano](https://firebase.google.com/docs/cloud-messaging/android/receive-messages).

El binding iOS instalado, `AdamE.Firebase.iOS.CloudMessaging` 12.10.0, usa una versión nativa que no expone el registro por FID descrito por la documentación actual. No se sustituye por un token de registro antiguo ni se declara una implementación ficticia. Se necesita un binding compatible, APNs y validación en Mac/iPhone. [API nativa 12.10.0](https://github.com/firebase/firebase-ios-sdk/blob/12.10.0/FirebaseMessaging/Sources/Public/FirebaseMessaging/FIRMessaging.h), [registro iOS actual](https://firebase.google.com/docs/cloud-messaging/ios/get-started).

## Aceptación operativa restante

Con Firebase y HTTPS configurados, preparar dos repartidores/rutas y un paquete realmente cargado por el actual. Instalar el APK, ingresar con su cuenta, aceptar notificaciones y comprobar el registro protegido en la API. Cerrar la app desde recientes, postergar desde Backoffice y verificar el aviso en el S21. No confundir quitar de recientes con «Forzar detención» de Android.

Tocar el aviso con el teléfono bloqueado, verificar desbloqueo, detalle vigente, parada suspendida y rendición conservada. Repetir con permiso denegado, FID renovado, aviso repetido/obsoleto y entrega registrada sin red. Confirmar rendición y recepción antes de despachar la ruta futura. Registrar por separado aceptación FCM, recepción observada, apertura y confirmación. La prueba controlada del Worker no sustituye esta prueba real.


## Preparación real verificada

El JSON aportado por el usuario coincide con `nexoruta-mobile` y `uy.nexoruta.repartidor`, está ignorado por Git y se incorporó al APK por la propiedad de compilación documentada. El APK Firebase Release se instaló sobre el anterior sin borrar datos; su arranque en frío devolvió `Status: ok`. El SDK confirmó «FirebaseApp initialization successful», sin excepción fatal en el proceso. No se registraron claves ni FID en la evidencia.

El usuario completó Google ADC localmente. Se obtuvo un token sin mostrarlo ni persistirlo en el repositorio y se consultó `testIamPermissions` del proyecto: **`cloudmessaging.messages.create` confirmado**. Esa consulta no cambia IAM ni envía mensajes. [Permiso de envío FCM](https://docs.cloud.google.com/iam/docs/roles-permissions/firebasecloudmessaging).

Ese registro histórico del 19/10 acreditaba configuración del cliente y autorización. El 20/10 se completó la prueba local con Worker conectado, registro real desde la app, reasignación por API y FCM con el proceso cerrado; ver [validación del S21](validacion-local-s21.md). No equivale a disponer de dominio/SMTP/IaC de producción ni a probar todas las condiciones operativas.
