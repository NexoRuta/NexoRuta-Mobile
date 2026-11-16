# ADR — Conflictos en la sincronización offline

**Estado:** decisión vigente; formaliza el comportamiento existente.

**Alcance:** operaciones de jornada móvil, en particular `EntregaBultos` y `RetirarBultos`. No modifica contratos ni reglas de negocio.

## Contexto

El repartidor puede capturar un hecho sin conexión. Mientras tanto, el servidor puede cambiar la versión o custodia de un bulto, retirar la parada o resolver una visita. También puede aplicar una operación y perderse la respuesta. Ambos escenarios se ven inicialmente como trabajo pendiente en el dispositivo, pero requieren acciones diferentes.

## Decisión

El cliente registra primero la operación en el diario local. Sube las evidencias asociadas antes de enviar el comando que las referencia y congela el sobre antes de su primer envío. Los comandos se envían en orden causal por jornada. Una lectura remota actualiza la vista de consulta: no cambia la hoja base, los sobres congelados ni sus precondiciones. Antes de congelar un comando que aún no salió, el sincronizador solo incorpora versiones que provienen de acuses válidos de operaciones propias aplicadas.

| Caso | Decisión y efecto |
| --- | --- |
| **Versión obsoleta** | Para entrega y retiro se validan las versiones de los bultos y la pertenencia a la visita y custodia correspondientes. Si uno no cumple, el servidor rechaza el comando completo de esa visita. No actualiza las versiones esperadas con una lectura remota para volver a intentarlo. |
| **Respuesta perdida** | Hasta obtener y guardar un acuse válido, la operación sigue pendiente. Al recuperar conexión o usar **Actualizar**, el transporte reenvía exactamente el mismo sobre, UUID y secuencia. Si el servidor ya lo registró, devuelve su acuse original para ese contexto y contenido, sin repetir custodia, eventos ni otros efectos. |
| **Rechazo persistido** | Un acuse de negocio con `resultado: "Conflicto"` consume secuencia y queda en ambos diarios. Conserva operación, causa y pruebas sin aplicar el cambio de negocio ni promover sus versiones. La UI muestra un rechazo; no lo presenta como entrega aplicada, prueba pendiente ni retiro disponible para rendir. Un error HTTP por secuencia o reutilización incompatible del ID no equivale a este acuse. |
| **Reintento explícito** | Tras revisar y resolver la causa, si la visita y la custodia siguen siendo admisibles, **Reintentar entrega** o **Reintentar retiro** exige confirmación. Crea un comando con otro UUID y secuencia, vinculado mediante `datos.resuelveOperacionId`. El servidor verifica que referencia el rechazo compatible del mismo actor, visita y envío. El rechazo original y sus pruebas se conservan. Si se pierde la respuesta del comando nuevo, se reenvía ese mismo comando. |

Un error de transporte o un acuse inválido no confirma trabajo. **Actualizar** puede repetir el transporte, pero no crea una segunda declaración física. Tras un rechazo persistido tampoco se reintenta automáticamente el negocio, se reabre una ruta cerrada ni se recupera custodia por software: cuando corresponde, primero se concilia con depósito.

## Verificación y consecuencias

Esta decisión conserva trazabilidad y evita duplicar hechos ante reconexión. A cambio, una divergencia real de versión o custodia requiere conciliación operativa. Se descartan «último escritor gana», reemplazar las precondiciones por la última lectura y generar un UUID distinto para resolver una incertidumbre de transporte.

- [Política preexistente de parciales y retiros](../cambios-31-41.md#acuses-históricos-y-resolución-explícita) · [arquitectura de la sincronización](../arquitectura-mobile.md).
- [Pruebas del sincronizador](../../tests/NexoRuta.Mobile.Application.Tests/SincronizadorJornadaTests.cs): respuesta perdida y reapertura con los mismos bytes, evidencias antes del comando y conflicto durable.
- [Pruebas de acuses por bulto](../../tests/NexoRuta.Mobile.Application.Tests/ParcialAcknowledgementTests.cs): recuperación de acuse histórico, versiones propias de carga y resolución explícita con un comando nuevo.
- [Aceptación Android](../aceptacion-android/README.md): distingue la reconexión en dispositivo de una prueba controlada de respuesta perdida después del commit.
