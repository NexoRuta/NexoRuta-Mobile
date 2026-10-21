# Reglas del envío descargado

Integración parcial de CU-04 para la app de reparto, conforme a D19. No declara terminado el caso de uso completo.

Al crear un envío, el servidor fija `Envio.VersionReglasId` a la publicación vigente del operador. Sin publicación, el alta devuelve `409` y no crea destinatario, bulto ni mensajes. Una publicación concurrente obliga a repetir el alta; no se elige otra versión silenciosamente. La referencia es inmutable en dominio y PostgreSQL y se conserva al reasignar.

Cada parada contiene `reglas`: identificador y número de publicación, requisitos de prueba, códigos/descripciones de motivos disponibles, máximo de intentos, intervalo y política de devolución. Dos envíos de la misma ruta pueden conservar publicaciones diferentes. El teléfono persiste esa instantánea con la hoja cifrada; el servidor vuelve a consultar la versión del envío para decidir, sin confiar en valores enviados por la app.

El selector muestra la descripción del motivo y envía su código. El servidor cuenta los intentos aplicados de ese envío entre jornadas y compara las fechas registradas por el dispositivo. Un intervalo o máximo incumplido conserva el registro como conflicto, sin inventar una visita. Si la versión exige iniciar devolución al agotar intentos, cambia el estado y suspende la parada; el bulto continúa bajo custodia del repartidor hasta rendición y recepción física.

La evidencia completa se valida automáticamente con los requisitos capturados. Se mantiene la entrega física con prueba pendiente hasta el cierre de jornada y la conciliación ya acordada para cancelaciones/reasignaciones concurrentes. Despacho no aprueba ni rechaza la entrega.

La configuración de reparto conserva únicamente zona horaria, máximo de paradas, foto del intento fallido y tratamiento de diferencias de carga. Los requisitos de entrega, motivos y límites de intentos se editan en la pantalla central de reglas operativas.

## Entorno de desarrollo

Las migraciones `BindShipmentToPublishedOperationalRules` y `RemoveDuplicateCourierRules` cambian el contrato sin compatibilidad. No atribuyen retrospectivamente una publicación a envíos anteriores: una base con envíos sin referencia exige inicializar otra base de desarrollo. El entorno local utiliza `nexoruta-mobile-local_database_cu04`; el volumen previo se conserva. `demo.py` y `verificar.py` publican reglas ficticias explícitas antes de admitir sus envíos, sin agregar valores automáticos al producto.

Una instalación anterior con hojas sin este contrato no acredita esta integración. Las pruebas nativas nuevas usan el paquete separado `uy.nexoruta.repartidor.validacion` para conservar sesión, archivos y pendientes de la app habitual. Esta variante de prueba no contiene configuración Firebase y no acredita push.

Verificado: 150 pruebas móviles, 22 de integración dirigidas y 20 de cierre de seguridad; compilación Android y recorrido ingreso/descarga/detalle en S21. La batería amplia previa obtuvo 443/447 y los cuatro casos fallidos pasaron tras corregir fixtures, firma SQL y propietario/ACL del guard. No se ejecutó otra batería amplia completa después de esas correcciones. La evidencia final de compilación, pruebas y recorrido se registra en [ejecución](ejecucion-completa.md).
