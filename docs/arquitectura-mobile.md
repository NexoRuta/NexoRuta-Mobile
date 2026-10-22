# Arquitectura de NexoRuta Mobile

Actualización operativa: [validación local y S21 del 20/10](validacion-local-s21.md). La integración parcial de CU-04 fija la publicación al crear el envío y descarga sus reglas en cada parada; se retiraron los intentos, pruebas y motivos duplicados de la configuración de reparto. El alcance y los límites están en [reglas del envío](reglas-envio.md). `VistaHoja` y `VistaParada` muestran cambios remotos sin modificar la hoja original ni los sobres causales pendientes.

## Estado y límites al 19/10/2026

La app implementa los flujos de jornada en MAUI y los adaptadores Android. La API central conserva la autoridad sobre permisos, asignaciones, custodia y validación de entrega. Las pruebas controladas y la compilación no acreditan integración con Firebase, HTTPS, correo o mapas reales. La evidencia vigente y los pendientes están en [ejecución completa](ejecucion-completa.md).

```text
MAUI: MVVM, navegación, SecureStorage, biometría, cámara, mapas, GPS y push
  → Application: sincronización causal, evidencia local y avisos
      → Api: cliente HTTPS con autenticación central y control de cuenta
          → Core: identidad y contratos de jornada/ruta
      → Storage: SQLite cifrado y archivos cifrados por cuenta
          → Core: diario de operaciones inmutable
```

Las cuatro capas portables se prueban sin workloads MAUI. Core no referencia EF, ASP.NET ni código del servidor. Los proveedores nativos se componen en `MauiProgram`. Los mapas se consumen mediante una interfaz; no hay navegación giro a giro ni geocodificación durante el reparto.

## Sesión y almacenamiento

El backend vigente usa Identity central y JWT. Mobile usa renovación rotativa con vencimiento absoluto a los 30 días del ingreso y versión de credencial. Antes de enviar un refresh, se guarda un estado sin tokens; perder la respuesta exige reingreso de la misma cuenta, sin borrar trabajo. El permiso local de biometría o credencial del teléfono permite abrir la jornada descargada; no sustituye la autorización online para iniciar otra.

SQLite usa cifrado ChaCha20 mediante SQLite3MC; las evidencias usan AES-GCM con asociación a cuenta y archivo. Las claves permanecen en SecureStorage. No se almacenan fotos en temporales planos. Perder una clave bloquea la lectura y conserva los archivos. Cada cuenta tiene su propio directorio y sus propias claves.

`Hojas` conserva la hoja original que alimenta las versiones de sincronización. `HojasRemotas` es una vista de consulta, separada del diario y de los sobres ya congelados. Las confirmaciones causadas por comandos propios promueven versiones; una lectura remota nunca reescribe un comando pendiente. La proyección de pantalla compara versiones para evitar retroceder frente a una confirmación propia más nueva.

## Salida protegida

La salida se orquesta en Application: comprueba pendientes y suspende el repositorio bajo el mismo semáforo que usan GPS, capturas y sincronización. El cliente mantiene su semáforo de autenticación durante la baja de instalación, la revocación y la eliminación de credenciales. Así se impiden tanto escrituras locales tardías como un registro push encolado que reactive el destino después de la baja.

Un fallo reanuda el almacén y conserva la identidad. El éxito conserva todos los datos y claves de la cuenta, detiene seguimiento/avisos y permite otro ingreso. El servidor autoriza la baja de instalación por cuenta y exige sesión móvil, incluso si se retiró el permiso de reparto. Reingresar a la cuenta anterior reanuda su repositorio. No se termina la jornada ni se cambia el estado de envíos al cerrar sesión. [Protocolo y pruebas](sesion-http.md).

## Sincronización y avisos

Cada operación se guarda antes del envío. El sobre HTTP se congela antes de su primer intento y se reenvía sin alterarlo si se pierde la respuesta. Las evidencias se suben antes del hecho que las referencia. El resultado por elemento se persiste antes de avanzar; conflicto no significa borrar el hecho físico ni su prueba. El cierre respeta la secuencia y espera confirmaciones anteriores.

El despachador solo despacha: no decide sobre entregas, devoluciones ni conflictos del paquete. Si el destinatario recibió el paquete, la entrega física se conserva aunque antes hubiera pedido cancelar; una devolución posterior es otro hecho. Un paquete que vuelve sin entregarse se rinde y se recibe en depósito, sin registrarlo como entregado.

