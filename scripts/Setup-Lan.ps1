param(
    [string]$Ip,
    [string]$ConfigDirectory = (Join-Path $env:LOCALAPPDATA 'RemoteDesk\infrastructure'),
    [string]$Dotnet
)
. (Join-Path $PSScriptRoot 'Lan.Common.ps1') -Dotnet $Dotnet
if ([string]::IsNullOrWhiteSpace($Ip)) {
    $route = Get-NetRoute -DestinationPrefix '0.0.0.0/0' -ErrorAction Stop | Sort-Object RouteMetric | Select-Object -First 1
    $address = Get-NetIPAddress -InterfaceIndex $route.InterfaceIndex -AddressFamily IPv4 -ErrorAction Stop | Where-Object { $_.IPAddress -notlike '169.254.*' -and $_.IPAddress -ne '127.0.0.1' } | Select-Object -First 1
    if ($null -eq $address) { throw 'Informe -Ip com o IPv4 da rede local.' }
    $Ip = $address.IPAddress
}
& $script:DotnetPath $script:UtilityDll setup $ConfigDirectory $Ip
if ($LASTEXITCODE -ne 0) { throw 'Falha na criação da configuração LAN.' }
Write-Output 'Não foram alterados firewall, confiança de certificados ou serviços Windows.'
