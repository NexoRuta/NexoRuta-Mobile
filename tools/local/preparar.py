#!/usr/bin/env python3
"""Prepara secretos y TLS locales; conserva lo ya creado y no imprime credenciales."""
import base64
import json
import os
from pathlib import Path
import secrets
import subprocess
import uuid

os.umask(0o077)
root = Path(__file__).resolve().parents[3]
local = root / '.local' / 'mobile'
local.mkdir(parents=True, exist_ok=True)
config = local / 'bootstrap.json'
env = local / 'local.env'
if not env.exists():
    if config.exists():
        raise SystemExit('Existe bootstrap.json sin local.env; recuperá la configuración previa antes de continuar.')
    op, commerce = str(uuid.uuid4()), str(uuid.uuid4())
    roles = [
        {'Codigo': 'administrador_operador', 'Nombre': 'Administrador del operador', 'OperadorId': op,
         'Permisos': ['usuarios.gestionar', 'configuracion.gestionar', 'envios.consultar', 'envios.gestionar', 'reparto.ejecutar']},
        {'Codigo': 'usuario_operador', 'Nombre': 'Despachador', 'OperadorId': op,
         'Permisos': ['envios.consultar', 'envios.gestionar']},
        {'Codigo': 'propietario_comercio', 'Nombre': 'Propietario del comercio', 'ComercioId': commerce,
         'Permisos': ['usuarios.gestionar', 'envios.crear', 'envios.consultar']},
        {'Codigo': 'usuario_comercio', 'Nombre': 'Usuario del comercio', 'ComercioId': commerce,
         'Permisos': ['envios.crear', 'envios.consultar']},
    ]
    account = {
        'OperadorId': op, 'ComercioId': commerce, 'OperadorNombre': 'Operador local mobile', 'ComercioNombre': 'Comercio local mobile',
        'OperadorUsuarioEmail': 'operador@mobile.nexoruta.test', 'UsuarioEmail': 'comercio@mobile.nexoruta.test',
        'OperadorUsuarioPassword': secrets.token_urlsafe(24), 'UsuarioPassword': secrets.token_urlsafe(24),
        'OperadorRolCodigo': 'administrador_operador', 'ComercioRolCodigo': 'propietario_comercio',
    }
    config.write_text(json.dumps({'AccesoInicial': account, 'Bootstrap': {'Roles': roles},
        'PlatformInitialization': {'Enabled': True, 'Email': 'plataforma@mobile.nexoruta.test', 'Password': secrets.token_urlsafe(24)}}, indent=2) + '\n')
    values = {key: secrets.token_hex(24) for key in ['POSTGRES_PASSWORD', 'API_DB_PASSWORD', 'WORKER_DB_PASSWORD', 'BOOTSTRAP_DB_PASSWORD', 'RABBIT_PASSWORD']}
    values.update({key: base64.b64encode(secrets.token_bytes(32)).decode() for key in ['JWT_KEY', 'TRACKING_KEY', 'PUSH_KEY', 'EVIDENCE_KEY']})
    values['NEXORUTA_LOCAL_DIR'] = str(local)
    values['NEXORUTA_GOOGLE_ADC_FILE'] = str(Path.home() / '.config/gcloud/application_default_credentials.json')
    # Claves de proveedores opcionales; se completan localmente, nunca en el APK ni en Git.
    values['GEOAPIFY_API_KEY'] = ''
    values['MAPBOX_PUBLIC_TOKEN'] = ''
    env.write_text(''.join(f'{key}={value}\n' for key, value in values.items()))
if 'EVIDENCE_KEY=' not in env.read_text():
    with env.open('a') as output:
        output.write('EVIDENCE_KEY=' + base64.b64encode(secrets.token_bytes(32)).decode() + '\n')
if not config.exists():
    raise SystemExit('Falta bootstrap.json; no se regenerarán las identidades sobre un entorno existente.')
tls = local / 'tls'
tls.mkdir(exist_ok=True)
def openssl(*args):
    subprocess.run(['openssl', *map(str, args)], check=True, stdout=subprocess.DEVNULL, stderr=subprocess.PIPE)
if not (tls / 'ca.key').exists():
    if (tls / 'ca.crt').exists():
        raise SystemExit('Falta la clave de la CA existente. Recuperala antes de regenerar certificados.')
    openssl('req', '-x509', '-newkey', 'rsa:3072', '-nodes', '-keyout', tls / 'ca.key', '-out', tls / 'ca.crt',
            '-sha256', '-days', '3650', '-subj', '/CN=NexoRuta Local Mobile CA', '-addext', 'basicConstraints=critical,CA:TRUE',
            '-addext', 'keyUsage=critical,keyCertSign,cRLSign')
if not (tls / 'server.crt').exists():
    openssl('req', '-newkey', 'rsa:3072', '-nodes', '-keyout', tls / 'server.key', '-out', tls / 'server.csr', '-subj', '/CN=localhost')
    extensions = tls / 'server.ext'
    extensions.write_text('subjectAltName=DNS:localhost,IP:127.0.0.1\nbasicConstraints=critical,CA:FALSE\nkeyUsage=critical,digitalSignature,keyEncipherment\nextendedKeyUsage=serverAuth\n')
    openssl('x509', '-req', '-in', tls / 'server.csr', '-CA', tls / 'ca.crt', '-CAkey', tls / 'ca.key', '-CAcreateserial',
            '-out', tls / 'server.crt', '-days', '365', '-sha256', '-extfile', extensions)
print('Configuración local preparada en .local/mobile. Las credenciales no se imprimen.')
