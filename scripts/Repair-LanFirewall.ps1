# Execute no PC servidor, em PowerShell como administrador.
$ErrorActionPreference = 'Stop'
$administrator = ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
if (-not $administrator) { throw 'Abra PowerShell como administrador no PC servidor e execute novamente.' }
$configurationPath = Join-Path $env:ProgramData 'RemoteDeskServer\lan.json'
$configuration = Get-Content -LiteralPath $configurationPath -Raw | ConvertFrom-Json
$serverIp = [Net.IPAddress]::Parse($configuration.Ip)
if ($serverIp.AddressFamily -ne [Net.Sockets.AddressFamily]::InterNetwork -or [Net.IPAddress]::IsLoopback($serverIp)) { throw 'IPv4 LAN inválido.' }
if (-not (Get-NetIPAddress -AddressFamily IPv4 | Where-Object IPAddress -eq $configuration.Ip)) { throw 'O IP configurado não pertence mais a este PC. Confira lan.json.' }
$binaryRoot = Join-Path $env:ProgramFiles 'RemoteDesk Server'
$components = @(
    @{ Name='Server'; Path='server\RemoteDesk.Server.exe'; Uri=$configuration.ServerUri },
    @{ Name='Relay'; Path='relay\RemoteDesk.Relay.exe'; Uri=$configuration.RelayUri }
)
foreach ($component in $components) {
    $program = Join-Path $binaryRoot $component.Path
    if (-not (Test-Path -LiteralPath $program -PathType Leaf)) { throw "Componente instalado não encontrado: $program" }
    $uri = [Uri]$component.Uri
    if ($uri.Scheme -ne 'wss' -or $uri.Host -ne $configuration.Ip -or $uri.Port -lt 1) { throw 'Endereço LAN inválido.' }
}
foreach ($component in $components) {
    $name = 'RemoteDesk-LAN-' + $component.Name
    $parameters = @{
        Direction='Inbound'; Action='Allow'; Enabled='True'; Profile='Any'; Protocol='TCP'
        LocalPort=([Uri]$component.Uri).Port; LocalAddress=$configuration.Ip; RemoteAddress='LocalSubnet'
        Program=(Join-Path $binaryRoot $component.Path)
    }
    if (Get-NetFirewallRule -Name $name -ErrorAction SilentlyContinue) { Set-NetFirewallRule -Name $name @parameters | Out-Null }
    else { New-NetFirewallRule -Name $name -DisplayName ("RemoteDesk LAN " + $component.Name) @parameters | Out-Null }
}
Write-Output 'Firewall configurado somente para o IP LAN do servidor e clientes da sub-rede local. API interna não exposta.'
Write-Output ("Servidor: " + $configuration.ServerUri)
