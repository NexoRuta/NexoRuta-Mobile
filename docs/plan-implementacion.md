# Plan de implementación de NexoRuta Mobile

Fecha de revisión: 21/10/2026. **Estado: núcleo, almacenamiento cifrado, sincronización, UI y adaptadores Android implementados; backend/Worker y pantallas web operativas. Entorno local HTTPS con API/Worker, roles restringidos, RabbitMQ y Mailpit verificado. Ingreso, descarga y push FCM real con proceso cerrado comprobados en el S21, incluidos desbloqueo local y confirmación. El plan completo sigue pendiente: mapa offline real, aceptación operativa integral y distribución/iOS.**

El alcance y las decisiones siguen vigentes. Solo se acredita como implementado lo registrado en el avance y su evidencia. Las decisiones pendientes bloquean las tareas que dependan de ellas. La IA debe consultar al usuario para resolverlas, sin convertir propuestas en requisitos.

## Avance de implementación

El usuario autorizó implementar el plan por etapas y pidió esperar cuando sea necesario preparar Visual Studio. Se revisaron las capas y el contexto de acceso del backend, los clientes web y la plantilla MAUI antes del primer corte. Detalle en [arquitectura móvil](arquitectura-mobile.md).

| Corte | Estado real | Evidencia / siguiente dependencia |
| --- | --- | --- |
| Núcleo y causalidad | Implementados | 34 pruebas del núcleo; operaciones inmutables, aislamiento de cuenta y barrera de cierre. |
| SQLite y archivos cifrados | Implementados | 35 pruebas de almacenamiento; reapertura y pérdida de clave también comprobadas previamente en el S21. |
| Identidad central | Implementada; integración actualizada | El backend incorporó Identity completo durante el merge. Se conserva ese sistema central; refresh absoluto de 30 días, rotación y vínculo con la versión de credencial. Sin aceptación de JWT antiguos. |
| Cliente HTTP y sincronización durable | Implementados con pruebas controladas | 52 pruebas HTTP y 52 de Application; sobre persistido antes del envío, reintentos con los mismos bytes, evidencia previa al hecho, conflictos conservados y reconexión de jornadas cerradas con más de 100 operaciones. Ingreso/descarga HTTPS verificados en S21; sincronización operativa completa aún pendiente. |
| Cierre y cambio de cuenta | Implementados con pruebas controladas | Bloqueo por pendientes/borradores, suspensión atómica del almacén, baja de instalación propia, revocación antes de borrar credenciales y datos cifrados conservados. Cierre sin pendientes comprobado contra HTTPS en S21; protección con pendientes aún requiere recorrido nativo. [Detalle](sesion-http.md). |
| Perfiles, políticas, vehículos y rutas | Backend implementado | Configuración explícita antes del despacho, coordenadas confirmadas, capacidad y control de versiones. Gestión web de perfiles/políticas, planificación, despacho y recepción. Borrador vacío para preparar la ruta de una reasignación; no admite despacho ni descarga. |
| Carga, entrega, prueba, rendición y cierre | Backend y pantallas implementados | Pruebas HTTP incluyen rol PostgreSQL restringido. La UI Android compila; queda validación operativa de punta a punta en el teléfono. |
| Seguimiento de jornada | Implementado; aceptación nativa parcial | Inicio explícito, servicio Android en segundo plano, diez puntos aplicados (30,00–30,33 s), recuperación tras interrumpir HTTPS y cierre/detención comprobados en S21; diez casos nuevos prueban continuidad sin tokens, intervalo durable, cierre y aislamiento. Medición prolongada de pantalla apagada/batería pendiente. [Detalle](ubicacion-jornada.md). |
| Mapa de la jornada | Preparación durable Android implementada; aceptación real pendiente | Manifiesto cifrado por cuenta/hoja, comprobación de estilo y teselas, avance/bytes y cancelación sin perder el mapa válido. La salida exige preparación. Puente JNI probado en S21 sin token; falta descarga/renderizado reales y puente nativo iOS. [Detalle](mapa-offline.md). |
| Integraciones nativas | Android compilado; validación parcial | Firma sintética capturada, exportada y recuperada cifrada en S21; puente JNI de mapas comprobado en UI/fondo. Cámara a memoria, lector, biometría y servicio de ubicación implementados, con aceptación pendiente según evidencia. APK Release ARM64 con tres advertencias Mapbox de páginas de 16 KB en Android 16. Firma actual de desarrollo; iOS preparado sin compilar ni probar. |
| Reasignación y avisos | Implementados; transporte FCM real verificado en S21 | Outbox/RLS con reserva vigente, reintento parcial y FID rotado; aviso solo al custodio actual. Android conserva referencias cifradas, consulta vigencia tras desbloquear y distingue recibido/abierto/confirmado. Proceso ausente antes de enviar, publicación Android, desbloqueo y confirmación acreditados. La vista remota no altera comandos pendientes. [Detalle y dependencias](push-reasignacion.md). |
| Conciliación de entrega física | Implementada con pruebas HTTP | Una entrega offline concurrente con cancelación/reprogramación prevalece si conserva la custodia de la ruta original; suspende la parada futura, conserva el diario y valida la prueba automáticamente. Sin decisión de despacho. Batería enfocada de 39 pruebas correcta, incluyendo API con PostgreSQL restringido. APK Android compilado; sin recorrido operativo contra HTTPS en el S21. |
| Cancelación autenticada | API, Comercio y Backoffice implementados | Motivo, versión, confirmación, auditoría inmutable, suspensión de paradas y outbox para el custodio actual. Despachador denegado; SQL restringido y carreras con entrega probados. [Contrato y límites](../../backend/docs/cu/cancelacion-envios.md). Sin aceptación remota en S21. |
| Cancelación pública y geocodificación | Implementadas; proveedores externos parcialmente pendientes | Correo obligatorio en el alta, código y controles para cancelar desde Tracking implementados. Geocodificación Geoapify y confirmación del comercio implementadas con respuestas controladas; falta validar proveedor y mapa reales con sus claves. No hay SMTP real configurado ni token Mapbox. API/Worker/HTTPS y correo en Mailpit verificados localmente; iOS push requiere binding FID compatible y recursos Apple. |
| Cierre con faltantes | Implementado y probado | Incidencias en diario inmutable, custodia conservada, recepción posterior verificada con SQL restringido; aviso previo móvil e historial en Backoffice. [Contrato](../../backend/docs/cu/cierre-jornada.md). |
| CI y entorno local | Implementados; ejecución remota/firma pendientes | Workflow Android y pruebas portables, script de firma privada y entorno local reproducible. [Evidencia S21](validacion-local-s21.md). |
| Integración CU-04 | Corte parcial móvil implementado y verificado | Publicación fijada al crear el envío; pruebas, motivos, máximo e intervalo propios por parada. Duplicación retirada. 22 pruebas dirigidas y 20 de cierre de seguridad correctas; ingreso/descarga/detalle verificados en variante separada del S21. No acredita el caso de uso completo. [Contrato y límites](reglas-envio.md). |
| Cierre del plan | Pendiente | Revisar tareas restantes y evidencias en [ejecución completa](ejecucion-completa.md); ninguna fila de esta tabla acredita aceptación integral. |

## 1. Objetivo y decisiones confirmadas

Desarrollar la aplicación del repartidor con .NET MAUI 10, cubriendo CU-18 a CU-24, navegación entre paradas, requisitos móviles de la letra y OPT-09: notificaciones remotas en producción ante una reasignación urgente, recibidas con la app cerrada y con gestión del ciclo de vida de los identificadores de dispositivo.

Decisiones expresadas por el usuario:

