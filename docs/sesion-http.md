# Sesión móvil, reingreso y salida protegida

Actualizado el 19/10/2026 sobre el [contrato central](contratos/identidad.md). Ingreso, desbloqueo, recuperación y cierre están conectados a MAUI. Las pruebas de sesión usan HTTP controlado y SQLite real cifrado; falta validar el recorrido contra API HTTPS desde el S21.

## Componentes

- `NexoRuta.Mobile.Api`: biblioteca .NET 10, dependiente solo de `Mobile.Core` para `CuentaMovil`. No referencia MAUI, proyectos backend, frontend ni paquetes adicionales.
- `ClienteSesionMovil`: ingreso, consulta de identidad, consulta de permisos y renovación. Usar **una instancia por proceso**, compuesta por DI al integrar la pantalla. Serializa estas operaciones con un único semáforo.
- `IAlmacenSesionMovil` y `AlmacenSesionProtegida`: estado completo en un único valor de `SecureStorage`, con JSON generado sin reflexión. El cliente se crea por DI cuando existe un origen HTTPS configurado. `EliminarAsync` retira solo la sesión protegida; no borra cuentas, claves ni evidencias.

La UI nunca recibe la contraseña desde el almacén: no se persiste. Las clases con tokens no generan un `ToString` que incluya sus propiedades. No se registran cuerpos ni respuestas de autenticación.

El cliente exige el origen HTTPS de la API, sin usuario/contraseña, ruta, query ni fragmento. El transporte predeterminado mantiene la validación normal de certificados, desactiva cookies y redirecciones automáticas, limita respuestas a 4 MiB y tiene timeout de 30 segundos. No se implementaron certificados permisivos, HTTP de desarrollo ni compatibilidades con contratos antiguos. El `HttpMessageHandler` inyectable permite las pruebas sin red.

La API local actual usa HTTP en el puerto 5041; **no se la convierte en un destino válido para enviar credenciales desde mobile**. Antes del corte de UI/integración real hay que configurar un origen HTTPS accesible y confiable para el S21.

## Identidad y aislamiento

1. Enviar credenciales a `POST /api/auth/mobile/login`.
2. Consultar `GET /api/usuarios/actual` con el JWT recibido. Obtener usuario, acceso y operador desde esa respuesta; no inferirlos del correo, nombre de rol o UUID proporcionado por la UI.
3. Exigir contexto de operador con rol. La pantalla exige también el perfil repartidor del servidor; el contexto de operador por sí solo no acredita ese permiso.
4. Persistir juntos origen, cuenta y par de tokens. Si ya hay una identidad guardada, permitir únicamente reingreso de **esa misma cuenta**. Ante otra cuenta o contexto inválido, intentar revocar solo la sesión recién creada, conservar el estado anterior y rechazar el ingreso.

El almacén queda asociado al origen HTTPS. Cambiar la dirección de la API no puede enviar sus credenciales a otro servidor: el cliente rechaza el estado y no lo borra. Versión desconocida, campos de estado desconocidos, JSON corrupto o cuenta inválida también fallan conservando los datos.

La lectura de cuenta guardada es solo metadato. No desbloquea la app, no concede operación offline y no sustituye la biometría.

## Rotación y recuperación ante fallos

- Renovar cuando al JWT le queden como máximo 30 segundos o una consulta de permisos responda 401. La anticipación es técnica; no extiende la autorización ni el plazo absoluto de 30 días del backend.
- **Antes de enviar refresh**, guardar el mismo origen y cuenta con tokens nulos. Ese estado exige reingreso. Si falla esta escritura, no enviar la renovación.
- Tras respuesta válida, guardar el par nuevo en una sola escritura. Exigir mismo `sesionId`, mismo vencimiento absoluto y refresh distinto. No exponer el JWT nuevo antes de completar esa escritura.
- Si se pierde la respuesta, se cancela la petición, cae el proceso o falla el guardado final, el refresh anterior no queda disponible para reenvío. Se conserva la cuenta y se exige reingreso. La implementación es conservadora: incluso 429/5xx de refresh dejan ese estado; no hay reintentos automáticos del secreto.
- Si vence el plazo absoluto, conservar la cuenta y exigir ingreso con contraseña. Un reloj manipulado no amplía los permisos del servidor.
- Ante 401, renovar y repetir una vez la solicitud autorizada; las escrituras operativas usan su sobre inmutable e idempotencia del servidor. Un segundo 401 invalida los tokens. Un 403 no dispara renovación. El cliente distingue rechazo de credenciales, reingreso, prohibición, límite de peticiones, servicio no disponible y respuesta inválida mediante errores locales sin copiar texto del servidor.
- Un error de red en la consulta GET conserva la sesión. El sincronizador reintenta las escrituras operativas conservando sus identificadores y bytes originales.

