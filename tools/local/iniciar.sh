#!/usr/bin/env bash
set -euo pipefail
umask 077
root=$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/../../.." && pwd)
[[ -f $root/backend/Dockerfile ]] || { echo 'Ejecutá este entorno en el checkout combinado de NexoRuta.' >&2; exit 2; }
python3 "$root/mobile/tools/local/preparar.py"
compose=(docker compose --env-file "$root/.local/mobile/local.env" -f "$root/mobile/tools/local/compose.yaml")
"${compose[@]}" config --quiet
"${compose[@]}" up -d --wait postgres rabbitmq mailpit
"${compose[@]}" build api worker
"${compose[@]}" --profile preparacion run --rm migrate
"${compose[@]}" --profile preparacion run --rm runtime-roles
"${compose[@]}" --profile preparacion run --rm bootstrap
"${compose[@]}" up -d --wait api https worker
curl --fail --silent --show-error --retry 5 --retry-all-errors --retry-delay 1 --cacert "$root/.local/mobile/tls/ca.crt" https://localhost:5843/health/ready
printf '\nAPI HTTPS local lista. Correo de prueba: http://localhost:5855\n'
