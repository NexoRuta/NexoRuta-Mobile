# Registro de ubicación durante la jornada

`RegistroUbicacion` pertenece a Application y comparte el mismo código entre Android e iOS. Los adaptadores nativos solo solicitan permisos, reciben mediciones y activan/detienen el servicio. La jornada, sus puntos y sus confirmaciones permanecen en SQLite cifrado por cuenta.

El registro exige una única jornada abierta cuyo inicio esté confirmado como `Aplicada` por el servidor. No requiere tokens vigentes para continuar una jornada ya descargada/iniciada sin red. Sin cuenta, con otra cuenta, sin inicio autorizado, con dos jornadas ambiguas o después del cierre, devuelve una orden de detener el seguimiento.

Las mediciones inválidas no generan coordenadas ficticias. Una posición anterior al inicio de la jornada se descarta; se conserva el instante original de las aceptadas. Los callbacks GPS/red duplicados, desordenados o separados por menos de treinta segundos no agregan otro punto. El control se reconstruye desde el diario al reiniciar, sin depender de un temporizador de la pantalla. Un cierre concurrente se comprueba de nuevo dentro de la modificación transaccional.

La sincronización solo se dispara cuando se agregó una medición, después de persistirla. Un fallo HTTP o de sesión conserva el punto para el siguiente disparador; cerrar la jornada no depende de poder transmitir inmediatamente. No se imprimen coordenadas en los registros de diagnóstico.

## Verificación

El 21/10/2026 pasaron diez casos de Application: inicio aún no confirmado/rechazado/en conflicto, continuidad sin tokens, reapertura con muestreo durable, posición anterior al inicio, cierre y callback inválido posterior, aislamiento de cuenta, callbacks concurrentes, coordenadas inválidas y ambigüedad de jornadas. La batería completa de Application pasó 52 casos; Core, 34.

La variante S21 inició una jornada ficticia mediante la UI contra HTTPS local, obtuvo permisos y activó el servicio Android. La consulta final del diario acreditó diez puntos `Aplicada`, con intervalos entre 30,00 y 30,33 segundos, sin imprimir ubicaciones. El servicio se comprobó en segundo plano con `isForeground=true`. Se interrumpió HTTPS local durante cuarenta segundos y se restauró saludable. La primera consulta aún mostraba cuatro puntos; al completar la sincronización llegaron los diez, incluidos dos diferidos más de treinta segundos y una demora máxima de 60,76 segundos. El diario conserva la hora de medición original y una sola operación por punto. Este ensayo acredita API inaccesible y recuperación, sin haber desactivado la Wi-Fi del teléfono. El cierre por UI terminó sin pendientes y detuvo el servicio. La medición extendida con pantalla apagada, huecos, batería y precisión sigue pendiente; una muestra corta no acredita una frecuencia sostenida ni una jornada completa. iOS no fue compilado ni probado.