## Salida y cambio de cuenta

La acción «Cerrar sesión / cambiar de cuenta» requiere que no existan operaciones sin ACK ni borradores de evidencia. Una captura incompleta también bloquea la salida hasta que se complete y asocie, o se descarte expresamente. Las operaciones en conflicto ya confirmadas por el servidor conservan su diario y evidencia; el logout no resuelve ni elimina ese conflicto.

`ServicioSesionTrabajo` obtiene la identidad de la instalación y llama a `SuspenderSiSinPendientesAsync` del repositorio. La comprobación y la suspensión comparten el semáforo de todos los accesos SQLite. Desde ese momento, un callback de GPS, una captura o un sincronizador tardío no puede escribir en la cuenta mientras se revoca la sesión. El bloqueo es por cuenta y dentro de la instancia única del repositorio del proceso; no elimina datos ni claves.

`ClienteSesionMovil.CerrarSesionAsync` mantiene el semáforo de autenticación durante toda la secuencia:

1. Verificar la cuenta esperada y, si hace falta, renovar la sesión para desactivar la instalación.
2. `DELETE /api/mobile/instalaciones/{id}` con JWT móvil: dar de baja solo esa instalación, aunque siga vinculada a una sesión anterior de la misma cuenta. No se exige conservar el permiso de repartir para darla de baja. Otra cuenta no puede desactivarla; un JWT web tampoco.
3. `POST /api/auth/mobile/logout` con el refresh actual, sin Bearer. El servidor revoca esa familia; repetirlo después de perder una respuesta es seguro. No se revocan sesiones de otros dispositivos.
4. Solo tras el 204 esperado, eliminar la sesión de SecureStorage. Parar el seguimiento, retirar los avisos visibles y vaciar la pantalla de la cuenta anterior. Las claves, hojas, diarios y archivos cifrados se conservan.

El cierre requiere respuesta del servidor. Si falta la renovación, se exige reingreso con la misma cuenta; no se olvida una sesión remota por no poder contactarla. Ante timeout, cancelación o fallo de SecureStorage, se conserva la identidad local y se reanuda el repositorio incluso si el token de cancelación ya venció. Si hubo baja de push pero no terminó la salida, la actualización de registro se reintenta al recuperar conexión o reingresar.

El semáforo del cliente impide que un registro push ya encolado reactive la instalación entre su baja y la revocación. Tras éxito, una petición antigua sigue exigiendo su cuenta original y no puede usar la sesión de quien ingrese después. Para volver a la cuenta anterior, el ingreso válido reanuda su repositorio y recupera los mismos datos cifrados. La salida no finaliza la jornada ni vence pruebas por su cuenta.

## Verificación

En Ubuntu, SDK 10.0.401:

```bash
dotnet test tests/NexoRuta.Mobile.Api.Tests/NexoRuta.Mobile.Api.Tests.csproj \
  --configuration Release --nologo --verbosity minimal

source ~/.config/nexoruta/android-env.sh
dotnet build src/NexoRuta.Mobile/NexoRuta.Mobile.csproj \
  --configuration Release --framework net10.0-android \
  -p:RuntimeIdentifier=android-arm64 -p:AndroidPackageFormats=apk --nologo
```

**52 pruebas correctas** con transporte controlado y almacén simulado. Cubren identidad obtenida del servidor, misma/otra cuenta, aislamiento por origen, errores HTTP, 12 consultas concurrentes con una sola renovación, respuesta perdida y reapertura, cancelación, fallos antes/después de consumir refresh, plazo absoluto, rotación inválida, serialización y estado corrupto. La capa Application pasó 26 pruebas con SQLite cifrado y archivos reales, incluidas siete de salida: operaciones y borradores pendientes, suspensión concurrente, pérdida de respuesta, cancelación y clave perdida. Storage conserva sus 35 pruebas correctas. Android ARM64 Release compila sin errores y con las tres advertencias Mapbox conocidas sobre páginas de 16 KB.

No se probó este cliente contra una API HTTPS real, ni su almacenamiento de sesión en el S21. La prueba nativa anterior de archivos/claves sigue siendo evidencia de T02.c, no de este flujo. iOS tiene el adaptador compartido preparado, sin compilación ni pruebas Apple.

## Validación pendiente

Preparar un origen HTTPS confiable y API/Worker conectados a su base antes de probar credenciales, salida y baja de notificaciones en el S21. La compilación del adaptador y las pruebas HTTP controladas no acreditan ese recorrido nativo. iOS sigue sin compilación ni pruebas Apple; su adaptador push necesita un binding compatible con FID.
