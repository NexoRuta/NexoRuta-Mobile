# AN-02 — Ruta mixta, foto, firma y recepción del retiro

**Objetivo:** demostrar que Android registra un retiro completo con sus pruebas, y que declarar la rendición, cerrar la jornada y recibir físicamente el retiro son hechos distintos.

**Preparación:** usar la [ficha](README.md#ficha-de-ejecución). Despachar para el mismo repartidor una ruta con una entrega de prueba y un retiro de prueba, con sus bultos etiquetados. Usar una configuración publicada que admita el retiro y una capacidad suficiente antes y después de recogerlo. Preparar un operador de depósito distinto del repartidor y usar solo fotos y firmas de prueba.

| Paso | Acción en Android o en depósito | Resultado esperado |
| --- | --- | --- |
| 1 | Descargar la ruta con conexión, iniciar jornada, cargar los bultos de entrega y confirmar salida. Descargar el mapa solo si se quiere comprobar su uso sin conexión. | Se distinguen entrega y retiro. La carga inicial contiene los bultos de entrega; el retiro aún no está bajo custodia del repartidor. |
| 2 | En la parada de entrega, escanear y completar el flujo y las pruebas de su publicación. Sincronizar. | El servidor registra la entrega según su política; el bulto entregado deja la carga. La parada de retiro sigue disponible. |
| 3 | En el retiro, escanear todos los bultos de la solicitud. Verificar que no se pueda confirmar sin los requisitos; capturar una foto del estado y la firma de quien entrega, y confirmar una sola vez. | Se registra un retiro completo. Las pruebas se asocian al comando; una captura incompleta no equivale a un retiro aplicado. |
| 4 | Actualizar el recorrido y comparar con el servidor. | El acuse aplicado lleva el retiro a `Retirado`, con custodia del repartidor. Verificar que ambas pruebas quedaron asociadas y que el retiro aplicado aparece para rendir. |
| 5 | Declarar la rendición de los bultos retirados y sincronizar. Si se cierra la jornada en este punto, verificar también el cierre. Antes del acto de recepción, consultar la custodia. | Se registra la declaración pendiente de depósito. Ni la rendición ni el cierre por sí solos pasan el retiro a `RetiroEnDeposito`. |
| 6 | Entregar los bultos de prueba al operador de depósito y confirmar la recepción en el flujo de depósito. Volver a consultar estado, custodia e historial. | La recepción física lleva el retiro a `RetiroEnDeposito` con custodia de depósito. El historial distingue retiro, rendición y recepción. La eventual entrega al comercio es posterior y queda fuera de este guion. |

**Resultado:** no ejecutado. APK y dispositivo pendientes de identificar en la ficha. Registrar por separado si quedó pendiente verificar cámara, firma, rendición o recepción; ver las [reglas de entregas parciales y retiros](../cambios-31-41.md).
