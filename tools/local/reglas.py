"""Publicación explícita de reglas ficticias del entorno local; nunca se usa en producción."""

def publicar_reglas(pedir, token):
    ruta = '/api/configuracion/reglas-operativas'
    estado = pedir(ruta, token=token)
    if estado['publicacionVigente'] is not None:
        return
    if estado['borrador'] is None:
        pedir(ruta + '/borrador', 'POST', token=token, statuses=(201,))
    borrador = pedir(ruta + '/borrador', token=token)
    borrador = pedir(ruta + '/borrador', 'PUT', {
        'versionConcurrencia': borrador['versionConcurrencia'], 'maxIntentosEntrega': 3,
        'intervaloEntreIntentosMinutos': 1, 'politicaDevolucionAlAgotarIntentos': 'DecisionOperativa',
        'plazoEstandarMinutos': 2880, 'plazoUrgenteMinutos': 1440,
        'requisitosPrueba': ['Fotografia', 'NombreReceptor'],
        'motivos': [{'codigo': 'AUSENTE', 'descripcion': 'Ausente', 'disponible': True},
                    {'codigo': 'RECHAZADO', 'descripcion': 'Rechazado por destinatario', 'disponible': True}],
    }, token)
    pedir(ruta + '/borrador/publicar', 'POST', {'versionConcurrencia': borrador['versionConcurrencia']}, token)