- Plataformas de producto: **Android e iOS**.
- **Coherencia visual con la aplicación web:** referencia principal **Backoffice**, confirmada por el usuario. Conservar una identidad reconocible, terminología y semántica de formularios/estados; adaptar paleta, jerarquía, iconos y distribución cuando mejore la UX del repartidor. Backoffice está incompleto y no debe copiarse literalmente: priorizar sencillez, pocas acciones, lectura rápida y controles táctiles en Android/iOS.
- **Sin compatibilidades para versiones anteriores:** el proyecto todavía no está desplegado. Implementar el contrato vigente directamente, sin alias, adaptadores, audiencias alternativas ni ramas para APIs o clientes antiguos.
- El plan incluye los cambios necesarios de **backend y mobile**, además de las adaptaciones web necesarias para identidad, planificación, cancelación, resolución de conflictos y recepción en depósito descritas en estas tareas.
- Ingreso inicial con **usuario y contraseña**, centralizado con el backend. No se pide incorporar ahora un proveedor externo de identidad.
- **Decisión de identidad actualizada:** reutilizar la identidad central del backend vigente y ampliarla con tokens de renovación para la app. El merge del 19/10 incorporó Identity completo al sistema central; se conserva esa implementación, sin crear otro sistema para mobile. **30 días desde el ingreso**, configurables; renovar no extiende ese plazo.
- **Las cuentas de repartidores las crea y gestiona el administrador del operador desde el backoffice**. No hay registro público en la app.
- **Firebase Cloud Messaging confirmado** como proveedor push, enviado desde el Worker y con APNs para iOS.
- **El despachador solo despacha; no aprueba ni rechaza entregas ni decide sobre devoluciones o conflictos del paquete.** Esta aclaración sustituye la propuesta anterior de resolución por despacho.
- **Conciliación automática de entrega física:** si el destinatario recibió el paquete, conservar la entrega aunque antes hubiera solicitado cancelar o se hubiera reasignado a una ruta posterior. Después corresponde devolución; no aprobación/rechazo por despacho. Verificar en el servidor la pertenencia a la jornada y la custodia original antes de conciliar; una incoherencia de custodia se conserva como conflicto, sin inventar una entrega ni borrar su evidencia. La conciliación suspende la entrega futura de ese paquete y conserva las reglas de validación de prueba de D08.
- **Permitir declarar la entrega física con prueba incompleta**, en estado `Entrega pendiente de validación`. Al completar a tiempo la evidencia exigida, el servidor valida automáticamente las reglas y pasa a `Entregado`, sin aprobación manual del despachador. **Plazo inicial: hasta el cierre de la jornada**, configurable y versionado por operador. Si cierra sin completar la prueba, pasa a `Entrega sin validación`, para decisión del operador según el diagrama. El bulto ya entregado no forma parte de los bultos que vuelven al depósito.
- **Biometría confirmada para desbloquear la sesión local y confirmar rendición/devolución**, con PIN o contraseña de bloqueo del teléfono como alternativa. La app usa la verificación del sistema; no solicita ni guarda ese PIN.
- Si vence la sesión online sin conectividad, **continuar y desbloquear la jornada ya descargada hasta su cierre**. Iniciar una nueva jornada requiere conexión. **Bloquear cierre de sesión y cambio de cuenta mientras haya operaciones o evidencias pendientes de sincronizar**.
- **Circuito diario: descargar la hoja del día, cargar todos los bultos en depósito al comienzo, realizar el reparto y rendir al regresar.** No hay cargas intermedias, retiros durante el trayecto ni alta de envíos desde mobile.
- **Rendición sin dinero:** el repartidor declara los bultos que devuelve y el operario de depósito confirma su recepción física.
- **Reasignar significa postergar el envío y pasarlo a otra ruta.** Si el bulto está en reparto, vuelve al depósito para una entrega posterior; no se transfiere físicamente a otro vehículo en mitad del trayecto.
- **Cancelación antes de la entrega física:** pueden solicitarla el destinatario desde su enlace público, el comercio sobre sus propios envíos y el administrador de su operador. El destinatario se verifica con código de un uso enviado al correo registrado por el comercio. **Sin vencimiento por tiempo**, según la aclaración del usuario; se invalida al utilizarlo, sustituirlo o registrarse la entrega física. Límite de cinco intentos por código y limitación de solicitudes; poseer el enlace de lectura no autoriza cancelar.
- **Después de la entrega física el trámite es una devolución**, también si falta validar la prueba. No cancelar retroactivamente esa entrega ni suponer que el paquete sigue con el repartidor.
- **Incluir el aviso push de cancelación durante el reparto.** La cancelación se suma al escenario de reasignación exigido para OPT-09.
- **Push urgente solo al repartidor de la ruta actual afectada**, tanto en reasignación como en cancelación. El repartidor de la ruta futura recibe el envío al descargar su hoja del día; no se le envía un aviso urgente.
- **Mapa integrado con un punto que marca el destino y mapa base de la zona descargado previamente**, utilizable sin conexión y sin indicaciones de giro. **Mapbox confirmado como proveedor inicial, restringido a opciones gratuitas y detrás de una interfaz reemplazable**, según la aclaración del usuario. Geocodificación con **Geoapify Free**, confirmado el 20/10, detrás de una interfaz separada, sin facturación obligatoria.
- **Coordenadas obtenidas automáticamente desde la dirección y confirmadas por el comercio antes de usarlas**. El comercio debe poder corregir el punto si la propuesta es incorrecta; el móvil descarga el destino ya confirmado.
- **El repartidor inicia y termina explícitamente la jornada**, activando/desactivando el seguimiento de ubicación. **Frecuencia inicial de registro: cada 30 segundos**, ajustable según las mediciones de batería y precisión del S21. Despachar una ruta o confirmar una rendición no sustituye esa acción.
- **Distribución de esta entrega: APK Android firmado para instalación directa en el S21 e iOS preparado en código/configuración**, pendiente de compilación y pruebas con recursos Apple. Publicar en las tiendas no forma parte de esta entrega.
- Hardware disponible: **Samsung Galaxy S21 SM-G991U con Android 14/API 34**, comprobado por Wi-Fi; USB averiado. No hay Mac ni iPhone disponibles. Implementar los adaptadores iOS y registrar por separado lo que no pueda compilarse o probarse.
- La implementación fue autorizada por el usuario. Avanzar por etapas verificables y esperar si necesita preparar Visual Studio; no transformar decisiones abiertas en requisitos asumidos.

## 2. Fuentes y precedencia

| Fuente | Uso en este plan |
| --- | --- |
| [Letra del laboratorio](../../docs/alcance/Laboratorio__NET_2026.pdf), §§4, 5.4, 6.3, 6.4, 6.11, 7.3 y 8.3 | Alcance, MVVM, SQLite, cifrado, ubicación con pantalla apagada, biometría, push y monitoreos. |
| [Entrega de análisis y diseño](../../docs/alcance/Entregas/Entrega-10-4-2026-Grupo-4.pdf), p. 6 y anexos C–E | Máquina de estados, entrega física con prueba pendiente, dependencias entre módulos, requisitos no funcionales y opcionales seleccionados. |
| [Planilla de CU y responsables](../../docs/alcance/Calcs/Casos%20de%20Uso%20-%20Responsables.xlsm) | CU-18–24, AP-08, contratos entre módulos y OPT-09. |
| [Alcance funcional](../../backend/docs/workflow/alcance-funcional.md) | Identificadores de requisitos e hitos. |
| [Arquitectura backend](../../backend/docs/workflow/arquitectura-backend.md) y [guía de CU](../../backend/docs/workflow/implementar-cu.md) | Límites de módulos, autoridad del servidor, especificaciones y bitácora. |
| [Máquina de estados propuesta](../../docs/alcance/Diagramas/Maquina-De-Estados-Propuesta.pdf) | Mismo diagrama de la p. 6: `Entrega pendiente de validación` y `Entrega sin validación`. El usuario confirmó admitir entrega física con prueba pendiente; cancelar todavía requiere ampliar el diagrama y acordar sus reglas. |

Precedencia: aclaraciones explícitas del usuario para las decisiones del producto; letra para los requisitos académicos; contratos y código verificados para describir lo existente. Si una aclaración deja un requisito de la letra sin cubrir, registrar esa diferencia y resolverla con el usuario. Los README históricos no prueban el estado actual.

## 3. Punto de partida verificado

Inspección inicial y actualización tras los cambios del equipo: backend `9fea476`, frontend `e93039c`; mobile conserva el scaffold `da537ec` con los cortes locales registrados arriba. Las pruebas actuales están en el avance y en sus documentos de evidencia.

| Área | Situación encontrada | Consecuencia |
| --- | --- | --- |
| Mobile | UI de plantilla; núcleo de jornada con 25 pruebas, archivos cifrados con 23 pruebas y cliente de identidad con 42 pruebas controladas. Sin persistencia operativa SQLite ni ingreso conectado a UI. | Integrar los flujos sobre estas bases; un archivo cifrado no es todavía una entrega ni una jornada durable. |
| Destinos | `net10.0-android`; iOS/MacCatalyst condicionados al host. `ApplicationId` de plantilla. | Elegir identidad de aplicación, versiones soportadas y toolchain antes de configurar push o firma. Windows/MacCatalyst no son objetivos funcionales solicitados. |
| Autenticación API | JWT Bearer, validación de acceso/versión y renovación móvil por 30 días absolutos configurables, con hashes y revocación por sesión. PasswordHasher de Identity, sin Identity completo. | Integrar MAUI con los contratos publicados y completar perfil repartidor. |
| Webs | Login central y transporte Bearer en el cliente HTTP; gestión de roles/usuarios por organización. Contratos comprobados contra la API actualizada. | Utilizar el mismo sistema central al integrar mobile. |
| Dominio operativo | Envíos/bultos y seguimiento; `EstadoEnvio` solo contiene `Admitido`. Sin rutas, paradas ni perfil de repartidor implementados. | M01, M02 y M03 son dependencias reales del desarrollo móvil. |
| Asincronía | Outbox, Worker y consumidores RabbitMQ/Kafka para seguimiento. | Inspeccionar y extender lo existente para eventos móviles y push; no crear otro sistema de colas. |
| Multitenancy | Envíos con `OperadorId` y `ComercioId`, filtrado por cuenta; RLS pendiente. | Identidad, consultas, sincronización y Worker deben conservar el aislamiento. |
| Datos locales | Archivos AES-256-GCM y SecureStorage comprobados en Android; reglas de exclusión de backup/transferencia configuradas e inspeccionadas. Sin SQLite ni captura de cámara. | Completar proveedor SQLite mantenido y asociación transaccional archivo/comando; verificar temporales de cámara, restauración y recursos Apple. |
| Coordenadas de destino | `Direccion` conserva una descripción, sin latitud/longitud. | Agregar geocodificación automática, confirmación/corrección por el comercio y persistencia de la ubicación aprobada antes de descargar la ruta. |
| Verificación del destinatario | `Destinatario` y el alta HTTP solo incluyen su nombre; no hay cédula, correo ni teléfono del destinatario en ese contrato. | El mecanismo de cancelación pública y el origen confiable de los datos/contactos deben definirse juntos; no comparar datos que el sistema no posee ni aceptar una autoafirmación del visitante. |

Referencias principales: `src/NexoRuta.Mobile/`, `../../backend/src/NexoRuta.Api/Core/Security/`, `../../backend/src/NexoRuta.Domain/Envios/`, `../../backend/src/NexoRuta.Infrastructure/Outbox/` y `../../frontend/src/NexoRuta.ApiClient/`.

## 4. Registro de decisiones y puntos pendientes

No elegir silenciosamente reglas del negocio, tiempos ni alternativas de seguridad. Actualizar esta tabla con la respuesta y fecha antes de iniciar la tarea dependiente.

