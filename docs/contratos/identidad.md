# Identidad central: contrato consumido por Mobile

El [cliente de sesión móvil](../sesion-http.md) usa la identidad central desde MAUI. El [registro del S21](../validacion-local-s21.md) acredita ingreso y descarga de una hoja por HTTPS, y registra por separado la evidencia de push.

## Cambios incorporados por el equipo

La API autentica con JWT, verifica el acceso vigente contra PostgreSQL y resuelve permisos por organización. Mobile usa los endpoints de sesión renovable y consulta identidad y permisos en la misma API central; no tiene otro registro de usuarios.

El Backend usa ASP.NET Core Identity para administrar credenciales de `Usuario`, con almacén EF Core en PostgreSQL. La sesión HTTP se emite como JWT Bearer y la renovación móvil se administra mediante endpoints propios.

La sesión renovable dura **30 días desde el ingreso por defecto**, configurables y sin extensión por rotación.

## HTTP actual

| Operación | Entrada y salida relevantes | Seguridad / errores |
| --- | --- | --- |
| `POST /api/auth/login` | `{ "email": "…", "password": "…" }` → `accessToken`, `tokenType`, `expiresAtUtc`, `contexto`, `tipoOrganizacion` | Anónimo; validación de DTO; 401 para credenciales inválidas; límite de peticiones con 429. |
| `GET /api/usuarios/actual` | `accesoId`, `usuarioId`, `operadorId`, `comercioId`, `usuarioEmail`, nombres, `rolId`, `rolCodigo`, `tipo`, `tipoAcceso` | Bearer; contexto resuelto por servidor. Identidades de plataforma o sin organización también tienen representaciones válidas para otros clientes. |
| `GET /api/usuarios/actual/permisos` | Lista de códigos de permisos efectivos | Bearer; permisos consultados en servidor. No se infieren de un rol global ni de datos locales. |
| `POST /api/auth/mobile/login` | `email`, `password` → `accessToken`, `tokenType`, `expiresAtUtc`, `refreshToken`, `refreshExpiresAtUtc`, `sesionId` | Cuenta activa de operador con rol; credenciales/contexto inválidos → 401. No acredita perfil repartidor. |
| `POST /api/auth/mobile/refresh` | `refreshToken` → nuevo par de tokens, mismo ID y vencimiento absoluto | Token opaco de un uso; rotación transaccional, detección de reutilización y revocación. No necesita JWT vigente. |
| `POST /api/auth/mobile/logout` | `refreshToken` → 204 | Revoca únicamente esa sesión; idempotente. El cliente bloquea la salida con trabajo pendiente. |
| `GET /api/mobile/perfil` | Perfil de repartidor del acceso autenticado | Bearer y permiso `reparto.ejecutar`; el servidor resuelve la pertenencia al operador. |

El login emite JWT HS256 con vigencia de hasta 15 minutos. La API valida firma, emisor, audiencia, expiración, identidad/acceso y versión de sesión. Los JWT móviles incluyen `nexoruta:sesion_movil` y el servidor comprueba además la revocación y expiración de esa sesión en base. Bajas o cambios de versión invalidan la sesión; los permisos efectivos se resuelven desde la base. [Contrato Backend y pruebas](https://github.com/NexoRuta/NexoRuta-Backend/blob/31756d6bcfa1280fb04849553f1fd48b0d7ca3e6/docs/workflow/sesiones-moviles.md).

Iniciar sesión no basta para ejecutar una ruta. El cliente valida el perfil de repartidor, y el servidor comprueba los permisos y la pertenencia de las hojas y operaciones. Una cuenta de operador o un rol llamado «repartidor» no concede por sí solo acceso a una ruta.

`POST /api/auth/register` existe para otros clientes; la app de repartidores no ofrece autorregistro.

## Capacidades y evidencia

El cliente exige HTTPS, protege los tokens con SecureStorage y serializa la renovación. Si se pierde la respuesta de refresh, conserva los datos de trabajo y exige reingreso con la misma cuenta. El almacenamiento cifrado permite retomar una jornada sin conexión; el bloqueo de salida protege las operaciones y capturas pendientes.

La [sincronización de jornada](../adr/conflictos-offline.md) y el [registro push](../push-reasignacion.md) tienen sus propios contratos y evidencia. La prueba histórica de ingreso, descarga y push en Android no acredita todos los escenarios de salida protegida ni la [aceptación de los recorridos de preentrega](../aceptacion-android/README.md).
