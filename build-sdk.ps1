param([string]$Dotnet="$env:LOCALAPPDATA/PCinemaBuild/dotnet/dotnet.exe")
$ErrorActionPreference='Stop'
& $Dotnet build "$PSScriptRoot/PCinema.ModuleApi/PCinema.ModuleApi.csproj" -c Release
if($LASTEXITCODE -ne 0){throw 'SDK build failed'}
Add-Type -AssemblyName System.IO.Compression
$output=Join-Path $PSScriptRoot 'artifacts/PCinema-SDK-1.1.0.zip'
$stream=[IO.File]::Open($output,[IO.FileMode]::Create)
$zip=[IO.Compression.ZipArchive]::new($stream,[IO.Compression.ZipArchiveMode]::Create)
try {
 $files=@{'PCinema.ModuleApi.dll'='PCinema.ModuleApi/bin/Release/net10.0-windows/PCinema.ModuleApi.dll';'MODULES.md'='docs/MODULES.md';'Contracts.cs'='PCinema.ModuleApi/Contracts.cs';'LICENSE.txt'='examples/LICENSE.txt';'PCinema.ModuleApi/PCinema.ModuleApi.csproj'='PCinema.ModuleApi/PCinema.ModuleApi.csproj';'PCinema.ModuleApi/Contracts.cs'='PCinema.ModuleApi/Contracts.cs'}
 foreach($entry in $files.GetEnumerator()){[IO.Compression.ZipFileExtensions]::CreateEntryFromFile($zip,(Join-Path $PSScriptRoot $entry.Value),$entry.Key)|Out-Null}
 foreach($dir in @('templates','examples')) {
  Get-ChildItem -LiteralPath (Join-Path $PSScriptRoot $dir) -Recurse -File | Where-Object { $_.FullName -notmatch '[\\/](bin|obj)[\\/]' -and $_.Extension -ne '.pcmodule' } | ForEach-Object {
   $name=[IO.Path]::GetRelativePath($PSScriptRoot,$_.FullName).Replace('\','/')
   [IO.Compression.ZipFileExtensions]::CreateEntryFromFile($zip,$_.FullName,$name)|Out-Null
  }
 }
} finally {$zip.Dispose();$stream.Dispose()}
Write-Output $output