| ID | Decisión | Estado al 17/10/2026 | Tareas relacionadas |
| --- | --- | --- | --- |
| D01 | Autenticación central | **Actualizado y confirmado por el usuario:** reutilizar la identidad central. El merge del 19/10 incorporó Identity completo al backend y se conserva esa implementación, sin otro sistema de identidad móvil. Renovación por **30 días desde el ingreso**, configurable, con rotación que no extiende el vencimiento. Se mantienen alta de repartidores por administrador y ausencia de autorregistro móvil. | T01, T03 |
| D02 | Proveedor push | **Confirmado:** Firebase Cloud Messaging directo desde Worker, con APNs para iOS. FCM figura sin costo y disponible en Spark. | T02, T11 |
| D03 | Plataformas y herramientas | Android+iOS confirmados; S21 SM-G991U con Android 14/API 34 comprobado por ADB, sin Mac/iPhone. Ubuntu/VS Code con MAUI Android instalado; S21 por Wi-Fi porque tiene el USB averiado. Mínimos confirmados: Android 11 e iOS 15. Nombre `NexoRuta Repartidor`, identificador `uy.nexoruta.repartidor`. Faltan cuenta Apple Developer y acceso a compilación/pruebas macOS/iPhone. | T02, T12 |
| D04 | Conflicto de sincronización | **Regla vigente confirmada:** si recibió el paquete, hay entrega física aunque hubiera pedido cancelar; cualquier trámite posterior es devolución. Conciliar automáticamente una cancelación/reprogramación concurrente cuando se verifica jornada/custodia original, suspendiendo la parada futura y conservando el hecho y su evidencia. Si el paquete no se entregó y vuelve al depósito, no está entregado. Mantener separada la validación automática de prueba de D08. **El despachador solo despacha**, sin aprobar/rechazar entregas. Las incoherencias de custodia y otros conflictos se conservan sin una intervención manual inventada. | T01, T06, T11 |
| D05 | Biometría | **Confirmado:** desbloqueo de sesión local y confirmación de rendición/devolución, con credencial de bloqueo del teléfono como alternativa si no hay biometría o el sensor está bloqueado. Cancelar la verificación no autoriza la acción. | T02, T03, T09 |
| D06 | Sesión sin red y cambio de usuario | **Confirmado:** continuar la jornada descargada aunque venza el token; para una nueva jornada, reconectar. Bloquear cierre/cambio de cuenta mientras haya operaciones o evidencias pendientes de sincronizar. Permitir reautenticar la misma cuenta para completar sync, conservando pendientes ante rechazo o revocación del servidor. | T03, T04, T06 |
| D07 | Hoja de ruta y mapa | **Confirmado:** hoja del día, mapa integrado con marcador de destino y zona descargada, sin indicaciones de giro. Coordenadas automáticas desde la dirección, confirmadas/corregidas por el comercio. Mapbox confirmado detrás de interfaz; geocodificador separado Geoapify Free, confirmado el 20/10. Zona horaria y reglas configurables por operador antes del primer despacho. Circuito diario en D13. | T01, T02, T04, T10 |
| D17 | Servicios e infraestructura | **Actualizado 19/10:** el usuario proporcionó el proyecto Firebase `nexoruta-mobile` y un JSON Android para `uy.nexoruta.repartidor`, ignorado por Git. ADC local y permiso de envío FCM confirmados; faltan origen API HTTPS, ejecución del Worker conectado a la base y correo de salida. El 20/10 el usuario confirmó que siguen sin existir Mapbox, API HTTPS ni SMTP; autorizó preparar un entorno local y dejar identificadas las dependencias externas. Entorno local preparado y probado con API HTTPS, Worker/RabbitMQ/Mailpit; FCM real probado en S21. La infraestructura de producción se realizará después mediante IaC. Implementar configuración sin inventar credenciales ni atribuirle pruebas remotas; no crear cuentas ni desplegar por inferencia. | T02, T11–T12 |
| D08 | Pruebas e intentos | **Confirmado:** declaración física con prueba incompleta → `Entrega pendiente de validación`; completitud válida dentro del plazo → validación automática y `Entregado`. **Plazo inicial hasta el cierre manual de la jornada**, configurable/versionado por operador; si cierra sin prueba completa → `Entrega sin validación`, para decisión del operador como en el diagrama. Especificar evidencia admisible por política, intento fallido y revisión posterior al vencimiento en T01, sin inventar datos. | T01, T05, T06, T08, T10 |
| D09 | Devolución y rendición | **Confirmado:** el repartidor declara bultos devueltos y el operario de depósito confirma recepción; sin gestión de dinero o cobros. Después de una entrega física, cualquier reversión se tramita como devolución, no cancelación. Conservar diferencia entre regreso a depósito para otra ruta, devolución definitiva y devolución posterior a entrega. No agregar retiros en calle por inferencia. | T01, T09, T11 |
| D10 | Seguimiento de jornada | **Confirmado:** inicio/fin explícitos por el repartidor y seguimiento solo mientras está iniciada. **Registro inicial cada 30 segundos**, ajustable tras medir batería y precisión en el S21. Calibrar comportamiento real del servicio y documentar precisión, huecos y retención; no fabricar puntos si falta GPS ni prometer una periodicidad observada sin medirla. | T01, T02, T10 |
| D11 | Reasignación urgente | **Confirmado:** cambiar el envío a otra ruta para entregarlo después; si está en reparto, vuelve físicamente al depósito. Aviso urgente **solo al repartidor de la ruta actual afectada**, no al de la ruta futura. Sin recarga/intercambio entre vehículos en la calle. Pendientes permisos/transiciones, recepción y preparación de la ruta posterior, vigencia del aviso y demostración con app cerrada. | T01, T04, T09, T11 |
| D12 | Distribución y evidencia de entrega | **Confirmado:** APK Android firmado para instalación directa en el S21; iOS preparado, pendiente de compilación/pruebas con recursos Apple. Sin publicación en tiendas para esta entrega. Registrar ambiente HTTPS para probar OPT-09 en remoto. Los hitos del curso son referencias de validación; no hay una estimación de duración comprometida. | T00, T12 |
| D13 | Circuito de carga diario | **Confirmado:** cargar todos los bultos en depósito al comienzo del día, ejecutar la hoja descargada y rendir al regresar. Sin cargas intermedias, recolecciones ni alta de envíos desde mobile. La reasignación lógica a una ruta posterior se contempla en D11; no equivale a un traslado físico entre vehículos durante el recorrido. | T01, T04, T07, T09, T11 |
| D14 | Cancelación y corte por entrega física | **Actores confirmados:** destinatario desde su enlace con verificación adicional, comercio sobre sus propios envíos y administrador de su operador. Push solo al repartidor actual. **Corte confirmado:** después de la entrega física corresponde devolución, también en estado pendiente de validación. Enumerar en T01 los estados y transiciones de cancelación previos al hecho físico; una entrega sin red concurrente conserva el conflicto de D04. El mecanismo público está en D15. | T01, T04, T06, T09, T11 |
| D15 | Verificación para cancelación pública | **Confirmado:** código de un uso al correo del destinatario registrado por el comercio; **correo obligatorio en cada envío**, confirmado por el usuario el 19/10. El usuario rechazó vencimiento temporal; autorizar solo antes de la entrega física. Mantener cinco intentos, reemplazo/invalidez al reemitir, limitación de solicitudes y autorización limitada a ese envío/acción. La entrega física offline concurrente se concilia según D04. Correo real pendiente de D17. | T01, T11, Tracking |
| D19 | Versión de reglas por envío | **Confirmado 20/10:** fijar la publicación central de CU-04 al crear el envío; sin reglas publicadas el operador no puede admitirlo. La referencia queda fija para sus intentos y pruebas posteriores, también tras reasignación. Retirar la duplicación de intentos, motivos y prueba en la configuración de reparto, sin compatibilidad con el contrato anterior. | T01, T04–T10 |
| D18 | Bultos pendientes al cierre | **Confirmado 20/10:** permitir finalizar la jornada registrando una incidencia por los bultos sin entrega ni rendición. Conservar su custodia y trazabilidad; no inventar recepción, entrega ni extravío. | T09–T10 |
| D16 | Identidad visual | **Confirmado por el usuario:** Backoffice como referencia de identidad, adaptada a una app de repartidor simple y cómoda. Backoffice está incompleto; no copiarlo literalmente. Ajustar presentación cuando mejore la UX, manteniendo coherencia y recursos MAUI compartidos. | T03–T12 |

### Pendientes y condiciones para comenzar cada tarea

- **D04 aclarada:** implementar y verificar conciliación automática de entrega física frente a cancelación/reprogramación con la custodia original comprobada. Se descartan las consultas anteriores sobre aceptar/rechazar entregas y rendirlas después del cierre. Si hubo entrega, corresponde una devolución posterior; si no se entregó, rendición y recepción en depósito. No inventar un rol de resolución manual ni modificar el plazo de prueba.

- **D15 resuelta:** implementar código de un uso por correo sin expiración temporal, con invalidación y cinco intentos. El servicio de correo real sigue pendiente de infraestructura D17; no presentar una simulación como envío real.
- **Pruebas técnicas:** proveedores/adaptadores elegidos y versiones fijadas en el código y contratos. Quedan la descarga/renderizado Mapbox reales con token propio, Geoapify con clave propia y la aceptación operativa nativa restante. La elección del proveedor no acredita su funcionamiento offline.
- **SQLite cifrado:** implementado con `Microsoft.Data.Sqlite.Core` 10.0.12 y `SQLite3MC.PCLRaw.bundle` 2.6.1; 35 pruebas de almacenamiento y evidencia nativa registrada. Conservar esos controles al cambiar versiones; el cifrado de archivos no sustituye el de SQLite.
- **Identidad:** perfil, permisos, renovación central e integración MAUI implementados. Falta completar los recorridos nativos con pendientes y aceptación operativa. Detalle HTTP y evidencia en [sesión HTTP](sesion-http.md).
- **Especificación antes de código:** T01 debe enumerar transiciones, contratos, políticas versionadas, evidencia admisible, permisos de reasignación y recuperación/activación de cuentas. Si necesita una nueva regla de producto, consultarla; no sustituir ese trabajo con decisiones arbitrarias del agente.
- **Preparación operativa:** registrar Android real del S21, ambiente HTTPS y materiales de firma. iOS conserva expresamente su limitación de validación; no bloquea la entrega del APK acordada.

