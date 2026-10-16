$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem
$projectRoot = Split-Path $PSScriptRoot -Parent
$output = Join-Path $projectRoot 'artifacts\RemoteDesk-source.zip'
New-Item -ItemType Directory -Force (Split-Path $output -Parent) | Out-Null
$excludedDirectories = @('bin','obj','artifacts','.git','data','__pycache__','.venv','build','dist','.pytest_cache')
$files = @(Get-ChildItem -LiteralPath $projectRoot -File -Recurse | Where-Object {
    $relative = $_.FullName.Substring($projectRoot.Length + 1)
    $parts = $relative -split '[\\/]'
    $excluded = @($parts | Where-Object { $_ -in $excludedDirectories }).Count -gt 0
    -not $excluded -and -not ($_.Attributes -band [IO.FileAttributes]::ReparsePoint) -and
    $_.Name -notin @('payload.zip','.env') -and $_.Name -notlike '*.local.json' -and $_.Extension.ToLowerInvariant() -notin @('.dpapi','.pfx','.part','.zip','.log','.pem','.key','.p12') -and
    -not ($parts.Count -gt 2 -and $parts[0] -eq 'docs' -and $parts[1] -eq 'evidence' -and $_.Extension -ne '.txt')
})
$stream = [IO.File]::Open($output, [IO.FileMode]::Create)
$archive = New-Object IO.Compression.ZipArchive($stream, [IO.Compression.ZipArchiveMode]::Create)
try {
    foreach ($file in $files) {
        $relative = $file.FullName.Substring($projectRoot.Length + 1).Replace('\','/')
        [IO.Compression.ZipFileExtensions]::CreateEntryFromFile($archive, $file.FullName, "RemoteDesk/$relative", [IO.Compression.CompressionLevel]::Optimal) | Out-Null
    }
} finally { $archive.Dispose(); $stream.Dispose() }
Write-Output "$output : $($files.Count) arquivos de código e documentação"
