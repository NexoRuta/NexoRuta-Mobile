[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidatePattern('^emulator-[0-9]+$')]
    [string]$Serial,
    [string]$Adb = "$env:LOCALAPPDATA\Android\Sdk\platform-tools\adb.exe",
    [string]$TlsDirectory = '',
    [switch]$ProbeApp
)

$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../../..'))
if (!$TlsDirectory) { $TlsDirectory = Join-Path $root '.local/mobile/tls' }
$TlsDirectory = (Resolve-Path -LiteralPath $TlsDirectory).Path
$state = Join-Path $env:LOCALAPPDATA 'NexoRuta/mobile-emulator'
$container = 'nexoruta-mobile-gateway'
$network = 'nexoruta_default' # Existing usual Compose network provides the template's Docker DNS resolver.
$image = 'nginx:1.28-alpine' # Same gateway as tools/local/compose.yaml; no API or database here.
$owner = 'uy.nexoruta.local.gateway'

function Invoke-Checked([string]$Executable, [string[]]$Arguments) {
    & $Executable @Arguments
    if ($LASTEXITCODE -ne 0) { throw "$Executable failed (exit $LASTEXITCODE)." }
}

foreach ($file in 'ca.crt', 'server.crt', 'server.key') {
    if (!(Test-Path -LiteralPath (Join-Path $TlsDirectory $file) -PathType Leaf)) { throw "Missing local TLS file: $file" }
}
if (!(Test-Path -LiteralPath $Adb -PathType Leaf)) { throw "adb not found: $Adb" }
if ((Invoke-Checked $Adb @('-s', $Serial, 'get-state')) -ne 'device') { throw "Emulator $Serial is not ready." }
if ((Invoke-Checked $Adb @('-s', $Serial, 'shell', 'getprop', 'sys.boot_completed')).Trim() -ne '1') { throw 'Wait for Android to finish booting.' }
Invoke-Checked 'docker' @('info', '--format', '{{.ServerVersion}}') | Out-Null
Invoke-Checked 'docker' @('network', 'inspect', $network, '--format', '{{.Driver}}') | Out-Null
if ((Invoke-WebRequest 'http://localhost:5000/ready' -UseBasicParsing -TimeoutSec 10).StatusCode -ne 200) {
    throw 'The usual API localhost:5000 is not ready. This script never starts another API.'
}

$config = Join-Path $state 'nginx.conf'
$content = [IO.File]::ReadAllText((Join-Path $PSScriptRoot 'nginx.conf')).Replace('api:8080', 'host.docker.internal:5000')
$found = Invoke-Checked 'docker' @('container', 'ls', '--all', '--filter', ('name=^/{0}$' -f $container), '--format', '{{.Names}}')
if ($found -eq $container) {
    $existing = Invoke-Checked 'docker' @('container', 'inspect', $container)
    $details = ($existing | ConvertFrom-Json)[0]
    if ($details.Config.Labels.$owner -ne 'usual-api-5000' -or $details.Config.Image -ne $image) {
        throw "Container $container is not the expected local gateway; it was left unchanged."
    }
    foreach ($mount in @(@($config, '/etc/nginx/nginx.conf'), @((Join-Path $TlsDirectory 'server.crt'), '/certs/server.crt'), @((Join-Path $TlsDirectory 'server.key'), '/certs/server.key'))) {
        $actual = @($details.Mounts | Where-Object { $_.Destination -eq $mount[1] })
        if ($actual.Count -ne 1 -or $actual[0].RW -or [IO.Path]::GetFullPath($actual[0].Source) -ine [IO.Path]::GetFullPath($mount[0])) {
            throw "Gateway mount differs: $($mount[1]). Existing container left unchanged."
        }
    }
    $binding = @($details.HostConfig.PortBindings.'443/tcp')
    if ($binding.Count -ne 1 -or $binding[0].HostIp -ne '127.0.0.1' -or $binding[0].HostPort -ne '5843') {
        throw 'Existing gateway is not bound only to 127.0.0.1:5843; left unchanged.'
    }
}
# Ownership and mounts must be accepted before changing any file a container might already mount.
New-Item -ItemType Directory -Force -Path $state | Out-Null
if (!(Test-Path -LiteralPath $config) -or [IO.File]::ReadAllText($config) -ne $content) {
    [IO.File]::WriteAllText($config, $content, (New-Object Text.UTF8Encoding($false)))
}
if ($found -eq $container) {
    if (!$details.State.Running) { Invoke-Checked 'docker' @('start', $container) | Out-Null }
    if ($details.NetworkSettings.Networks.PSObject.Properties.Name -notcontains $network) {
        Invoke-Checked 'docker' @('network', 'connect', $network, $container)
    }
    Invoke-Checked 'docker' @('exec', $container, 'nginx', '-t')
    Invoke-Checked 'docker' @('exec', $container, 'nginx', '-s', 'reload')
} else {
    Invoke-Checked 'docker' @('run', '--detach', '--name', $container, '--restart', 'unless-stopped',
        '--network', $network,
        '--label', "$owner=usual-api-5000", '--publish', '127.0.0.1:5843:443',
        '--mount', "type=bind,source=$config,target=/etc/nginx/nginx.conf,readonly",
        '--mount', "type=bind,source=$TlsDirectory/server.crt,target=/certs/server.crt,readonly",
        '--mount', "type=bind,source=$TlsDirectory/server.key,target=/certs/server.key,readonly", $image) | Out-Null
}

# Same verified SSL context as the existing Python local tools. Windows Schannel expects
# revocation services that this private development CA does not publish; do not change host trust.
$httpsCheck = @'
import ssl, sys, time, urllib.request
context = ssl.create_default_context(cafile=sys.argv[1])
for attempt in range(15):
    try:
        with urllib.request.urlopen('https://localhost:5843/ready', context=context, timeout=5) as response:
            if response.status != 200:
                raise OSError(f'HTTP {response.status}')
        break
    except OSError as error:
        if attempt == 14:
            raise SystemExit(f'Trusted HTTPS readiness failed: {error}')
        time.sleep(1)
'@
Invoke-Checked 'python' @('-c', $httpsCheck, (Join-Path $TlsDirectory 'ca.crt'))
Invoke-Checked $Adb @('-s', $Serial, 'reverse', 'tcp:5843', 'tcp:5843') | Out-Null
Invoke-Checked $Adb @('-s', $Serial, 'reverse', '--list')

if ($ProbeApp) {
    $probe = [Guid]::NewGuid().ToString('D')
    Invoke-Checked $Adb @('-s', $Serial, 'shell', 'am', 'start', '-n', 'uy.nexoruta.repartidor/crc64cdd12cc4b73c80b7.MainActivity', '--es', 'nexoruta.dev.connection', $probe) | Out-Null
    $result = $null
    for ($attempt = 0; $attempt -lt 25; $attempt++) {
        $result = @(Invoke-Checked $Adb @('-s', $Serial, 'logcat', '-d', '-s', 'NexoRuta.Connection:I', '*:S') | Where-Object { $_ -match "probe=$probe;" })
        if ($result.Count) { break }
        Start-Sleep -Seconds 1
    }
    $result | Tee-Object -FilePath (Join-Path $state "probe-$probe.log")
    if (!($result -match ';origin=https://localhost:5843/;status=200')) { throw 'The Debug app did not confirm trusted HTTPS200. See NexoRuta.Connection logcat.' }
}
Write-Host "Ready: https://localhost:5843 -> usual API localhost:5000; $Serial. Gateway logs: docker logs $container. Probe logs: $state"