El servidor concilia automáticamente la entrega offline frente a cancelación/reprogramación cuando la versión quedó atrás y la jornada tiene salida confirmada y todos los bultos siguen bajo custodia de ese repartidor/ruta. Registra entrega y prueba, pasa los bultos al destinatario y suspende la parada de cualquier ruta futura aún planificada, incrementando su versión. Todo se guarda con la operación y su respuesta inmutables en una transacción bajo el bloqueo del operador; un reintento devuelve esa misma respuesta. Las reglas de prueba y su plazo no cambian. El cierre de otra ruta no vence la prueba de esta entrega. Una custodia incompatible, una versión inválida u otro estado no se fuerzan: se conservan como conflicto y sin modificaciones parciales. Los conflictos ya persistidos no se reescriben.

Un único sincronizador serializa los disparadores manuales, GPS y reconexión. La reconexión y la activación de la ventana reanudan también jornadas cerradas con pendientes, en intentos acotados a 45 segundos. Cada envío verifica la cuenta esperada bajo el mismo semáforo que la autenticación.

La reasignación del servidor cambia la ruta lógica, conserva la custodia física y genera aviso/outbox en la misma transacción. El Worker obtiene destinos mediante funciones SQL con reserva vigente y permisos restringidos; no tiene lectura general de las tablas operativas. FCM aceptado, recibido, abierto y confirmado son hechos distintos.

La cancelación autenticada usa esa misma entrega de avisos. Una función SQL acotada permite al comercio cancelar su propio envío sin habilitarle UPDATE general ni lecturas de rutas; el administrador del operador también puede cancelar y el despachador no. Se persisten cancelación inmutable, versiones, suspensión y aviso/outbox en una transacción; se conserva la custodia hasta rendición/recepción. El aviso móvil contempla una entrega ya recibida offline y nunca le exige rendir ese paquete. Tracking incorpora solicitud y confirmación del código de un uso por correo obligatorio: sin vencimiento temporal, cinco intentos y reemisión. Reutiliza el núcleo transaccional de cancelación y el outbox/RabbitMQ/SMTP existentes, con hash de código y sobre protegido; falta envío a un buzón real. [Contrato](../../backend/docs/cu/cancelacion-envios.md).

El push contiene solo versión e identificadores de aviso/operador/acceso. Android guarda la referencia cifrada y espera desbloqueo antes de consultar su vigencia en la API. Avisos repetidos se agrupan por ID; los de otra cuenta se rechazan. Se consulta la ruta antes de mostrar una instrucción vigente. La vista remota puede impedir una nueva entrega, pero no elimina evidencias previas. El detalle está en [push y reasignación](push-reasignacion.md).

## Presentación y plataformas

La referencia visual es Backoffice, adaptada a controles táctiles y pocas acciones por pantalla. La app se bloquea al detenerse y protege la captura de pantalla en Android. Un permiso cancelado no autoriza una rendición.

Android Release ARM64 compila con MAUI 10.0.60, Android mínimo 11. Mapbox mantiene tres advertencias de páginas nativas de 16 KB para Android 16. iOS mínimo 15 sigue sin compilación ni prueba; su adaptador push está bloqueado por el binding FID y por recursos Apple. No hay compatibilidad con contratos anteriores.

## Registro de jornada

`JornadaLocal` registra inicio, declaraciones físicas, incorporación de evidencia y finalización. Cada operación tiene UUIDv7 estable, secuencia, fecha del dispositivo con desplazamiento horario y datos JSON propios. Los datos se copian al registrar la operación para conservarlos aunque se libere el documento JSON original.

- El orden depende de la secuencia, no del reloj del teléfono. Se conserva la fecha realmente informada; no se cambia para forzar el orden.
- Finalizar conserva un único ID y fecha de cierre, incluso si se repite la acción.
- Después de finalizar no se pueden insertar declaraciones o pruebas con una fecha anterior para eludir el plazo.
- `PrepararEnvio()` devuelve primero las operaciones previas sin confirmar. El cierre solo queda disponible cuando todas tienen confirmación.
- `ConfirmarPersistencia()` es idempotente, rechaza IDs ajenos y no permite confirmar el cierre antes del trabajo previo. No elimina operaciones.
- `TienePendientes` permanece verdadero hasta confirmar todo, incluido el cierre. La capa de sesión deberá usar este estado junto con las evidencias pendientes de subir para bloquear cierre/cambio de cuenta.

