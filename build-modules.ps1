param([string]$Dotnet="$env:LOCALAPPDATA/PCinemaBuild/dotnet/dotnet.exe")
$ErrorActionPreference='Stop'
$root=$PSScriptRoot
$out=Join-Path $root 'artifacts/modules'
New-Item -ItemType Directory -Force $out | Out-Null
Add-Type -AssemblyName System.IO.Compression
foreach($name in @('Compact','SleepTimer')) {
 & $Dotnet build (Join-Path $root "examples/$name/$name.csproj") -c Release
 if($LASTEXITCODE -ne 0){throw "Build failed: $name"}
}
foreach($name in @('Compact','SleepTimer','Font')) {
 $source=Join-Path $root "examples/$name"
 $manifest=Get-Content (Join-Path $source 'manifest.json') -Raw | ConvertFrom-Json
 $target=Join-Path $out "$($manifest.id)-$($manifest.version).pcmodule"
 $stream=[IO.File]::Open($target,[IO.FileMode]::Create)
 $zip=[IO.Compression.ZipArchive]::new($stream,[IO.Compression.ZipArchiveMode]::Create)
 try {
  $files=@{ 'manifest.json'=(Join-Path $source 'manifest.json') }
  if($name -eq 'Font') { $files['PT_Sans-Web-Regular.ttf']=Join-Path $source 'PT_Sans-Web-Regular.ttf';$files['OFL.txt']=Join-Path $source 'OFL.txt' }
  else { $files["$name.dll"]=Join-Path $source "bin/Release/net10.0-windows/$name.dll";$files['LICENSE.txt']=Join-Path $root 'examples/LICENSE.txt' }
  foreach($entry in $files.GetEnumerator()) { [IO.Compression.ZipFileExtensions]::CreateEntryFromFile($zip,$entry.Value,$entry.Key,[IO.Compression.CompressionLevel]::Optimal) | Out-Null }
 } finally {$zip.Dispose();$stream.Dispose()}
 Write-Output $target
}