Estas condiciones están asignadas a tareas concretas. No significan que toda M04 deba esperar a una decisión de cancelación pública o a disponer de un iPhone.

## 5. Cobertura y evidencia exigida

| Requisito | Tareas | Prueba de aceptación |
| --- | --- | --- |
| CU-18: descargar ruta | T04 | Ruta despachada del repartidor correcto, disponible tras reiniciar sin red. |
| CU-19: escaneo de carga | T07 | Carga inicial en depósito: bulto correcto, repetido, faltante y ajeno; cotejo local contra la hoja del día, según D13. |
| CU-20: entrega | T05–T06 | Entrega física sin red, evidencia disponible y hora/posición o carencia explícita; sincronización idempotente a `Entregado` o `Entrega pendiente de validación`, según corresponda. |
| CU-21: intento fallido | T08 | Motivo de catálogo versionado, evidencia y sincronización con transición válida. |
| CU-22: devolución/rendición | T09 | Flujo acordado hasta recepción en depósito, con pendientes y trazabilidad. |
| CU-23: ubicación | T10 | Puntos registrados durante jornada, con pantalla apagada y segundo plano. |
| CU-24: operación sin red | T03–T10 | Operación local durable, recuperación tras cierre, lotes parciales y conflicto explícito. |
| AP-08: navegación | T10 | Seleccionar una parada y ver su destino marcado sobre el mapa de la zona descargado previamente, también sin red; sin indicaciones de giro. |
| §6.11: biometría | T02–T03, T09 | Verificación real de las operaciones acordadas, cancelación y alternativa aprobada. |
| §§6.3–6.4: datos locales | T02–T04 | SQLite y archivos sensibles cifrados; aislamiento y ciclo de vida de las claves. |
| OPT-09: push producción | T02, T11–T12 | Reasignación real persistida, Worker, proveedor y teléfono cerrado, incluyendo rotación/baja de registro. |
| Cancelación en reparto | T01, T06, T09, T11 | Cambio autorizado y push al repartidor; parada actualizada, bulto pendiente de rendición y conflicto con una entrega física concurrente conservado. |

El push de reasignación al repartidor es distinto de CU-27, que notifica cambios de estado al destinatario. No sustituir uno por otro. Geocercas, impresora Bluetooth, optimización de rutas y gRPC no forman parte de este plan. Kafka/NoSQL y caché de otros módulos se coordinan por sus contratos existentes, sin implementarlos completos como requisito de mobile.

## 6. Arquitectura propuesta para implementar con IA

