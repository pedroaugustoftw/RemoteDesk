param([switch]$BuildOutputs,[switch]$WhatIf)
$ErrorActionPreference='Stop'
$projectRoot=[IO.Path]::GetFullPath((Split-Path $PSScriptRoot -Parent)).TrimEnd('\','/')
$artifacts=Join-Path $projectRoot 'artifacts'
$targets=@()
if(Test-Path -LiteralPath $artifacts){
    # Published canonical packages, the current build and release outputs are kept.
    $targets+=Get-ChildItem -LiteralPath $artifacts -Directory | Where-Object Name -NotIn @('win-x64','portable','release','maintenance') | ForEach-Object FullName
    $win=Join-Path $artifacts 'win-x64'
    if(Test-Path -LiteralPath $win){$targets+=Get-ChildItem -LiteralPath $win -Directory | Where-Object Name -NotIn @('client','installer','server-installer','server-service','uninstaller','server','relay','lan-tools') | ForEach-Object FullName}
    $targets+=Get-ChildItem -LiteralPath $artifacts -File | Where-Object { $_.Extension -eq '.png' -or ($_.Extension -eq '.py' -and $_.Name -notin @('ci-status.py','github-check.py')) } | ForEach-Object FullName
}
if($BuildOutputs){
    foreach($relative in (& git -C $projectRoot ls-files '*.csproj')){
        $directory=Split-Path (Join-Path $projectRoot $relative) -Parent
        foreach($name in @('bin','obj')){ $candidate=Join-Path $directory $name;if(Test-Path -LiteralPath $candidate){$targets+=$candidate} }
    }
}
$bytes=0L;$count=0
foreach($target in ($targets | Sort-Object -Unique)){
    $resolved=[IO.Path]::GetFullPath($target)
    if(!$resolved.StartsWith($projectRoot+[IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase)){throw "Destino fora do projeto: $resolved"}
    $item=Get-Item -LiteralPath $resolved -Force
    if($item.Attributes -band [IO.FileAttributes]::ReparsePoint){throw "Link rejeitado: $resolved"}
    $descendants=@();if($item.PSIsContainer){$descendants=@(Get-ChildItem -LiteralPath $resolved -Recurse -Force);if($descendants | Where-Object {$_.Attributes -band [IO.FileAttributes]::ReparsePoint}){throw "Link interno rejeitado: $resolved"}}
    $size=if($item.PSIsContainer){($descendants | Where-Object {!$_.PSIsContainer} | Measure-Object Length -Sum).Sum}else{$item.Length}
    if(!$WhatIf){Remove-Item -LiteralPath $resolved -Recurse -Force}
    $bytes+=$size;$count++
}
[pscustomobject]@{Preview=[bool]$WhatIf;Targets=$count;FreedMiB=[math]::Round($bytes/1MB,2);Scope=$projectRoot}
