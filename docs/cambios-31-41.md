# Entregas parciales y retiros

La parada descarga `EsRetiro`, resultado y versión de cada bulto y política parcial publicada. El repartidor escanea cada bulto que entrega/retira y elige un motivo por pendiente. Los retiros requieren foto del estado y firma de quien entrega.

`EntregaBultos`, `RetirarBultos` y `RetiroFallido` usan el diario existente. Los resultados viajan en `datos.resultadosBultos`: `{bultoId, version, entregado, motivo}`. Foto y firma siguen siendo referencias a archivos cifrados vinculados transaccionalmente en SQLite. El sincronizador sube archivos antes del comando, congela el sobre y solo avanza versiones de bulto mediante acuses de comandos propios. Los conflictos consumen secuencia y conservan el hecho, sin aplicar efectos duplicados.

La evaluación de carga excluye retiros y bultos finalizados. Una parada de entrega parcial puede salir con una parte cargada cuando su publicación lo permite. Los bultos retirados se agregan a la rendición; cerrar la jornada no los recibe físicamente.

La guía reproducible y el ADR se encuentran en `docs/cambios-requerimientos` del repositorio principal. El cliente de consola de `tests/Cambios.DemoMobile` ejecuta las bibliotecas reales con SQLite y API real en Cloud; no equivale a ejecutar MAUI/Android.

La corrección H1–H6 proyecta el diario local al habilitar captura de prueba, consume `enEstaRuta` para no completar evidencia desde otra visita y conserva separados pendiente/aplicado/conflicto. Solo un retiro aplicado entra en rendición. **Reintentar retiro** exige confirmación y crea un comando con `datos.resuelveOperacionId`; el transporte conserva ese sobre ante pérdida de respuesta y nunca elimina el rechazo anterior. Pruebas: `EntregaBultosInterfazTests.cs`. Android nativo continúa pendiente; ver el informe del superproyecto.
