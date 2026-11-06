[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidatePattern('^emulator-[0-9]+$')]
    [string]$Serial,
    [string]$ArtifactsDirectory = "$env:LOCALAPPDATA/NexoRuta/mobile-emulator-build"
)

$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../../..'))
$adb = Join-Path $env:LOCALAPPDATA 'Android/Sdk/platform-tools/adb.exe'
$abi = & $adb -s $Serial shell getprop ro.product.cpu.abi
if ($LASTEXITCODE -ne 0 -or $abi.Trim() -ne 'x86_64') { throw 'This Debug helper requires the selected x86_64 Android emulator.' }

# Read only the existing public Mapbox setting, not other credentials in the private environment.
$setting = @([IO.File]::ReadAllLines((Join-Path $root '.env')) | Where-Object { $_ -match '^\s*Mapbox__PublicAccessToken=' })
if ($setting.Count -ne 1) { throw 'Expected one Mapbox__PublicAccessToken in the private root .env; no token was generated.' }
$token = ($setting[0] -split '=', 2)[1].Trim().Trim('"', "'")
if ($token -notmatch '^pk\.[A-Za-z0-9._-]+$') { throw 'The existing Mapbox setting must be a public pk token.' }
$ca = Join-Path $root '.local/mobile/tls/ca.crt'
if (!(Test-Path -LiteralPath $ca -PathType Leaf)) { throw 'The existing development CA is missing.' }
$r8 = & python (Join-Path $root 'mobile/tools/preparar-r8.py')
if ($LASTEXITCODE -ne 0) { throw 'Could not prepare the verified R8 tool.' }

$previous = $env:NexoRutaMapboxPublicToken
try {
    # MSBuild imports environment properties. Do not put token values in argv, logs or tracked files.
    $env:NexoRutaMapboxPublicToken = $token
    & dotnet build (Join-Path $root 'mobile/src/NexoRuta.Mobile/NexoRuta.Mobile.csproj') -c Debug -f net10.0-android `
        -p:RuntimeIdentifier=android-x64 -p:RestoreLockedMode=true -p:AndroidPackageFormats=apk -p:EmbedAssembliesIntoApk=true `
        "-p:AndroidR8JarPath=$r8" -p:NexoRutaBackendTarget=Docker "-p:NexoRutaAndroidDevelopmentCa=$ca" `
        --artifacts-path $ArtifactsDirectory --nologo -v minimal -m:1 -nr:false
    if ($LASTEXITCODE -ne 0) { throw "Android build failed (exit $LASTEXITCODE)." }
} finally { $env:NexoRutaMapboxPublicToken = $previous }
$metadata = Join-Path $ArtifactsDirectory 'obj/NexoRuta.Mobile/debug_net10.0-android_android-x64/NexoRuta.Mobile.AssemblyInfo.cs'
if (!(Test-Path -LiteralPath $metadata)) { throw 'The exact Android target metadata was not generated.' }
# The SDK may split long attribute strings into concatenated C# literals.
$attribute = [regex]::Match([IO.File]::ReadAllText($metadata), 'AssemblyMetadata(?:Attribute)?\("NexoRutaMapboxPublicToken",(.*?)\)\]', 'Singleline')
$compiled = (@([regex]::Matches($attribute.Groups[1].Value, '"([^"]*)"') | ForEach-Object { $_.Groups[1].Value }) -join '')
if (!$attribute.Success -or $compiled -cne $token) {
    throw 'The exact Android target did not receive the configured Mapbox metadata; no installation should be performed.'
}
Write-Host "Debug android-x64 built with existing local Mapbox configuration and https://localhost:5843/. Artifacts: $ArtifactsDirectory"