- **Diseño visual obligatorio:** Backoffice es la referencia confirmada; revisar `frontend/src/NexoRuta.Backoffice/Pages/Ingresar.cshtml`, `wwwroot/css/site.css` y su layout antes de construir UI. Adaptar su identidad a tareas de reparto y uso táctil, sin reproducir la distribución de escritorio ni sus partes incompletas. Centralizar colores/estilos MAUI, con legibilidad, contraste, pocas acciones por pantalla y estados claros de guardado/sincronización. El usuario autoriza ajustes visuales que mejoren esa UX.
- Conservar la app MAUI existente, XAML, Shell, inyección de dependencias y MVVM. Organizar por funcionalidades: sesión, rutas, carga, entregas, sincronización y jornada. Las vistas no ejecutan HTTP, SQL ni reglas de transición.
- Extraer únicamente lógica móvil comprobable sin dispositivo a una biblioteca `net10.0` si hace falta para probarla sin workloads nativos. No reproducir las cinco capas del backend ni agregar proyectos por cada pantalla.
- Adaptadores Android/iOS para push, cámara/escáner, ubicación continua, biometría y almacenamiento seguro. Elegir paquetes mantenidos después de demostrar compatibilidad con .NET 10, Release y ambas plataformas; registrar versiones y licencias.
- SQLite como almacenamiento operativo local. Transacción local para guardar resultado, referencias a evidencia y comando pendiente; interfaz lee el estado local durable.
- Cifrar BD y archivos sensibles; `SecureStorage` protege claves y credenciales, no cifra por sí solo SQLite ni las fotos. Evaluar un proveedor SQLite cifrado compatible, incluyendo sus bibliotecas nativas, y probar también temporales/backups. [SecureStorage](https://learn.microsoft.com/en-us/dotnet/maui/platform-integration/storage/secure-storage?view=net-maui-10.0)
- Cliente HTTP propio del submódulo mobile, contratos explícitos derivados de OpenAPI acordado. Reutilizar convenciones y escenarios de `NexoRuta.ApiClient`; evitar referencias de proyecto que exijan clonar otro submódulo para compilar mobile.
- Servidor autoriza por usuario, pertenencia, perfil, operador y asignación vigente. No confiar en IDs del cliente, biometría local, payload push ni catálogos descargados como autorización del servidor.
- M01 conserva identidad/configuración; M03, rutas y asignaciones; M02, transiciones/eventos; M04, ejecución/sync; comunicaciones del Worker se coordinan con M05. La app propone operaciones y el servidor valida los efectos definitivos.

### Contratos mínimos que deben especificarse en T01

No se fijan URLs ni DTOs inexistentes como si ya fueran contratos aprobados. Definir primero proveedor, autorización, campos, versiones, errores y pruebas de cada familia:

| Familia | Contenido mínimo a acordar |
| --- | --- |
| Identidad | Ingreso, renovación, usuario/cuentas autorizadas, perfil repartidor, selección validada y baja de sesión/dispositivo. |
| Gestión de repartidores | Alta/gestión por administrador de su operador desde backoffice, vinculación con identidad y perfil; no autorregistro público en MAUI. |
| Ubicación del destino | Propuesta automática desde dirección, selección/corrección y confirmación por el comercio, coordenadas persistidas con su dirección y versión. Definir resultado sin coincidencias o ambiguo, fallo del proveedor y cambios de dirección posteriores. |
| Hoja de ruta | Identidad/versión, jornada/zona horaria, asignación, orden de paradas, envíos/bultos, dirección y coordenadas validadas del destino, datos necesarios del destinatario. Catálogos y reglas versionados suficientes para operar sin red. |
| Eventos móviles | ID estable de operación, instalación, entidad/versión base, tipo, fecha/hora del dispositivo con desplazamiento, hora de recepción servidor, posición/precisión y referencias de evidencia. |
| Evidencias | Subida repetible, tamaño/tipo/hash, autorización, asociación y estado de completitud. Admitir la declaración física con prueba incompleta en estado pendiente; impedir marcar `Entregado` validado hasta cumplir D08. |
| Sincronización | Resultado por operación: aplicado, duplicado, pendiente de procesamiento, rechazado o en conflicto; versión resultante, errores recuperables y cambios del servidor. |
| Jornada/posición | Vehículo/ruta autorizados, inicio/fin y puntos con identidad y tiempo; entrega diferida y retención. |
| Cierre de jornada y plazo de prueba | Cierre explícito con ID estable, jornada/versión, hora del dispositivo y operaciones previas identificadas. Detener seguimiento local y evaluar vencimiento sin confundir momento de cierre con recepción tardía en el servidor. |
| Registros push | Identidad de instalación y registro del proveedor, plataforma/ambiente, propietario autenticado, renovación, revocación y última actividad. No confundir registro push con identidad o sesión. |
| Reasignación urgente | Comando autorizado y concurrente de M03; suspensión de la entrega actual, ruta posterior, custodia del bulto y recepción en depósito; evento versionado desde transacción/outbox y consulta del estado vigente. |
| Cancelación | Destinatario según D15, comercio propietario o administrador del operador, estados permitidos antes de entrega física, motivo, concurrencia, bulto a rendir y evento/outbox para push al repartidor actual. Poseer un enlace de lectura no autoriza por sí solo la cancelación. No usar la recepción en depósito como borrado de la cancelación. |
| Verificación pública | Autorización limitada a un envío y a la acción de cancelar, con confirmación deliberada y validación del servidor. Definir datos/contactos, mecanismo y respuestas en D15; abrir un enlace de seguimiento no ejecuta cambios de estado. |
| Conciliación/depósito | Recepción física confirmada por depósito y conciliación automática de D04 con validación de jornada/custodia, auditoría inmutable y resultado al móvil. El despachador no decide sobre entregas o devoluciones. |
| Preparación del mapa offline | Zona que cubre las paradas, versión/estilo y niveles de detalle seleccionados, estado y completitud de descarga; obtener las coordenadas antes del recorrido. Una descarga incompleta no se informa como lista para operar sin red. |

Las coordenadas que usa MAUI provienen de la confirmación del comercio, no de una consulta automática realizada durante el reparto. Si cambia la dirección, no reutilizar silenciosamente la confirmación del punto anterior. El servidor valida rangos y acceso; el comercio confirma la ubicación propuesta o corregida. Si el proveedor falla o devuelve varias coincidencias, mantener el dato como pendiente de confirmación sin inventar `(0, 0)` ni elegir un destino silenciosamente.

Elegir proveedor de geocodificación junto con el de visualización, comprobando costo y posibilidad de persistir los resultados que luego se descargarán al móvil. En Mapbox, los resultados temporales no se pueden almacenar en caché y el almacenamiento permanente es una modalidad distinta; no asumir que la configuración temporal sirve para este flujo offline. [Almacenamiento de resultados de Mapbox](https://docs.mapbox.com/api/search/geocoding/#storing-geocoding-results).

### Estado del envío, custodia del bulto y validación de prueba

La página 6 distingue estos recorridos. Implementarlos en M02 y consumirlos desde MAUI; no derivar el estado exclusivamente de una pantalla o del número de archivos subidos.

| Situación | Comportamiento del plan | Regla aún pendiente |
| --- | --- | --- |
| Entrega con prueba exigida válida | Registrar `Entregado`, con evento inmutable, hora y posición del dispositivo. | Contrato y validaciones de la política versionada. |
| Entrega física declarada con prueba incompleta | Registrar `Entrega pendiente de validación`; permitir completar evidencia hasta cerrar la jornada, con la política inicial acordada. Mostrar el resultado con ese nombre, sin presentar entrega validada ni bulto pendiente de devolución física. | Evidencia admisible y reglas versionadas de D08. |
| Se completa y valida la prueba | Validación automática del servidor contra las reglas versionadas y transición de `Entrega pendiente de validación` a `Entregado`, sin aprobación manual del despachador. | Validaciones concretas del contrato; una prueba incompleta o inválida no dispara la transición. |
| Se cierra la jornada sin prueba completa | Aplicar la política inicial de cierre y pasar a `Entrega sin validación`, para decisión del operador; no convertirla en intento fallido ni en paquete físicamente devuelto. | Especificar la revisión posterior del operador; el cierre sin red respeta las operaciones y evidencias generadas antes de cerrar. |
| Reasignación a entrega posterior durante el reparto | Suspender esa entrega y mantener la custodia del bulto hasta rendirlo en depósito; conservar ruta anterior, nueva planificación y trazabilidad. | Estados de tránsito/recepción de D11, sin inventar un intento fallido si no ocurrió. |
| Cancelación durante el reparto | Incorporar la transición autorizada y el aviso push; seguir registrando quién conserva el bulto y cuándo lo recibe depósito. | D14; la cancelación no figura en el diagrama de origen. |
| Solicitud después de la entrega física | Tramitar como devolución; preservar el hecho de entrega y sus pruebas, incluso si siguen pendientes de validación. No presentar esa operación como cancelación de una parada activa. | Detalle del circuito de devolución posterior, sin inferir retiros por el repartidor. |

Planificar una ruta futura no prueba que el bulto esté disponible para cargar. La disponibilidad física debe surgir de la recepción en depósito, sin asignaciones activas incompatibles ni transferencias entre vehículos en la calle. Tampoco debe confundirse el retorno a depósito para otro intento con `Devuelto` al terminar una devolución definitiva.

Completar evidencia o resolver su revisión genera una operación nueva con su propio ID. No modificar el contenido del evento original de entrega física ni reutilizar su ID con otro payload; sus reintentos siguen siendo idempotentes.

Cada prueba conserva su hora y origen reales. Una fotografía o posición capturada después no se fecha como si se hubiera obtenido en el momento de la entrega; la política debe definir qué evidencia posterior admite para validar el hecho físico.

El plazo inicial termina con la acción explícita de **finalizar jornada**, no al cerrar sesión, rendir en depósito, llegar a medianoche ni recibir el servidor un lote tardío. El cierre puede guardarse sin red y detiene la ubicación local inmediatamente. Al sincronizar, enviar/procesar las operaciones y evidencias anteriores al cierre antes de decidir qué entregas quedaron sin validación. Conservar un orden verificable por jornada y un ID de cierre estable; repetir su envío no reabre la jornada ni vuelve a aplicar vencimientos.

Una prueba completada antes del cierre no se rechaza solo porque el archivo llegue después al servidor. Tampoco se permite fechar o insertar después del cierre una prueba nueva como si fuera anterior. Concretar en el contrato el orden y las confirmaciones de procesamiento necesarias para no evaluar un cierre antes de recibir su trabajo previo.

### Cancelación desde el seguimiento público

El destinatario no necesita una cuenta de operador o comercio. Tiene acceso al enlace de seguimiento, pero el mecanismo adicional de D15 debe autorizar la cancelación específica. D15 quedó resuelta: código de un uso por correo, sin expiración temporal, con cinco intentos e invalidación al reemplazarlo. La cancelación está implementada con pruebas controladas; falta el correo real; no usar comparación de cédula ni tratar la lectura del enlace como autorización de cancelación.

Diseño confirmado e implementado: código de un uso enviado al contacto asociado previamente al envío, sin expiración temporal, cinco intentos y validación server-side ligada a envío/acción. El visitante no puede cambiar el destinatario del código en la misma solicitud. Revalidar el estado y consumir la autorización al confirmar la cancelación para impedir repetición o una carrera con la entrega. Esta propuesta adapta las recomendaciones de autorización de operaciones de [OWASP](https://cheatsheetseries.owasp.org/cheatsheets/Transaction_Authorization_Cheat_Sheet.html).

La protección de D15 bloquea la acción de cancelación pública hasta acordar e implementar su autorización; no se reemplaza con un botón sin verificación. El plan conserva por separado las cancelaciones del comercio y administrador autenticados. Probar enlace ajeno, verificación incorrecta/reemplazada/reutilizada, cambio de envío durante la operación y entrega física concurrente, sin revelar datos personales en errores o registros.

### Reglas técnicas de sincronización

1. Guardar operación y evidencia durable antes de mostrar éxito local. Distinguir visualmente guardado local, pendiente, confirmado, rechazo y conflicto.
2. Mantener el mismo ID y contenido al reintentar. El servidor debe devolver el resultado previo para un duplicado y detectar la reutilización del ID con otro contenido o propietario.
3. Procesar orden por envío y gestionar la versión devuelta por la operación anterior. No reenviar una secuencia entera con la misma versión inicial ni asumir orden global entre dispositivos.
4. Tratar aplicación del evento, registro de idempotencia y outbox en una transacción del servidor. Con procesamiento asíncrono, un `202` o ACK de transporte no significa que la entrega ya quedó aplicada: definir consulta/confirmación posterior.
5. Persistir resultados por elemento. Tras respuesta parcial, timeout o cierre, reanudar pendientes sin duplicar los ya confirmados. Aplicar exclusión entre disparadores de sincronización dentro del móvil.
6. Reintentos acotados con espera creciente para errores recuperables; autenticación, validación y conflictos requieren tratamiento distinto. La conectividad reportada por el sistema no prueba que la API sea accesible.
7. Separar hora del hecho y hora de recepción. Registrar precisión/ausencia de GPS y desfase conocido sin inventar coordenadas ni alterar la cronología para forzar una transición.
8. Aplicar D04 en conflictos de estado/asignación; conservar evidencia y auditoría. La reasignación nunca debe borrar operaciones pendientes del repartidor anterior.
9. Aislar datos locales y cola por cuenta/operador. Bloquear cierre de sesión y cambio de cuenta con operaciones o evidencias sin sincronizar; permitir reautenticar la misma cuenta. Revocación, pérdida de claves o migración local no pueden descartar pendientes silenciosamente; aplicar D06.
10. Coordinar el cierre de jornada con las operaciones/evidencias previas, además del orden por envío. El vencimiento por cierre no se procesa antes de esos elementos; probar sincronización tardía, lote parcial, caída y reenvío del cierre sin efectos duplicados.

## 7. Secuencia de trabajo

El avance anterior distingue los cortes implementados de las tareas aún pendientes. Una tarea termina con comportamiento verificable, pruebas ejecutadas y limitaciones registradas. Las tareas bloqueadas por decisiones no se sustituyen con reglas inventadas.

Las etapas agrupan trabajo; no son encargos para resolver enteros en una sola sesión de IA. Usar estos cortes mínimos para las dependencias más grandes:

| Corte | Componente y resultado | Verificación antes de continuar |
| --- | --- | --- |
| T02.a–g | Una integración nativa por corte: push, SQLite cifrado, archivos cifrados, biometría/credencial del teléfono, ubicación, escáner/captura y mapa integrado. | Release instalado y prueba concreta en S21; iOS con estado de evidencia separado. |
| T03.a | Backend: identidad central y migración/vínculo con usuarios existentes. | Contraseña y renovación válidas/inválidas; conservación de IDs y pertenencias. |
| T03.b | Backend: perfil repartidor, contexto de cuenta y aislamiento API/BD para las operaciones móviles. | Dos operadores, dos repartidores, escrituras ajenas y RLS con rol restringido; sin contexto, denegar. |
| T03.c | Webs: ingreso y llamadas API con la identidad central. | Alta/consulta existentes siguen funcionando; el encabezado demo no evita la autenticación. |
| T03.d | Mobile: composición MVVM, SQLite cifrado, archivos y migraciones locales. | Reinicio, clave incorrecta, temporales y actualización con pendientes. |
| T03.e | Mobile: ingreso, renovación, sesión sin red y biometría. | D05–D06 aplicadas; cierre/cambio bloqueados con pendientes, reingreso de la misma cuenta y cambio seguro después de sync. |
| T04.a | M01: reglas de prueba/motivos versionados y datos mínimos del repartidor/vehículo que requiere la ruta. | Configuraciones distintas por operador y conservación de las versiones aplicadas. |
| T04.b | M02: transiciones y eventos inmutables necesarios para asignar, despachar y ejecutar. | Transiciones válidas/inválidas, concurrencia y eventos con contexto. |
| T04.c | M03: ruta/paradas, asignación y despacho. | Bultos, límite de paradas, peso/volumen y franjas compatibles; orden documentado, asignación única y control de versión. |
| T04.d | API + mobile: consulta autorizada, descarga y lectura local de la hoja. | Contrato y reinicio en modo avión con snapshot coherente. |
| T04.e | Backend + Commerce: dirección, geocodificación automática, confirmación/corrección del comercio y coordenadas persistidas. | Resultado correcto, ambiguo, sin coincidencias y error de proveedor; cambio de dirección invalida la confirmación anterior. T04.d consume coordenadas confirmadas. |
| T06.a–d | Subida de evidencia; aplicación idempotente del evento; coordinación de sync local; conciliación. | Cada corte conserva su prueba de reintento/recuperación; el último verifica el recorrido completo. |

T03.a y T03.b no necesitan esperar a una cámara o un escáner funcionando. La integración móvil sí espera su prueba nativa correspondiente. T04.a–c deben revalidarse al comenzar: si otro integrante ya implementó esos contratos, integrar y verificar en lugar de duplicarlos. El aislamiento por BD incluye las políticas, configuración de contexto y migraciones pendientes; no se acredita únicamente con filtros de EF.

### T00 — Cerrar decisiones y recorte de entrega

- Resolver los puntos pendientes de D01–D15 con el usuario, actualizar esta tabla y separar alcance del 22/10 del cierre completo. D15 puede permanecer como bloqueo explícito de la cancelación pública si el usuario decide posponer ese mecanismo; no dar esa parte por terminada.
- Registrar hardware, acceso macOS/Apple, responsables por módulo y ambiente remoto disponible.
- Salida: decisiones fechadas, dependencias y criterios de aceptación sin ambigüedad. No comprometer tiempos antes de resolver estos puntos.

### T01 — Acordar contratos y dependencias del backend

- Depende de T00 para las decisiones correspondientes. Crear especificaciones de CU-18–24 y OPT-09 con sus contratos M01/M02/M03/M05 y AP-08.
- Definir rutas/DTOs/errores en OpenAPI, estados/transiciones, reglas de prueba, idempotencia y resolución de conflictos. Ampliar la especificación de la máquina de estados de la p. 6 con cancelación, reasignación con retorno a depósito y las reglas de validación pendientes, sin modificar el PDF original por inferencia. Fijar ejemplos de JSON de éxito, prueba pendiente, duplicado, lote parcial, acceso indebido y cancelación/reasignación concurrente.
- Descomponer los pendientes de M01/M02/M03 en tareas que puedan ejecutarse completas; cada una debe tener su propia prueba. No llamar “implementar backend” a una tarea indivisible.
- Salida: prueba de contrato que pueda verificar el cliente móvil, sin exigir datos reales ni presentar simulaciones como integración terminada.

### T02 — Comprobar temprano las integraciones nativas

- Depende de D02, D03 y D05. En Android, construir e instalar un Release mínimo en el S21; comprobar push visible con app cerrada, apertura desde el aviso, SQLite cifrado, lectura de una foto cifrada, biometría y ubicación con pantalla apagada.
- Documentar paquetes/versiones, permisos, firma, restricciones y resultado real de cada comprobación. Si una dependencia no compila o no funciona, resolverla antes de extender su uso.
- Preparar adaptadores y configuración iOS según documentación oficial. Sin Mac/iPhone, registrar **código preparado, compilación no ejecutada, recepción no probada**; no aprobar el soporte iOS real.
- Una notificación de prueba del proveedor solo valida transporte. OPT-09 se cierra después con una reasignación real desde NexoRuta.
- Para el mapa, descargar una zona, cerrar la app, desactivar la red y comprobar mapa base y marcador con coordenadas conocidas en Release. Evaluar integración MAUI Android/iOS, dependencias, consumo/costo y credenciales antes de confirmar Mapbox. Una vista que exige descargar estilos, scripts o teselas al abrir sin red no satisface D07.

### T03 — Identidad central, estructura móvil y datos protegidos

- Depende de T01 y de las pruebas nativas pertinentes de T02. Implementar el mecanismo central acordado, vinculado a `Usuario`/`AccesoUsuario` existentes, sin duplicar usuarios ni perder IDs o pertenencias.
- Mantener hashing, JWT y persistencia de sesiones en infraestructura/API, con las reglas de sesión y pertenencia en dominio/aplicación. Reutilizar el sistema central confirmado. Definir perfiles dentro de cada pertenencia; no conceder un rol global sobre todos los operadores por accidente.
- Agregar en backoffice el alta/gestión necesaria de cuentas de repartidores por el administrador de su operador, con pruebas de aislamiento. La pantalla de MAUI ofrece ingreso, no registro público.
- Integrar MAUI con el ingreso central, renovación, expiración y permisos actuales. El encabezado demo no puede conceder identidad. Implementar el contrato vigente sin capas para versiones anteriores.
- Implementar base MVVM, almacenamiento local cifrado y migraciones, clave protegida, sesión local y biometría según D05–D06. Contraseña nunca persistida en el dispositivo.
- Definir los recursos visuales MAUI a partir de la web y aplicarlos desde el ingreso: botones, campos, validaciones, indicadores de carga, mensajes y estados. Cada pantalla posterior reutiliza esos recursos y la misma terminología funcional.
- Verificar 401/403, renovación concurrente, dos operadores, cuenta ajena, reapertura sin red, pérdida de permisos y datos pendientes al salir. Si Identity usa tokens integrados, no tratarlos como JWT: son tokens propios del framework. [Identity](https://learn.microsoft.com/en-us/aspnet/core/security/authentication/identity-api-authorization?view=aspnetcore-10.0)

### T04 — Ruta descargada y utilizable sin conexión

- Depende de T03 y del despacho M03 acordado en T01. Implementar las operaciones mínimas de gestión/asignación/despacho necesarias en backend, con concurrencia para evitar doble asignación; no crear un CRUD general adicional.
- Implementar T04.e en el alta del portal: proponer coordenadas desde la dirección, mostrar el punto y exigir confirmación/corrección del comercio. Persistirlo de forma autorizada y preservar los envíos existentes al migrar; una fila histórica sin coordenadas no se transforma en una ubicación ficticia.
- Descargar solo rutas despachadas y asignadas al repartidor autenticado, junto con bultos, reglas y catálogos versionados. Persistir el conjunto coherentemente; una descarga parcial no reemplaza una ruta válida.
- Interfaz: hoja de ruta del día, paradas, detalle y estado de preparación/sincronización. Cada operación conserva su ruta y jornada de origen. Preparar también el mapa de la zona antes de salir del depósito; no agregar selector de múltiples rutas ni recargas en mitad del trayecto.
- Verificar descarga, reinicio en modo avión, actualización de versión y acceso denegado a ruta de otro repartidor/operador.

### T05 — Entrega durable con evidencia

- Depende de T04 y D08. Implementar formulario según política versionada: firma, foto comprimida, receptor/documento y hora/posición del dispositivo, cuando corresponda.
- Permitir declarar la entrega física aunque la prueba esté incompleta, con motivo/ausencia de datos explícitos y estado `Entrega pendiente de validación`. Ofrecer la posterior incorporación de evidencia con el circuito acordado, incluyendo funcionamiento sin red. No inventar coordenadas cuando falle la ubicación.
- Aplicar la política inicial de prueba pendiente hasta cierre de jornada, configurable/versionada por operador. Conservar qué evidencia se completó antes del cierre y tratar lo que quedó incompleto como `Entrega sin validación` cuando el cierre y sus operaciones previas se procesen.
- Al recibir y validar automáticamente todos los requisitos de la versión aplicable, el servidor registra la transición a `Entregado` y su evento, sin revisión manual del despachador. Comprobar requisitos incompletos, inválidos y completados en más de una sincronización.
- Guardar archivos sensibles cifrados y comando pendiente de forma recuperable, incluyendo fallos entre captura y commit. Evitar temporales con datos personales sin proteger.
- Verificar entrega completamente sin red y tras reinicio, doble toque, cámara cancelada, disco lleno, permiso denegado, GPS no disponible y prueba insuficiente; el resultado debe seguir la política acordada.

### T06 — Sincronización y primer flujo completo

- Depende de T05 y del contrato M02 de eventos/transiciones definido en T01. Implementar subida de evidencias, declaración física, validación posterior, aplicación/confirmación de eventos, descarga de cambios y estados visibles. Un evento de entrega física puede estar sincronizado y conservar prueba pendiente; distinguir ambos conceptos para aplicar el bloqueo de sesión de D06.
- Verificar idempotencia del servidor y durabilidad local con el mismo evento enviado repetidamente, caída tras commit antes de responder, lote parcialmente aplicado y cierre durante subida.
- Implementar D04: conciliar automáticamente una entrega física offline concurrente con cancelación/reprogramación si el servidor verifica jornada iniciada, carga confirmada y todos los bultos bajo custodia del repartidor y ruta originales. Suspender la parada de una ruta futura aún planificada e incrementar su versión; conservar el hecho y la evidencia, validando la prueba por D08. Reintentos del mismo ID devuelven la respuesta original. Otros estados/versiones o una custodia incompatible conservan el conflicto, sin cambios parciales ni aprobación/rechazo por despacho. No reescribir operaciones previas ni convertir una entrega física en un bulto sin entregar.
- Probar específicamente entrega física sin red concurrente con cancelación o reasignación del servidor. No convertir automáticamente al bulto ya entregado en uno a devolver, ni perder la declaración por el cambio remoto de ruta.
- Probar declaración física, prueba completada y cierre generados sin red: al reconectar, procesar la secuencia causal antes de evaluar vencimientos. Comparar también con prueba incompleta al cerrar, archivos pendientes de subir y respuesta perdida después de procesar el cierre.
- Salida demostrable: ruta real → modo avión → entrega → reinicio → reconexión → confirmación definitiva en backend → repetición sin efectos duplicados.

### T07 — Carga por escaneo

- Depende de T04; reutiliza persistencia y sync de T05–T06. Implementar lector QR/código de barras y verificación contra bultos asignados.
- Mostrar correctos, faltantes, sobrantes, duplicados y códigos desconocidos. Decidir con las reglas del operador si una discrepancia impide continuar; no inventar un bloqueo.
- Verificar cámara real, poca luz, escaneo repetido, permisos y operación sin red. Una entrada manual solo se agrega si el usuario o contrato la contempla.
- Registrar de forma durable la carga inicial en depósito, contra todos los bultos de la hoja del día. Las salidas de bultos durante el recorrido corresponden a entregas y sus pruebas; los restantes se declaran en la rendición al regresar. No implementar cargas intermedias ni recolecciones.

### T08 — Intentos fallidos

- Depende de T06 y D08. Usar motivos versionados descargados y evidencia configurada; compartir captura y persistencia con entrega.
- Backend valida transición, intentos y reglas del envío. La app no cambia catálogos ni decide por sí sola la devolución definitiva.
- Verificar sin red, motivo que cambió en servidor, reintento de sync y evento incompatible, sin perder pruebas.

### T09 — Devolución y rendición

- Depende de T06, T08 y D09. Implementar entrega de bultos al depósito, recepción/confirmación y trazabilidad según el circuito aprobado; incluir la acción mínima de depósito en backend/web.
- Repartidor declara, operario de depósito confirma recepción física y discrepancias; sin caja, cobros ni dinero. Incluir bultos reprogramados/reasignados para otra fecha y cancelados con retorno a depósito según D11–D14, distinguiéndolos de los entregados con prueba pendiente y de la devolución definitiva.
- Si ya hubo entrega física, tramitar la solicitud de reversión como devolución según D09: conservar la entrega original y confirmar cada recepción física cuando ocurra. No producir una cancelación retroactiva ni generar una recogida móvil sin especificar ese circuito.
- Exigir la verificación local de D05: biometría o credencial de bloqueo del teléfono. Identificar lo pendiente y lo confirmado; no declarar recibido algo que depósito aún no confirmó.
- Verificar bultos faltantes, cierre parcial si está permitido, repetición, operación sin red y sincronización. No agregar importes, caja ni cobros sin una definición explícita.

### T10 — Mapa de destino y seguimiento de la jornada

- Depende de T04, T06, D07 y D10. Permitir seleccionar paradas y mostrar el destino mediante un marcador en el mapa integrado. Descargar previamente la zona, incluidos los recursos necesarios para renderizarla, y mantener dirección y coordenadas disponibles localmente. Comprobar mapa base y marcador tras reinicio sin red.
- Mostrar progreso, tamaño y resultado de la descarga de la zona; manejar cancelación, espacio insuficiente y descarga parcial sin dañar una preparación válida. No presentar la hoja como completamente preparada sin los datos y el mapa requeridos para esa jornada.
- La letra (§4) pide navegación entre paradas, sin especificar indicaciones de giro. Para este plan se concreta como selección de parada y visualización del destino, según la aclaración del usuario. No incluir cálculo de recorrido, voz, indicaciones de giro ni abrir una app externa.
- Implementar Mapbox detrás de interfaz y comprobar la prueba T02.g. Android mínimo 11, aprobado por el usuario para el adaptador MAUI elegido. Sus SDK móviles documentan descarga de regiones sin conexión; eso no prueba compatibilidad de un adaptador MAUI concreto. Registrar versiones y uso previsto antes de confirmar el proveedor. [Mapas offline Android](https://docs.mapbox.com/android/maps/guides/offline/), [iOS](https://docs.mapbox.com/ios/maps/guides/offline/) y [precios](https://www.mapbox.com/pricing).
- Android: adaptar servicio de ubicación y permisos a la versión objetivo. iOS: adaptador Core Location y capacidades correspondientes. No implementar seguimiento continuo mediante un temporizador de una pantalla.
- Botones de inicio/fin de jornada accionados por el repartidor, con estado durable y operación idempotente. El inicio habilita el seguimiento; el fin lo detiene, también sin red. Despacho y rendición no activan/desactivan el seguimiento automáticamente ni el fin borra pendientes o confirma recepción en depósito.
- Registrar ubicación con objetivo inicial cada **30 segundos** durante la jornada. Medir los intervalos realmente obtenidos con pantalla apagada, batería y precisión en el S21; ajustar la configuración a partir de resultados. Un GPS no disponible deja una ausencia explícita, sin reutilizar una posición vieja como medición nueva.
- El fin de jornada también es el vencimiento inicial de las pruebas pendientes de D08: mostrar ese efecto antes de confirmar el cierre y persistirlo para su sincronización ordenada. Con prueba todavía incompleta se puede finalizar; la app muestra la prueba vencida y el cierre pendiente de sincronizar. El servidor confirma `Entrega sin validación` al procesar el cierre y su trabajo previo.
- Guardar puntos sin red y enviarlos después con autorización, respetando la política de sesión D06. Medir funcionamiento con pantalla apagada, reinicio del proceso cuando aplique, precisión y batería durante el período acordado. Probar que fuera de jornada o después de finalizarla no se reanuda el seguimiento al recrear una pantalla o reiniciar el proceso.
- Android impone requisitos de servicio `location`, permisos y restricciones al arranque desde segundo plano. Validarlos en el S21; no basta compilar. [Servicios de ubicación](https://developer.android.com/develop/background-work/services/fgs/service-types#location)

### T11 — Reasignación, cancelación y push real

- Depende de T01–T04, T06, D02, D11 y D14; aprovecha el transporte probado en T02. Implementar el flujo de la sección 8, desde la acción autorizada en backend/web hasta la consulta autenticada al tocar la notificación.
- Reasignar a una ruta posterior para suspender la entrega actual; conservar el bulto en custodia del repartidor hasta la rendición y recepción en depósito. Agregar cancelación autorizada y su aviso según D14. Al recibir cambios, mantener visible la obligación de rendir el bulto aunque ya no sea una parada a entregar.
- Incluir los accesos de cancelación del comercio y administrador autenticados; en Tracking, agregar la acción del destinatario únicamente con D15 resuelta e implementada. Después de la entrega física, tratar la solicitud como devolución en lugar de ejecutar la transición de cancelación.
- **Avance 19/10:** cancelación autenticada desde Comercio y Backoffice, registro inmutable, versiones, suspensión y aviso/outbox implementados. Se reutilizan `envios.crear` para el comercio propio y rol protegido `administrador_operador` más `envios.gestionar` para el administrador; un despachador con gestión de rutas no puede cancelar. Circuito público D15 implementado con código sin expiración, cinco intentos, reemisión y outbox de correo; falta SMTP real y aceptación remota. [Especificación](../../backend/docs/cu/cancelacion-envios.md).
- Capturar en la transacción/evento la ruta y el repartidor afectados **antes** de cambiar la asignación. El push se dirige solo a ese repartidor; comprobar que la consulta posterior de una ruta futura no redirige el aviso a su repartidor. El futuro repartidor recibe los datos en su hoja del día, sin push urgente.
- Alta/renovación/baja de registros por instalación y cuenta; tratamiento de rotación, reinstalación, registros inválidos, revocación y cambio de usuario.
- Aislar ambientes y destinatarios; proteger credenciales del proveedor en Worker. Comprobar reintentos y fallo definitivo sin prometer entrega exactamente una vez.
- Salida: reasignación desde backend/web, registro transaccional, mensaje y push visibles en S21 con app cerrada, con evidencia correlacionada. iOS conserva su estado pendiente hasta tener compilación y recepción reales.

### T12 — CI, distribución y aceptación

- Depende de las tareas incluidas en el recorte aprobado. CI ejecuta pruebas de lógica/contratos, backend y compilación Android Release con versiones fijadas. Añadir compilación iOS en macOS cuando exista runner compatible y permisos/costos aprobados.
- Producir **APK Android Release firmado**, instalarlo directamente en el S21 y conservar versión, checksum y resultado de la instalación/pruebas. Guardar keystore y contraseñas fuera del repositorio; documentar cómo firmar una actualización con la misma identidad.
- Para iOS, entregar código/adaptadores/configuración y lista de comprobaciones pendientes. Con los recursos actuales no producir ni afirmar un IPA validado o una publicación en App Store/TestFlight. Verificar migración de una instalación anterior sin borrar operaciones locales pendientes.
- Correlacionar operación móvil → API → Worker sin registrar documentos, fotos, contraseñas, claves ni identificadores push completos. Medir pendientes, conflictos, antigüedad de sync, reintentos y fallos push con estados observables.
- Ejecutar la matriz de aceptación y registrar por separado pruebas automáticas, simulador, teléfono y entorno remoto. Una compilación o un proveedor que acepta un envío no prueban recepción en el teléfono.

## 8. Diseño y aceptación de OPT-09

Flujo con FCM confirmado; reasignación significa postergar a otra ruta con retorno físico a depósito. Reasignación y cancelación notifican solo al repartidor de la ruta actual afectada. Quedan reglas de permisos y transiciones por cerrar en D11–D14:

```text
Actor autorizado según la operación
  → API valida permisos, asignación, estado y versión mediante M03/M02
  → transacción: reasignación/cancelación + evento/outbox
  → publicador → RabbitMQ → Worker independiente
  → instalación vigente del repartidor afectado de la ruta original → proveedor push
  → Android / APNs en iOS → notificación visible
  → usuario toca aviso → app valida sesión → API devuelve asignación actual
```

El aviso informa de un cambio; la fuente de verdad sigue siendo el backend. Al abrirlo, comprobar cuenta, permiso y versión actuales antes de mostrar datos o modificar la ruta local. Reconciliar con los comandos pendientes; no reemplazarlos por el contenido del push.

La consulta de la ruta original debe permitir al repartidor autorizado conocer la suspensión y la obligación de rendir el bulto, aunque el envío haya salido de sus entregas activas. No concederle acceso a la nueva ruta ni a sus datos por haber recibido el aviso. No se envía push urgente al repartidor futuro.

El aviso durante reparto permite suspender la entrega y rendir el bulto al regresar; no implica que el siguiente repartidor pueda cargarlo antes de su recepción en depósito. La prueba de cancelación complementa el opcional: la demostración de OPT-09 también debe incluir la reasignación urgente exigida por la letra.

Con FCM, evaluar payload visible con datos mínimos para navegación, tratamiento de primer/segundo plano, canales Android y arranque en frío. En Android, mensajes visibles en segundo plano pasan por la bandeja del sistema y los datos se reciben al abrir la actividad; no depender solo de `OnMessageReceived` para navegar. [Recepción FCM](https://firebase.google.com/docs/cloud-messaging/android/receive-messages)

Para iOS hacen falta configuración APNs, permisos, entitlements, firma y herramientas Apple. Una notificación visible y una actualización silenciosa en segundo plano tienen garantías distintas: el plan exige aviso visible para la reasignación y no depende de ejecutar código silencioso con la app terminada. [FCM en Apple](https://firebase.google.com/docs/cloud-messaging/ios/client)

Propuesta de alcance de “cerrada”, por acordar: app previamente instalada/abierta/registrada, fuera de primer plano, pantalla bloqueada y proceso terminado mediante cierre normal. Probar también retirada de recientes según el dispositivo. Registrar por separado **Forzar detención**, permisos revocados y ausencia de conectividad; el estado detenido de Android tiene restricciones del sistema hasta una acción del usuario. No prometer entrega inmediata en esos estados. [Estado detenido de Android](https://developer.android.com/about/versions/15/behavior-changes-all#stopped-state)

Ciclo de vida y confiabilidad:

- Registrar instalación, cuenta/operador autorizados, plataforma, ambiente y fecha de actividad; actualizar el registro cuando el SDK lo renueve. Seleccionar la API disponible en las versiones comprobadas, sin asumir que un token y un Firebase Installation ID son intercambiables.
- Desasociar al salir/cambiar de cuenta y rechazar registros ajenos, respetando el bloqueo de D06 mientras haya pendientes sin sincronizar. Después de sync, definir baja del registro y limpieza de sesión sin permitir un aviso dirigido a la cuenta anterior. Evitar datos personales en el aviso visible en pantalla bloqueada.
- Invalidar registros que el proveedor confirme como expirados; no invalidar automáticamente ante cualquier `400`, que también puede indicar un payload incorrecto. [Ciclo de vida FCM](https://firebase.google.com/docs/cloud-messaging/manage-tokens)
- Respetar expiración/vigencia acordada y comprobar que el cambio siga siendo relevante antes de enviar. El destinatario es el repartidor afectado capturado en el evento, no el de una asignación futura; validar su identidad y registro vigentes sin suprimir el aviso por haberse retirado el envío de su ruta. Reintentos transitorios con espera creciente y tratamiento de fallo definitivo, reutilizando el mecanismo existente donde corresponda.
- Distinguir evento persistido, envío pendiente, proveedor aceptó, abierto y confirmado por app si el contrato lo contempla. No llamar “entregado” al resultado exitoso del SDK del servidor.
- Registrar ID de evento/notificación y deduplicar procesamiento y apertura. Una caída después de que el proveedor aceptó puede causar un aviso repetido: no hay transacción atómica BD+proveedor ni garantía de exactamente una vez.

Aceptación mínima en Android: reasignación real remota, app cerrada y pantalla bloqueada, aviso visible y apertura correcta; sesión vencida; usuario equivocado; doble envío; aviso antiguo; permiso rechazado; teléfono sin red y reconexión; rotación/baja y registro inválido. Repetir los casos aplicables en iOS cuando haya recursos. Sin esa evidencia no se cierra OPT-09 para iOS.

Costo verificado al planificar: Firebase lista **Cloud Messaging sin costo**, también en Spark sin método de pago. Esto no cubre hosting de NexoRuta, recursos Apple, CI macOS ni otros productos Firebase. La propuesta utiliza el Worker existente y no requiere Cloud Functions, Firestore ni Firebase Authentication. [Precios Firebase](https://firebase.google.com/pricing)

## 9. Ejecución con IA y control de errores

Una sesión de implementación toma una tarea o un corte vertical pequeño de ella. Antes de tocar código:

1. Leer este plan, respuestas confirmadas, especificación del CU, OpenAPI y código actual de todos los componentes afectados. Revalidar la situación del checkout: este plan no congela el backend.
2. Nombrar resultado observable, dependencias, archivos/repositorios afectados, casos de fallo y condición de parada. Si falta una regla de negocio, preguntar antes de escribirla.
3. Hacer primero el contrato y una prueba significativa cuando haya lógica de negocio, sincronización, concurrencia, seguridad o preservación de datos; después implementar el corte mínimo completo.
4. Verificar conjuntamente pantalla, estado local, payload, autorización, persistencia del servidor y efectos asíncronos cuando correspondan. Un mock verifica su contrato, no la integración real.
5. Leer el diff completo y ejecutar las comprobaciones pertinentes. Registrar comandos, cantidad real de pruebas, plataforma, dispositivo, ambiente y resultado. No marcar terminado un punto que solo compiló o no se pudo ejecutar.
6. Actualizar especificación, decisiones y bitácora individual según las convenciones del backend. Conservar cambios ajenos y el manejo separado de submódulos; no hacer commit, push ni despliegue por inferencia.

Formato de una tarea para encargar a IA:

```text
Tarea: Txx, corte concreto y requisito de origen.
Leer: plan, decisiones Dxx confirmadas, CU, contrato, implementación actual.
Resultado: recorrido que una persona puede ejecutar y observar.
Dependencias: implementaciones/decisiones que deben existir antes.
Cambios permitidos: componentes y contratos que alcanza esta tarea.
Reglas: invariantes y ejemplos de éxito/fallo ya acordados.
Validación: pruebas automáticas, dispositivo y/o HTTP necesarios para este corte.
Parar: al cumplir criterios; ante una regla ausente, preguntar sin inventarla.
Entrega: diff, pruebas realmente ejecutadas, limitaciones y próximos bloqueos.
```

Para reanudar entre sesiones, actualizar junto a cada tarea su estado (`pendiente`, `bloqueada por Dxx`, `en curso`, `verificada Android`, `pendiente iOS`, `completa`) y la referencia a su evidencia. No duplicar todo este documento en cada prompt.

## 10. Hitos y definición de terminado

La letra fija el **22/10/2026** para descargar ruta, registrar entregas sin conexión y sincronizar; el **12/11/2026** para opcionales completos y el **15/11/2026** para entrega final. Son fechas del curso, no estimaciones de este plan.

- Primer flujo que acredita el monitoreo del 22/10: T00–T06 con contratos reales, una ruta despachada y una entrega con la evidencia que efectivamente exija la regla configurada; T02 anticipa riesgos de hardware y push. No declarar completa la operación móvil por demostrar solo ese recorrido ni presentar esa fecha como estimación de implementación.
- Cierre funcional móvil: completar CU-18–24, AP-08, biometría, seguridad local y casos de conflicto, con prueba en Android.
- Cierre de OPT-09: flujo de reasignación remoto y ciclo de vida de registros probado en teléfono; evidencia distinta del envío manual desde la consola del proveedor.
- iOS: implementar código y configuración, luego compilar en macOS y probar en iPhone. Mientras falten esas comprobaciones, informar el soporte como pendiente de validación; no atribuirle la evidencia Android.

Checklist de aceptación global:

- [ ] Coherencia visual mobile/web comprobada en ingreso, listas, detalles, formularios, mensajes y estados, con recursos MAUI compartidos y evidencia en S21.
- [ ] Un único contrato vigente; sin flujos de compatibilidad para APIs o clientes anteriores.
- [ ] Puntos pendientes de D01–D15 resueltos, o aplazamientos/bloqueos explícitos acordados, y contratos versionados con responsables y recorte de entrega.
- [ ] Una identidad central para web/mobile, pertenencias preservadas y permisos por operador/repartidor.
- [ ] Repartidores creados/gestionados por administrador del operador, sin autorregistro público en la app.
- [ ] BD, evidencias y claves protegidas, incluyendo temporales, backups y actualización de esquema.
- [ ] Jornada sin red con reinicio y sin pérdida de operaciones; sincronización parcial, reintentos y duplicados comprobados.
- [ ] Cierre/cambio de cuenta bloqueados con operaciones o evidencias pendientes; reautenticación de la misma cuenta y desbloqueo tras sync comprobados.
- [ ] Mapa de la zona preparado antes de salir; mapa base y destino visibles después de reiniciar en modo avión.
- [ ] Dirección geocodificada automáticamente y ubicación confirmada/corregida por el comercio antes de descargarse al móvil.
- [ ] Inicio/fin manual de jornada activa/detiene ubicación; no hay seguimiento automático fuera de la jornada iniciada.
- [ ] Conflicto de estado y reasignación resuelto según política, con evidencia preservada.
- [ ] Entrega física con prueba pendiente, validación posterior y vencimiento distinguidos de sincronización pendiente y devolución física.
- [ ] Cierre de jornada como plazo inicial de prueba, política versionada y sincronización tardía de evidencia anterior al cierre comprobados sin pérdida o falso vencimiento.
- [ ] Reasignación a otra ruta y cancelación durante reparto conservan custodia, aviso al repartidor y rendición/recepción en depósito.
- [ ] Cancelación del destinatario con verificación acordada, del comercio y del administrador autorizadas; después de entrega física se usa el circuito de devolución.
- [ ] Carga, pruebas de entrega, fallos, devoluciones, rendición, navegación, biometría y ubicación con pantalla apagada comprobados.
- [ ] Aislamiento entre operadores/repartidores en API, sincronización, almacenamiento y Worker; RLS verificado con rol restringido cuando se incorpore.
- [ ] Push real desde una reasignación, registro/renovación/baja y manejo de avisos antiguos o repetidos.
- [ ] CI, APK Android Release firmado e instalación directa en S21 comprobados; estado iOS informado según evidencia disponible.
- [ ] Documentación de instalación, recuperación de sync y demostración reproducible; pruebas y bitácora actualizadas.

### Criterio de experiencia de uso — actualización 21/10

La app se utiliza normalmente conectada y mantiene capacidad de trabajar sin red. El repartidor trabaja con su **hoja de ruta**, paradas, bultos, entregas y rendición: no debe gestionar almacenes locales, descargas de datos o sincronización. Actualizar la información automáticamente, preservar siempre el trabajo pendiente y mostrar problemas de conectividad solo cuando requieran una acción o expliquen un resultado pendiente. Mantener como referencia visual el Backoffice, adaptándolo a tareas rápidas, lectura clara y controles táctiles en reparto. La preparación del mapa antes de salir continúa como requisito operativo; no equivale a pedir una descarga manual de la hoja.

Implementación y evidencia de la revisión visual: [ejecución completa, etapa 21/10](ejecucion-completa.md). La validación visual de pantallas no sustituye la aceptación del circuito completo ni cierra las dependencias externas pendientes.
