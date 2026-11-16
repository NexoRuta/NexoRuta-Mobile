# Aceptación nativa de Android — preentrega

Ejecutá estos guiones con **NexoRuta Repartidor** en un teléfono Android físico, con bultos, imágenes y cuentas ficticias en un entorno de prueba. Identificá la API y prepará el APK según la [guía de Android](../../tools/local/README.md#instalar-y-verificar-en-android). Completá un registro por ejecución y guion.

| Guion | Estado de esta preentrega | Qué falta acreditar |
| --- | --- | --- |
| [AN-01 — Entrega parcial offline](entrega-parcial-offline.md) | **Pendiente de ejecución nativa** | Escaneo real, modo avión, reapertura y acuse de entrega parcial sin duplicados. |
| [AN-02 — Ruta mixta y retiro](ruta-mixta-retiro.md) | **Pendiente de ejecución nativa** | Cámara, firma, acuse, rendición y recepción física separadas en el recorrido completo. |

No se instaló ni identificó un APK candidato para estos guiones. El [registro histórico del S21](../validacion-local-s21.md) identifica un SM-G991U con Android 14 y acredita ingreso y descarga por HTTPS, además de un ensayo de push. Es evidencia de esa ejecución, no un resultado para AN-01 o AN-02. Las pruebas portables tampoco sustituyen la ejecución en el dispositivo.

## Ficha de ejecución

| Dato | Registro |
| --- | --- |
| Fecha, hora y zona / guion / responsable | Pendiente |
| APK: nombre, enlace o referencia, SHA-256, versión y código de versión, ABI, Debug/Release y commit de Mobile | Pendiente |
| Dispositivo: modelo, versión de Android y ABI; sin número de serie personal | Pendiente |
| API: entorno, origen HTTPS y versión/commit del Backend | Pendiente |
| Datos de prueba: identificadores de jornada, visita y bultos ficticios | Pendiente |
| Comunicación: resultado de `/ready`, ingreso y descarga desde Android | Pendiente |
| Resultado por paso: aprobado, fallido, bloqueado o no ejecutado; esperado frente a observado | Pendiente |
| Evidencia: referencias a capturas, acuses, estado y custodia antes/después; anotar IDs de operación ficticios y secuencias | Pendiente |
| Resultado global / casos pendientes / dependencia o motivo del bloqueo | Pendiente de ejecución |

Registrá si se usó Debug, porque no acredita verificación biométrica de Release. Ocultá credenciales, tokens, FID, datos personales y contenido sensible en capturas o logs. Si falla un paso, conservá la cola y registrá el estado antes de reintentar. Un paso bloqueado o sin evidencia queda pendiente.
