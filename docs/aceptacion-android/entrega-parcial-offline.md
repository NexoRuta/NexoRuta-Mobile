# AN-01 — Entrega parcial en modo avión y sincronización

**Objetivo:** demostrar que la captura offline sobrevive a la reapertura y que el servidor aplica una única entrega parcial por operación.

**Preparación:** usar la [ficha](README.md#ficha-de-ejecución) y una jornada asignada y despachada con un envío de dos bultos ficticios, A y B. La publicación del envío debe permitir entrega parcial y tener un motivo válido para el bulto pendiente. Preparar las pruebas exigidas por esa publicación. Usar etiquetas escaneables; registrar el estado inicial de ambos bultos en la API o Backoffice.

| Paso | Acción en Android o en el entorno de prueba | Resultado esperado |
| --- | --- | --- |
| 1 | Con conexión, iniciar con la cuenta ficticia, descargar la hoja, iniciar jornada y cargar A y B. Confirmar la salida y sincronizar la carga. | La hoja y las etiquetas coinciden con el caso. El servidor confirma la carga y la custodia del repartidor. La descarga del mapa no condiciona la salida. |
| 2 | Activar modo avión y comprobar que Wi-Fi y datos siguen apagados. Abrir la parada. Escanear A, elegir para B el motivo pendiente y capturar la evidencia que corresponda. Confirmar una sola vez. | A se muestra entregado localmente y B pendiente con el motivo; la declaración queda sin acuse. El servidor aún no registra la entrega. |
| 3 | Cerrar la app, volver a abrirla aún sin red y desbloquear localmente cuando corresponda. Volver a la parada. | Se recuperan la misma jornada, el registro pendiente, el motivo de B y la evidencia asociada. No se vuelve a capturar ni a confirmar la entrega. |
| 4 | Restaurar conexión y, si quedan pendientes, usar **Mi recorrido → Actualizar**. Verificar el resultado en el dispositivo y en el servidor. | La operación recibe acuse aplicado; A pasa a custodia de destinatario y B queda pendiente bajo custodia del repartidor. El envío refleja entrega parcial según la publicación. Las evidencias requeridas están asociadas y no se declara entregado B. |
| 5 | Actualizar otra vez y reabrir la app. Contrastar el diario y el estado remoto. | No se duplica el hecho de entrega de A ni se pierde el pendiente de B; registrar el UUID y secuencia del acuse y el estado observado de los dos bultos. |

**Alcance:** descargar el mapa es opcional; si también se va a probar el [mapa offline](../mapa-offline.md), preparar la zona antes del modo avión y registrar ese resultado por separado. Repetir **Actualizar** verifica que una entrega ya aplicada no se duplica al resincronizar. Este recorrido no induce una pérdida de respuesta posterior al commit. Ese caso, así como versión obsoleta, rechazo persistido y resolución explícita, se registra aparte si se ejecuta una prueba controlada. Ver el [ADR de conflictos offline](../adr/conflictos-offline.md).

**Resultado:** no ejecutado. APK y dispositivo pendientes de identificar en la ficha.
