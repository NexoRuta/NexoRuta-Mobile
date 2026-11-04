param([Parameter(Mandatory=$true)][string]$HelperPath)
$ErrorActionPreference = 'Stop'
$HelperPath = (Resolve-Path -LiteralPath $HelperPath).Path
$scratch = Join-Path $env:TEMP ('nexoruta-gateway-refusal-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $scratch | Out-Null
$originalLocalAppData = $env:LOCALAPPDATA
$calls = New-Object 'System.Collections.Generic.List[string]'
function docker {
    param([Parameter(ValueFromRemainingArguments=$true)][string[]]$Command)
    $global:LASTEXITCODE = 0
    $key = $Command -join ' '; $calls.Add($key)
    if ($key -eq 'info --format {{.ServerVersion}}') { return '28.0.4' }
    if ($key -eq 'network inspect nexoruta_default --format {{.Driver}}') { return 'bridge' }
    if ($key.StartsWith('container ls ')) { return 'nexoruta-mobile-gateway' }
    if ($key -eq 'container inspect nexoruta-mobile-gateway') {
        return '[{"Config":{"Labels":{},"Image":"nginx:1.28-alpine"}}]'
    }
    throw "Unexpected Docker operation in read-only refusal check: $key"
}
function Invoke-WebRequest { param($Uri, [switch]$UseBasicParsing, $TimeoutSec) return [pscustomobject]@{StatusCode=200} }
try {
    $env:LOCALAPPDATA = Join-Path $scratch 'local-app-data'
    $state = Join-Path $env:LOCALAPPDATA 'NexoRuta/mobile-emulator'
    New-Item -ItemType Directory -Force -Path $state | Out-Null
    $config = Join-Path $state 'nginx.conf'
    [IO.File]::WriteAllText($config, 'Foreign configuration must remain byte-identical.')
    $before = (Get-FileHash -LiteralPath $config -Algorithm SHA256).Hash
    $tls = Join-Path $scratch 'tls'; New-Item -ItemType Directory -Path $tls | Out-Null
    foreach ($name in 'ca.crt','server.crt','server.key') { [IO.File]::WriteAllText((Join-Path $tls $name), 'Test placeholder; must never reach TLS or Docker.') }
    $adb = Join-Path $scratch 'mock-adb.ps1'
    @'
param([Parameter(ValueFromRemainingArguments=$true)][string[]]$Command)
$global:LASTEXITCODE=0
switch ($Command -join ' ') {
    '-s emulator-5554 get-state' { 'device' }
    '-s emulator-5554 shell getprop sys.boot_completed' { '1' }
    default { throw 'Unexpected adb command; no emulator is accessed by this test.' }
}
'@ | Set-Content -LiteralPath $adb
    $refused = $false
    try { & $HelperPath -Serial emulator-5554 -Adb $adb -TlsDirectory $tls }
    catch {
        if ($_.Exception.Message -notmatch 'not the expected local gateway') { throw }
        $refused = $true
    }
    if (!$refused) { throw 'Foreign gateway was not refused.' }
    $after = (Get-FileHash -LiteralPath $config -Algorithm SHA256).Hash
    if ($before -ne $after) { throw "BLOCKER: refusal modified configuration. Scratch retained: $scratch" }
    [pscustomobject]@{passed=$true;refused=$true;configHashUnchanged=$true;dockerReadOperations=$calls.Count;scratch=$scratch} | ConvertTo-Json
} finally { $env:LOCALAPPDATA = $originalLocalAppData }
