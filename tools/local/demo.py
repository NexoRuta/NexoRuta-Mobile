#!/usr/bin/env python3
"""Crea una hoja ficticia por API, con cuenta de repartidor sin permisos administrativos."""
import datetime
import json
import os
from pathlib import Path
import secrets
import ssl
import urllib.error
import urllib.request
from zoneinfo import ZoneInfo
from reglas import publicar_reglas

os.umask(0o077)
local = Path(__file__).resolve().parents[3] / '.local/mobile'
config = json.loads((local / 'bootstrap.json').read_text())['AccesoInicial']
tls = ssl.create_default_context(cafile=str(local / 'tls/ca.crt'))
state_file = local / 'demo.json'
if not state_file.exists():
    state_file.write_text(json.dumps({'email': 'repartidor@mobile.nexoruta.test', 'password': secrets.token_urlsafe(24)}, indent=2))
state = json.loads(state_file.read_text())

def save():
    state_file.write_text(json.dumps(state, indent=2) + '\n')

def api(path, method='GET', body=None, token=None, statuses=(200,)):
    headers = {'Content-Type': 'application/json'}
    if token:
        headers['Authorization'] = 'Bearer ' + token
    request = urllib.request.Request('https://localhost:5843' + path,
        data=None if body is None else json.dumps(body).encode(), headers=headers, method=method)
    try:
        response = urllib.request.urlopen(request, context=tls, timeout=20)
    except urllib.error.HTTPError as error:
        response = error
    with response:
        assert response.status in statuses, f'{method} {path}: HTTP {response.status}'
        data = response.read()
        return json.loads(data) if data and response.headers.get_content_type() == 'application/json' else None

operator = api('/api/auth/login', 'POST', {'email': config['OperadorUsuarioEmail'], 'password': config['OperadorUsuarioPassword']})['accessToken']
merchant = api('/api/auth/login', 'POST', {'email': config['UsuarioEmail'], 'password': config['UsuarioPassword']})['accessToken']
publicar_reglas(api, operator)
roles = api('/api/roles', token=operator)
role = next((r for r in roles if r['codigo'] == 'repartidor_demo'), None)
if role is None:
    role = api('/api/roles', 'POST', {'codigo': 'repartidor_demo', 'nombre': 'Repartidor de prueba local'}, operator, (201,))
    role['permisos'] = []
if role['permisos'] != ['reparto.ejecutar']:
    api(f'/api/roles/{role["id"]}/permisos', 'PUT', {'permisos': ['reparto.ejecutar']}, operator, (204,))
user = next((u for u in api('/api/usuarios', token=operator) if u['email'] == state['email']), None)
if user is None:
    user = api('/api/usuarios', 'POST', {'email': state['email'], 'password': state['password'], 'rolId': role['id']}, operator, (201,))
state['accesoId'] = user['accesoId']
profiles = api('/api/repartidores', token=operator)
if not any(p['accesoId'] == user['accesoId'] for p in profiles):
    api(f'/api/repartidores/{user["accesoId"]}', 'PUT', {'nombre': 'Repartidor local S21', 'activo': True}, operator)
policy = api('/api/reparto/configuracion', token=operator, statuses=(200, 404))
if policy is None:
    api('/api/reparto/configuracion', 'PUT', {'version': 0, 'zonaHoraria': 'America/Montevideo', 'maximoParadas': 30,
        'bloquearDiscrepanciasCarga': True}, operator)
vehicles = api('/api/reparto/vehiculos', token=operator)
vehicle = next((v for v in vehicles if v['matricula'] == 'LOCAL-S21'), None)
if vehicle is None:
    vehicle = api('/api/reparto/vehiculos', 'PUT', {'matricula': 'LOCAL-S21', 'pesoMaximoGramos': 100000,
        'volumenMaximoCm3': 1000000, 'activo': True}, operator)
zone = ZoneInfo('America/Montevideo')
today = datetime.datetime.now(zone).date()
routes = api(f'/api/rutas?fecha={today}', token=operator)
route = next((r for r in routes if r['repartidorAccesoId'] == user['accesoId']), None)
if route is None:
    code = f'LOCAL-S21-{today}'
    shipments = api('/api/envios', token=merchant)
    shipment = next((e for e in shipments if any(b['codigo'] == code for b in e['bultos'])), None)
    if shipment is None:
        shipment = api('/api/envios', 'POST', {'operadorId': config['OperadorId'], 'destinatarioNombre': 'Destino ficticio S21',
            'destinatarioEmail': 'destinatario-s21@mobile.nexoruta.test', 'direccion': 'Destino de prueba local, sin entrega real',
            'codigoBulto': code, 'pesoGramos': 1000, 'largoCentimetros': 10, 'anchoCentimetros': 10, 'altoCentimetros': 10}, merchant, (201,))
        shipment = next(e for e in api('/api/envios', token=merchant) if e['id'] == shipment['id'])
    destination = api(f'/api/envios/{shipment["id"]}/destino', token=merchant)
    if destination['confirmadaEnUtc'] is None:
        api(f'/api/envios/{shipment["id"]}/destino', 'PUT', {'version': destination['version'], 'latitud': -34.9011, 'longitud': -56.1645}, merchant, (204,))
    if shipment['estado'] == 'Admitido':
        api(f'/api/deposito/envios/{shipment["id"]}/recepcion', 'POST', {'version': shipment['version']}, operator, (204,))
    arrival = datetime.datetime.combine(today, datetime.time(15), zone).isoformat()
    route = api('/api/rutas', 'POST', {'repartidorAccesoId': user['accesoId'], 'vehiculoId': vehicle['id'], 'fecha': str(today),
        'paradas': [{'envioId': shipment['id'], 'llegadaEstimadaUtc': arrival}]}, operator)
if route['estado'] == 'Planificada':
    route = api(f'/api/rutas/{route["id"]}/despacho', 'POST', {'version': route['version']}, operator)
state['rutaId'] = route['id']
state['fecha'] = str(today)
save()
session = api('/api/auth/mobile/login', 'POST', {'email': state['email'], 'password': state['password']})
assert api('/api/mobile/perfil', token=session['accessToken'])['accesoId'] == user['accesoId']
own = api(f'/api/mobile/rutas?fecha={today}', token=session['accessToken'])
assert any(r['id'] == route['id'] for r in own)
print('Hoja ficticia preparada y acceso móvil verificado. Credenciales únicamente en .local/mobile/demo.json.')