La confirmación de persistencia solo podrá invocarla el futuro adaptador de sincronización tras verificar la respuesta del servidor y la persistencia de las evidencias asociadas. Una conexión disponible, un HTTP `202` o un ACK de transporte no bastan. El cliente HTTP de identidad no implementa sincronización de jornadas, reintentos de negocio ni confirmaciones reales de entregas.

## Estado local versión 1

`EstadoJornadaLocal` guarda identidad de jornada/ruta, cuenta, registro completo y confirmaciones. `CuentaMovil` distingue `UsuarioId`, `AccesoId` y `OperadorId`; ninguno sustituye a los otros ni autoriza por sí solo una acción del servidor.

`SerializadorJornadaLocal` utiliza generación de código de `System.Text.Json`, sin serialización por reflexión en el núcleo. Las propiedades usan camelCase y los tipos de operación se escriben como texto. Es un formato local versionado, **no un contrato HTTP ya soportado por la API**.

La restauración exige la cuenta actual esperada y rechaza:

- otra identidad, acceso u operador;
- versión desconocida, secuencia rota, IDs duplicados o inicio ausente;
- cierre intermedio o cierre confirmado con operaciones previas pendientes;
- confirmaciones ajenas/repetidas y JSON nulo, truncado o con campos desconocidos.

La serialización produce bytes en memoria. **No implementa persistencia durable ni cifrado.** El adaptador SQLite cifrado de T03 deberá guardar estado/archivos de forma segura y transaccional antes de informar éxito local. No escribir estos bytes directamente en archivos, preferencias o logs. La biblioteca no contiene credenciales ni un mecanismo de autenticación local.

El modelo es síncrono; la composición futura debe serializar las escrituras de la jornada y la coordinación de sincronización. No se ha demostrado acceso concurrente ni recuperación ante fallos de almacenamiento.

## Verificación histórica del primer corte

En contenedor temporal `mcr.microsoft.com/dotnet/sdk:10.0`, con SDK **10.0.401**, runtime **10.0.12** y caché NuGet en `/tmp/nexoruta-mobile-nuget`:

```bash
dotnet test tests/NexoRuta.Mobile.Core.Tests/NexoRuta.Mobile.Core.Tests.csproj \
  --configuration Release --logger 'console;verbosity=normal'
```

Restore, compilación y **25 pruebas pasaron**. Cubren cierre ordenado, confirmaciones parciales y repetidas, evidencia anterior al cierre restaurada después de serializar, cambios de reloj, aislamiento de cuenta, estado corrupto y dependencias de arquitectura.

La prueba de restauración utiliza memoria serializada: no acredita durabilidad de SQLite, cifrado, HTTP, ejecución en Android/iOS ni publicación AOT de la app.

## Entorno nativo Ubuntu preparado

El primer intento en Docker:

```bash
dotnet build src/NexoRuta.Mobile/NexoRuta.Mobile.csproj \
  --configuration Release --framework net10.0-android --no-restore --nologo
```

Resultado inicial: **`NETSDK1147`, falta `maui-android`**. La revisión posterior encontró Android SDK y ADB en `~/Android/Sdk`, fuera del `PATH`. Se corrige el diagnóstico inicial de que no estaban instalados.

El usuario autorizó preparar Ubuntu/VS Code. Se instalaron SDK .NET **10.0.401**, workload **`maui-android`** (`10.0.0/10.0.100`, .NET Android `36.1.2`), extensión **.NET MAUI 1.17.289** y sus dependencias, herramientas Android adicionales mediante `InstallAndroidDependencies` y **Microsoft OpenJDK 21.0.12.1** completo. Java 21 existente era solo un runtime; Java 25 no es compatible con este workload. No se cambiaron los paquetes Java del sistema.

