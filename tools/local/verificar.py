#!/usr/bin/env python3
"""Prueba HTTPS/JWT/outbox/RabbitMQ/SMTP/Tracking con destinatario ficticio de Mailpit."""
import json
from pathlib import Path
import re
import ssl
import time
import urllib.error
import urllib.parse
import urllib.request
import uuid
from reglas import publicar_reglas

root = Path(__file__).resolve().parents[3]
local = root / '.local/mobile'
config = json.loads((local / 'bootstrap.json').read_text())['AccesoInicial']
tls = ssl.create_default_context(cafile=str(local / 'tls/ca.crt'))
api = 'https://localhost:5843'
def pedir(path, method='GET', body=None, token=None):
    headers = {'Content-Type': 'application/json'}
    if token: headers['Authorization'] = 'Bearer ' + token
    request = urllib.request.Request(api + path, data=None if body is None else json.dumps(body).encode(), headers=headers, method=method)
    with urllib.request.urlopen(request, context=tls, timeout=15) as response:
        content = response.read()
        return response.status, json.loads(content) if content and response.headers.get_content_type() == 'application/json' else None

status, _ = pedir('/health/ready')
assert status == 200
status, session = pedir('/api/auth/login', 'POST', {'email': config['UsuarioEmail'], 'password': config['UsuarioPassword']})
assert status == 200
token = session['accessToken']
_, operator_session = pedir('/api/auth/login', 'POST', {'email': config['OperadorUsuarioEmail'], 'password': config['OperadorUsuarioPassword']})
def reglas_api(path, method='GET', body=None, token=None, statuses=(200,)):
    status, result = pedir(path, method, body, token)
    assert status in statuses
    return result
publicar_reglas(reglas_api, operator_session['accessToken'])
uid = uuid.uuid4().hex
recipient = f'destinatario-{uid}@mobile.nexoruta.test'
status, created = pedir('/api/envios', 'POST', {
    'operadorId': config['OperadorId'], 'destinatarioNombre': 'Destinatario ficticio local', 'destinatarioEmail': recipient,
    'direccion': 'Dirección ficticia de prueba local', 'codigoBulto': 'LOCAL-' + uid,
    'pesoGramos': 1000, 'largoCentimetros': 10, 'anchoCentimetros': 10, 'altoCentimetros': 10,
}, token)
assert status == 201
shipment = created['id']
_, destination = pedir(f'/api/envios/{shipment}/destino', token=token)
assert destination['latitud'] is None and destination['confirmadaEnUtc'] is None
status, _ = pedir(f'/api/envios/{shipment}/destino', 'PUT', {'version': destination['version'], 'latitud': -34.9011, 'longitud': -56.1645}, token)
assert status == 204
tracking = None
for _ in range(40):
    status, result = pedir(f'/api/envios/{shipment}/seguimiento', token=token)
    if status == 200:
        tracking = urllib.parse.urlparse(result['url'])
        assert tracking.scheme == 'https' and tracking.netloc == 'localhost:5843'
        break
    assert status == 202
    time.sleep(0.5)
assert tracking is not None, 'El Worker no completó el seguimiento.'
status, _ = pedir(tracking.path + '/cancelacion/codigo', 'POST')
assert status == 202
code = None
for _ in range(40):
    with urllib.request.urlopen('http://127.0.0.1:5855/api/v1/messages', timeout=5) as response:
        messages = json.load(response)['messages']
    message = next((m for m in messages if any(x['Address'] == recipient for x in m['To'])), None)
    if message:
        with urllib.request.urlopen('http://127.0.0.1:5855/api/v1/message/' + message['ID'], timeout=5) as response:
            content = json.load(response)
        match = re.search(r'\b[A-HJ-KM-NP-Z2-9]{4}-[A-HJ-KM-NP-Z2-9]{4}-[A-HJ-KM-NP-Z2-9]{4}\b', content['Text'])
        assert match, 'El correo no contiene el código esperado.'
        code = match.group()
        break
    time.sleep(0.5)
assert code, 'No llegó el correo al SMTP local.'
status, result = pedir(tracking.path + '/cancelacion', 'POST', {'codigo': code})
assert status == 200 and result['estado'] == 'Cancelado'
status, repeated = pedir(tracking.path + '/cancelacion', 'POST', {'codigo': code})
assert status == 200 and repeated == result
print('Correcto: HTTPS validado, JWT de comercio, alta, destino, outbox/RabbitMQ, SMTP local y cancelación pública idempotente.')
