param([string]$ConfigDirectory = (Join-Path $env:LOCALAPPDATA 'RemoteDesk\infrastructure'))
$ErrorActionPreference = 'Stop'
$recordPath = Join-Path $ConfigDirectory 'processes.json'
if (-not (Test-Path -LiteralPath $recordPath)) { Write-Output 'Nenhum processo RemoteDesk LAN registrado.'; return }
$records = Get-Content -LiteralPath $recordPath -Raw | ConvertFrom-Json
foreach ($record in $records) {
    $process = Get-Process -Id $record.Id -ErrorAction SilentlyContinue
    if ($null -eq $process) { continue }
    $actualPath = [System.IO.Path]::GetFullPath($process.Path)
    $expectedPath = [System.IO.Path]::GetFullPath($record.Executable)
    if (-not $actualPath.Equals($expectedPath, [StringComparison]::OrdinalIgnoreCase) -or $process.StartTime.ToUniversalTime().Ticks -ne [long]$record.StartTicks) {
        Write-Warning "PID $($record.Id) não corresponde ao processo originalmente iniciado; preservado."
        continue
    }
    $information = Get-CimInstance Win32_Process -Filter "ProcessId = $($record.Id)"
    if ($null -eq $information) { continue }
    if ($information.CommandLine -notlike ('*' + $record.Assembly + '*')) {
        Write-Warning "Linha de comando do PID $($record.Id) não corresponde ao componente; preservado."
        continue
    }
    Stop-Process -Id $record.Id
    Write-Output "RemoteDesk LAN encerrado: PID $($record.Id)"
}