Las variables y rutas están en `~/.config/nexoruta/android-env.sh`, cargado por `.profile`/`.bashrc`. VS Code tiene las rutas Android/JDK en ajustes de usuario, y se guardaron copias de los archivos previos. Detalles y conexión inalámbrica en el [README](../README.md). [Instalación oficial](https://learn.microsoft.com/en-us/dotnet/maui/get-started/installation?view=net-maui-10.0).

Comprobado en Ubuntu, con el entorno cargado:

```bash
dotnet test tests/NexoRuta.Mobile.Core.Tests/NexoRuta.Mobile.Core.Tests.csproj \
  --configuration Release --nologo
dotnet build src/NexoRuta.Mobile/NexoRuta.Mobile.csproj \
  --configuration Release --framework net10.0-android \
  -p:RuntimeIdentifier=android-arm64 -p:AndroidPackageFormats=apk --nologo
```

Resultado: **25 pruebas correctas; build Android Release correcto, 0 advertencias y 0 errores**. APK generado en `src/NexoRuta.Mobile/bin/Release/net10.0-android/android-arm64/com.companyname.nexoruta.mobile-Signed.apk`. La compilación incluye optimización de ensamblados y AOT Mono; no demuestra funcionamiento de adaptadores aún no implementados ni publicación NativeAOT.

El S21 tiene el USB averiado; se validó por ADB inalámbrico después de que el usuario renovara el emparejamiento desde Android Studio. Modelo **SM-G991U, Android 14/API 34**. `adb install -r` informó `Success`; el arranque en frío con `am start -W` informó `Status: ok` y el proceso permaneció activo. Se leyó la jerarquía UI de la app: `Hello, World!`, bienvenida MAUI y botón `Clicked 2 times`, visibles. El XML temporal se eliminó del teléfono; no se guardaron códigos de emparejamiento ni direcciones del dispositivo en el repositorio.

`apksigner` comprobó la firma v1/v2/v3, con certificado **Android Debug**. Las advertencias del esquema v1 sobre entradas `META-INF` no invalidaron la verificación; el APK tiene también firmas v2/v3. Esta firma es local de desarrollo, no la clave definitiva de distribución. El build de MSBuild tuvo 0 advertencias y 0 errores.

Esto comprueba compilación, instalación y UI de la plantilla, no el depurador de VS Code, Hot Reload ni los adaptadores futuros. iOS sigue sin compilación ni pruebas, por falta de Mac/Xcode/iPhone.

La siguiente etapa debe completar los contratos operativos de T01, el perfil repartidor y las pruebas nativas restantes de T02. Para conectar el ingreso MVVM se necesita un origen HTTPS confiable, persistencia operativa y biometría. El backend y el cliente de identidad no acreditan T01 completa ni el ingreso MAUI. La cancelación pública mantiene el bloqueo D15 y la selección de Mapbox todavía requiere evaluación.

## Integración parcial de reglas por envío

La app consume la publicación capturada al crear cada envío, mediante `ParadaRuta.Reglas`. La configuración común de ruta conserva los parámetros propios de reparto. [Contrato, límites y preparación del entorno](reglas-envio.md).

## Preparación cartográfica por jornada

`ZonaMapa` calcula cobertura/huella; `ServicioPreparacionMapa` publica la preparación en el almacén cifrado existente y consulta `IProveedorMapaOffline`. La vista depende de `IProveedorMapas`. Android usa el mismo SDK Mapbox del control mediante un facade Java pequeño, con cancelación, espera de estilo/teselas y consulta al reabrir; no basta el callback de región del control. [Persistencia, recursos y limitaciones de plataforma](mapa-offline.md). No hubo cambios adicionales en CU-04 en este corte.


## Registro de ubicación y desarrollo sin huella — 21/10/2026

La coordinación de ubicación se trasladó al proyecto Application; Android e iOS conservan sus adaptadores nativos e inyectan el mismo servicio. El diario cifrado controla el intervalo tras reinicio, el inicio autorizado y el cierre atómico. [Reglas y evidencia](ubicacion-jornada.md).

Por solicitud del usuario se agregó `OmitirVerificacionLocalParaDesarrollo`, con TODO para ponerlo en `false` antes de la aceptación biométrica. Solo vale con `ValidarAlmacenamiento=true`, compilación restringida al ID `uy.nexoruta.repartidor.validacion`. El ID habitual rechaza ese modo y el script de distribución fuerza `ValidarAlmacenamiento=false`. El atajo omite únicamente la comprobación local: no elimina el ingreso central ni concede permisos de API. Las pruebas realizadas con él no acreditan biometría.
