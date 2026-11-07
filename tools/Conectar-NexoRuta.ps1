# Ejecutar despues de iniciar el emulador y el entorno local NexoRuta.
# Usa el reconector del proyecto; no reinstala la app ni borra sus datos.
[CmdletBinding()]
param(
    [string]$Serial = 'emulator-5554',
    [string]$RepoPath = (Join-Path $env:USERPROFILE 'NexoRuta'),
    [switch]$NoPause
)

$ErrorActionPreference = 'Stop'
$conexionCorrecta = $false

try {
    $reconector = Join-Path $RepoPath 'mobile\tools\local\reconectar-emulador.ps1'
    if (-not (Test-Path -LiteralPath $reconector -PathType Leaf)) {
        throw "No se encontro el reconector en: $reconector. Usa -RepoPath para indicar la carpeta NexoRuta."
    }

    Push-Location -LiteralPath $RepoPath
    try {
        Write-Host "Conectando $Serial con NexoRuta..." -ForegroundColor Cyan
        & $reconector -Serial $Serial -ProbeApp
        if (-not $?) {
            throw 'El reconector no termino correctamente. Revisa su salida anterior.'
        }
        $conexionCorrecta = $true
    }
    finally {
        Pop-Location
    }
}
catch {
    Write-Host "ERROR: $($_.Exception.Message)" -ForegroundColor Red
}
finally {
    if (-not $NoPause) {
        [void](Read-Host 'Presiona Enter para cerrar')
    }
}

if (-not $conexionCorrecta) {
    exit 1
}
