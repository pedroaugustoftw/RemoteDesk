param(
    [string]$ConfigDirectory = (Join-Path $env:LOCALAPPDATA 'RemoteDesk\infrastructure'),
    [string]$ServerAssembly,
    [string]$RelayAssembly,
    [string]$Dotnet
)
. (Join-Path $PSScriptRoot 'Lan.Common.ps1') -Dotnet $Dotnet
$ConfigDirectory = (Resolve-Path -LiteralPath $ConfigDirectory).Path
if ([string]::IsNullOrWhiteSpace($ServerAssembly)) { $ServerAssembly = Join-Path $script:ProjectRoot 'artifacts\win-x64\server\RemoteDesk.Server.dll' }
if ([string]::IsNullOrWhiteSpace($RelayAssembly)) { $RelayAssembly = Join-Path $script:ProjectRoot 'artifacts\win-x64\relay\RemoteDesk.Relay.dll' }
$ServerAssembly = (Resolve-Path -LiteralPath $ServerAssembly).Path
$RelayAssembly = (Resolve-Path -LiteralPath $RelayAssembly).Path
# No passwords or relay secrets occur in argv. They are decrypted inside the utility.
$launchArguments = @($script:UtilityDll, 'start', $ConfigDirectory, $script:DotnetPath, $ServerAssembly, $RelayAssembly) | ForEach-Object { '"' + $_ + '"' }
$supervisor = Start-Process -FilePath $script:DotnetPath -ArgumentList $launchArguments -WindowStyle Hidden -RedirectStandardOutput (Join-Path $ConfigDirectory 'supervisor.log') -RedirectStandardError (Join-Path $ConfigDirectory 'supervisor.error.log') -PassThru
$ready = $false
for ($attempt = 0; $attempt -lt 20; $attempt++) {
    Start-Sleep -Milliseconds 500
    if ($supervisor.HasExited) { throw "Infraestrutura não iniciou. Consulte $ConfigDirectory\supervisor.error.log" }
    & $script:DotnetPath $script:UtilityDll check $ConfigDirectory 2>$null
    if ($LASTEXITCODE -eq 0) { $ready = $true; break }
}
if (-not $ready) { throw 'Servidor não respondeu com certificado esperado. Use Stop-Lan.ps1 e consulte logs.' }
$config = Get-Content -LiteralPath (Join-Path $ConfigDirectory 'lan.json') -Raw | ConvertFrom-Json
Write-Output "RemoteDesk LAN em execução: $($config.ServerUri)"
Write-Output "Relay: $($config.RelayUri)"
Write-Output "SHA256 TLS: $($config.CertificateSha256)"
Write-Output "Logs: $ConfigDirectory"
