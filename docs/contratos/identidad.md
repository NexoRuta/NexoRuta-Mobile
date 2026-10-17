# Identidad central: contrato existente y pendientes móviles

Revisión del 17/10/2026: base backend `9fea476`, frontend `e93039c`, con el corte local de sesiones renovables aplicado. El [cliente de identidad T03.e.1](../sesion-http.md) ya consume sus contratos con pruebas de transporte controlado; todavía no hay ingreso conectado a la pantalla MAUI ni prueba HTTPS real de ese cliente.

## Cambios incorporados por el equipo

La API ahora autentica con JWT, verifica el acceso vigente contra PostgreSQL y resuelve permisos por organización. Las webs usan el login central y Bearer en su cliente HTTP. Mobile debe consumir ese mismo contrato, sin copiar proyectos del frontend ni crear otro registro de usuarios.

`Microsoft.Extensions.Identity.Core` se usa para `PasswordHasher<string>`; no están configurados `UserManager`, `SignInManager` ni un almacén Identity completo. Se agregaron endpoints de sesión renovable sobre el sistema JWT existente.

El usuario confirmó conservar y ampliar el mecanismo actual, sin migración a Identity completo. La renovación dura **30 días desde el ingreso**, configurables y sin extensión por rotación. El proyecto aún no está desplegado; se implementa el contrato vigente sin compatibilidades para versiones anteriores.

## HTTP actual

| Operación | Entrada y salida relevantes | Seguridad / errores |
| --- | --- | --- |
| `POST /api/auth/login` | `{ "email": "…", "password": "…" }` → `accessToken`, `tokenType`, `expiresAtUtc`, `contexto`, `tipoOrganizacion` | Anónimo; validación de DTO; 401 para credenciales inválidas; límite de peticiones con 429. |
| `GET /api/usuarios/actual` | `accesoId`, `usuarioId`, `operadorId`, `comercioId`, `usuarioEmail`, nombres, `rolId`, `rolCodigo`, `tipo`, `tipoAcceso` | Bearer; contexto resuelto por servidor. Identidades de plataforma o sin organización también tienen representaciones válidas para otros clientes. |
| `GET /api/usuarios/actual/permisos` | Lista de códigos de permisos efectivos | Bearer; permisos consultados en servidor. No se infieren de un rol global ni de datos locales. |
| `POST /api/auth/mobile/login` | `email`, `password` → `accessToken`, `tokenType`, `expiresAtUtc`, `refreshToken`, `refreshExpiresAtUtc`, `sesionId` | Cuenta activa de operador con rol; credenciales/contexto inválidos → 401. No acredita perfil repartidor. |
| `POST /api/auth/mobile/refresh` | `refreshToken` → nuevo par de tokens, mismo ID y vencimiento absoluto | Token opaco de un uso; rotación transaccional, detección de reutilización y revocación. No necesita JWT vigente. |
| `POST /api/auth/mobile/logout` | `refreshToken` → 204 | Revoca únicamente esa sesión; idempotente. El cliente deberá impedir su uso con pendientes sin sincronizar. |

El login emite JWT HS256 con vigencia de hasta 15 minutos. La API valida firma, emisor, audiencia, expiración, identidad/acceso y versión de sesión. Los JWT móviles incluyen `nexoruta:sesion_movil` y comprueban además revocación y expiración de la sesión en BD. Bajas o cambios de versión invalidan la sesión; los permisos efectivos se resuelven desde la base. Esas validaciones pertenecen al servidor. [Contrato backend y pruebas](../../../backend/docs/workflow/sesiones-moviles.md).

El catálogo actual contiene `usuarios.gestionar`, `configuracion.gestionar`, `envios.crear`, `envios.consultar` y `envios.gestionar`, con ámbitos definidos. **No existen todavía perfil de repartidor ni permisos/endpoints de ejecución móvil.** Una cuenta de operador o un rol cuyo nombre sea “repartidor” no prueba por sí solo autorización para ejecutar una ruta.

`POST /api/auth/register` existe para otros clientes; la app de repartidores no ofrecerá autorregistro, de acuerdo con la decisión del usuario.

## Siguientes cortes de T01 / T03

1. Corte backend de renovación implementado: hashes de refresh, 30 días absolutos configurables, rotación/reutilización y revocación por sesión, migración aplicada y pruebas registradas. La decisión de arquitectura está resuelta.
2. Definir el vínculo/perfil de repartidor y el permiso de ejecución dentro del operador, con gestión desde backoffice y aislamiento de servidor. No abrir una ruta solo porque mobile posea su UUID.
3. Cliente HTTP y adaptador SecureStorage implementados en T03.e.1, con 42 pruebas controladas. Exigen HTTPS, serializan renovación y conservan la cuenta ante respuesta perdida o error de persistencia. Pendientes integración MVVM, origen HTTPS confiable y verificación real del flujo/almacén en S21; no persistir contraseña ni habilitar cambio de cuenta/cierre antes de comprobar pendientes.
4. Completar almacenamiento operativo cifrado, sesión offline y bloqueo de cierre/cambio con pendientes. El repositorio de archivos cifrados no sustituye SQLite ni acredita esas capacidades.

Rutas/asignaciones, transiciones de entrega, sincronización, registro push y cancelación pública siguen sin contrato implementado en este checkout. D15 permanece pendiente; este documento no define su verificación por inferencia.
