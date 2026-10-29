param([string]$Dotnet='dotnet',[string]$SdkPath='../../PCinema.ModuleApi/bin/Release/net10.0-windows/PCinema.ModuleApi.dll')
$ErrorActionPreference='Stop'
& $Dotnet build "$PSScriptRoot/Module.csproj" -c Release "-p:SdkPath=$SdkPath"
if($LASTEXITCODE -ne 0){throw 'Build failed'}
$manifest=Get-Content "$PSScriptRoot/manifest.json" -Raw | ConvertFrom-Json
Add-Type -AssemblyName System.IO.Compression
$output=Join-Path $PSScriptRoot "$($manifest.id)-$($manifest.version).pcmodule"
$stream=[IO.File]::Open($output,[IO.FileMode]::Create)
$zip=[IO.Compression.ZipArchive]::new($stream,[IO.Compression.ZipArchiveMode]::Create)
try {
 foreach($file in @('manifest.json','LICENSE.txt')) { if(Test-Path "$PSScriptRoot/$file"){[IO.Compression.ZipFileExtensions]::CreateEntryFromFile($zip,"$PSScriptRoot/$file",$file)|Out-Null} }
 [IO.Compression.ZipFileExtensions]::CreateEntryFromFile($zip,"$PSScriptRoot/bin/Release/net10.0-windows/MyModule.dll",'MyModule.dll')|Out-Null
} finally {$zip.Dispose();$stream.Dispose()}
Write-Output $output
