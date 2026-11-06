# Entregas parciales y retiros

La parada descarga `EsRetiro`, resultado y versión de cada bulto y política parcial publicada. El repartidor escanea cada bulto que entrega/retira y elige un motivo por pendiente. Los retiros requieren foto del estado y firma de quien entrega.

`EntregaBultos`, `RetirarBultos` y `RetiroFallido` usan el diario existente. Los resultados viajan en `datos.resultadosBultos`: `{bultoId, version, entregado, motivo}`. Foto y firma siguen siendo referencias a archivos cifrados vinculados transaccionalmente en SQLite. El sincronizador sube archivos antes del comando, congela el sobre y solo avanza versiones de bulto mediante acuses de comandos propios. Los conflictos consumen secuencia y conservan el hecho, sin aplicar efectos duplicados.

La evaluación de carga excluye retiros y bultos finalizados. Una parada de entrega parcial puede salir con una parte cargada cuando su publicación lo permite. Los bultos retirados se agregan a la rendición; cerrar la jornada no los recibe físicamente.

La guía reproducible y el ADR se encuentran en `docs/cambios-requerimientos` del repositorio principal. El cliente de consola de `tests/Cambios.DemoMobile` ejecuta las bibliotecas reales con SQLite y API real en Cloud; no equivale a ejecutar MAUI/Android.

La corrección H1–H6 proyecta el diario local al habilitar captura de prueba, consume `enEstaRuta` para no completar evidencia desde otra visita y conserva separados pendiente/aplicado/conflicto. Solo un retiro aplicado entra en rendición. **Reintentar retiro** exige confirmación y crea un comando con `datos.resuelveOperacionId`; el transporte conserva ese sobre ante pérdida de respuesta y nunca elimina el rechazo anterior. Pruebas: `EntregaBultosInterfazTests.cs`. Android nativo continúa pendiente; ver el informe del superproyecto.

## Acuses históricos y resolución explícita

Un cliente anterior podía descartar `versionesBulto` al deserializar y guardar un acuse. Un inicio o una posición sin cambios de bultos admite un mapa vacío; una carga, rendición o primera declaración de entrega/retiro aplicada necesita las versiones correspondientes. Normalizar `null` en el DTO no acredita esas operaciones.

Al encontrar un acuse histórico incompleto, el sincronizador reproduce **el mismo sobre durable y UUID** por la API idempotente existente. Comprueba identidad, secuencia, resultado y versiones antes de incorporarlas en memoria. No reemplaza el sobre ni la respuesta histórica SQLite. La recuperación queda en memoria durante el proceso; después de reiniciar puede repetirse el replay, sin nuevos efectos de negocio. Si falta el sobre, falla la red o el replay sigue siendo inválido, conserva los datos y no envía la entrega siguiente como si la carga estuviera confirmada.

Los acuses propios de carga avanzan también la proyección de bultos del formulario. La hoja original permanece intacta; una lectura remota no reescribe un comando congelado. Un reintento de transporte conserva exactamente sus bytes.

Escanear selecciona un bulto para entregar y quita su motivo pendiente. Elegir un motivo hace lo contrario: ese bulto queda pendiente y ya no se declara entregado. La etiqueta y el resumen reflejan esa exclusión.

Una entrega con acuse `Conflicto` se muestra como **Entrega rechazada · requiere resolución**, nunca como entrega aceptada ni prueba pendiente. Cuando la visita y la custodia siguen siendo admisibles, el usuario revisa bultos/motivos y pulsa **Reintentar entrega**. La confirmación explícita crea otro UUID y secuencia, vinculados mediante `resuelveOperacionId`, conservando el rechazo y sus pruebas. El mismo control del servidor usado por retiros verifica actor, envío, visita y rechazo original. No reabre rutas finalizadas ni convierte una rendición en custodia disponible para entregar; primero corresponde el circuito operativo del depósito.

Regresiones portables:

```sh
dotnet test tests/NexoRuta.Mobile.Application.Tests -c Release --filter FullyQualifiedName~SincronizadorJornadaTests
dotnet test tests/NexoRuta.Mobile.Api.Tests -c Release --filter FullyQualifiedName~ConfirmacionOperacionTests
```

`ParcialAcknowledgementTests.cs` reproduce el DTO histórico truncado, reapertura SQLite, versión de carga en el formulario/sobre nuevo, motivos después de escanear y resolución explícita sin cambiar el rechazo. Estas pruebas no sustituyen cámara, firma, conectividad ni aceptación Android del recorrido real.

Si un acuse es inválido, la pantalla explica que no pudo actualizar, conserva los datos y ofrece reintentar con **Actualizar**. Los IDs y versiones detallados permanecen en el diagnóstico Android `NexoRuta.Sync`, no en el mensaje del repartidor. La prueba `InvalidReceiptShowsRetryGuidanceWithoutExposingDiagnosticsOrConfirmingWork` cubre actualización y sincronización directa sin confirmar ni perder operaciones.
