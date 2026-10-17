# Cliente móvil de identidad — T03.e.1

Implementado el 17/10/2026 sobre el [contrato central vigente](contratos/identidad.md). Este corte es la base de transporte y recuperación de sesión; no completa T03.e ni ofrece todavía una pantalla de ingreso.

## Componentes

- `NexoRuta.Mobile.Api`: biblioteca .NET 10, dependiente solo de `Mobile.Core` para `CuentaMovil`. No referencia MAUI, proyectos backend, frontend ni paquetes adicionales.
- `ClienteSesionMovil`: ingreso, consulta de identidad, consulta de permisos y renovación. Usar **una instancia por proceso**, compuesta por DI al integrar la pantalla. Serializa estas operaciones con un único semáforo.
- `IAlmacenSesionMovil` y `AlmacenSesionProtegida`: estado completo en un único valor de `SecureStorage`, con JSON generado sin reflexión. El adaptador está registrado en DI; todavía no se crea el cliente al arrancar la plantilla.

La UI nunca recibe la contraseña desde el almacén: no se persiste. Las clases con tokens no generan un `ToString` que incluya sus propiedades. No se registran cuerpos ni respuestas de autenticación.

El cliente exige el origen HTTPS de la API, sin usuario/contraseña, ruta, query ni fragmento. El transporte predeterminado mantiene la validación normal de certificados, desactiva cookies y redirecciones automáticas, limita respuestas a 64 KiB y tiene timeout de 30 segundos. No se implementaron certificados permisivos, HTTP de desarrollo ni compatibilidades con contratos antiguos. El `HttpMessageHandler` inyectable permite las pruebas sin red.

La API local actual usa HTTP en el puerto 5041; **no se la convierte en un destino válido para enviar credenciales desde mobile**. Antes del corte de UI/integración real hay que configurar un origen HTTPS accesible y confiable para el S21.

## Identidad y aislamiento

1. Enviar credenciales a `POST /api/auth/mobile/login`.
2. Consultar `GET /api/usuarios/actual` con el JWT recibido. Obtener usuario, acceso y operador desde esa respuesta; no inferirlos del correo, nombre de rol o UUID proporcionado por la UI.
3. Exigir contexto de operador con rol. Esto **no acredita perfil repartidor ni autorización sobre rutas**, todavía pendientes en servidor.
4. Persistir juntos origen, cuenta y par de tokens. Si ya hay una identidad guardada, permitir únicamente reingreso de **esa misma cuenta**. Ante otra cuenta o contexto inválido, intentar revocar solo la sesión recién creada, conservar el estado anterior y rechazar el ingreso.

El almacén queda asociado al origen HTTPS. Cambiar la dirección de la API no puede enviar sus credenciales a otro servidor: el cliente rechaza el estado y no lo borra. Versión desconocida, campos de estado desconocidos, JSON corrupto o cuenta inválida también fallan conservando los datos.

La lectura de cuenta guardada es solo metadato. No desbloquea la app, no concede operación offline y no sustituye la biometría.

## Rotación y recuperación ante fallos

- Renovar cuando al JWT le queden como máximo 30 segundos o una consulta de permisos responda 401. La anticipación es técnica; no extiende la autorización ni el plazo absoluto de 30 días del backend.
- **Antes de enviar refresh**, guardar el mismo origen y cuenta con tokens nulos. Ese estado exige reingreso. Si falla esta escritura, no enviar la renovación.
- Tras respuesta válida, guardar el par nuevo en una sola escritura. Exigir mismo `sesionId`, mismo vencimiento absoluto y refresh distinto. No exponer el JWT nuevo antes de completar esa escritura.
- Si se pierde la respuesta, se cancela la petición, cae el proceso o falla el guardado final, el refresh anterior no queda disponible para reenvío. Se conserva la cuenta y se exige reingreso. La implementación es conservadora: incluso 429/5xx de refresh dejan ese estado; no hay reintentos automáticos del secreto.
- Si vence el plazo absoluto, conservar la cuenta y exigir ingreso con contraseña. Un reloj manipulado no amplía los permisos del servidor.
- Ante 401, renovar y repetir **solo una vez la consulta GET**; un segundo 401 invalida los tokens. Un 403 no dispara renovación. El cliente distingue rechazo de credenciales, reingreso, prohibición, límite de peticiones, servicio no disponible y respuesta inválida mediante errores locales sin copiar texto del servidor.
- Un error de red en la consulta GET conserva la sesión. No se reintentan automáticamente escrituras de negocio; todavía no se implementan.

Este corte no toca las jornadas ni archivos de evidencia. No ofrece cierre de sesión ni cambio de cuenta: esas acciones requieren primero el almacenamiento operativo y la comprobación real de pendientes de D06. El endpoint de logout solo se utiliza internamente para limpiar una sesión nueva cuyo ingreso no pudo completarse. Si esa limpieza falla, no se reemplaza el error original ni se altera la cuenta; esa sesión remota queda sujeta a su vencimiento/revocación del servidor.

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

**42 pruebas correctas** con transporte controlado y almacén simulado. Cubren identidad obtenida del servidor, misma/otra cuenta, aislamiento por origen, errores HTTP, 12 consultas concurrentes con una sola renovación, respuesta perdida y reapertura, cancelación, fallos antes/después de consumir refresh, plazo absoluto, rotación inválida, serialización y estado corrupto. El APK Android ARM64 Release compila sin advertencias ni errores.

No se probó este cliente contra una API HTTPS real, ni su almacenamiento de sesión en el S21. La prueba nativa anterior de archivos/claves sigue siendo evidencia de T02.c, no de este flujo. iOS tiene el adaptador compartido preparado, sin compilación ni pruebas Apple.

## Siguiente corte

Completar perfil/autorización del repartidor, SQLite cifrado y la prueba nativa de biometría; luego conectar el ingreso MVVM y recuperación de sesión con esos contratos. Preparar un origen HTTPS confiable antes de probar credenciales en el S21. La UI toma identidad de Backoffice y la adapta a uso del repartidor: acciones breves, controles táctiles, lectura rápida y estados inequívocos de guardado/sincronización; no copiar su distribución de escritorio.
