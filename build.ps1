param([string]$Dotnet = 'dotnet', [string]$Iscc = 'C:\Program Files (x86)\Inno Setup 6\ISCC.exe')
$ErrorActionPreference = 'Stop'
if ($Dotnet -eq 'dotnet' -and -not (Get-Command dotnet -ErrorAction SilentlyContinue)) { $Dotnet = "$env:LOCALAPPDATA\PCinemaBuild\dotnet\dotnet.exe" }
if (-not (Test-Path -LiteralPath $Iscc)) { $Iscc = "$env:LOCALAPPDATA\PCinemaBuild\InnoSetup\ISCC.exe" }
$publishDirectory = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot 'artifacts/publish'))
$workspacePrefix = [IO.Path]::GetFullPath($PSScriptRoot).TrimEnd([IO.Path]::DirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
if (-not $publishDirectory.StartsWith($workspacePrefix, [StringComparison]::OrdinalIgnoreCase)) { throw 'Publish directory is outside workspace' }
& $Dotnet test "$PSScriptRoot\PCinema.Tests\PCinema.Tests.csproj" -c Release
if ($LASTEXITCODE -ne 0) { throw 'Tests failed' }
if (Test-Path -LiteralPath $publishDirectory) { Remove-Item -LiteralPath $publishDirectory -Recurse -Force }
& $Dotnet publish "$PSScriptRoot\PCinema\PCinema.csproj" -c Release -r win-x64 --self-contained true -o "$PSScriptRoot\artifacts\publish"
if ($LASTEXITCODE -ne 0) { throw 'Publish failed' }
& $Iscc /Q "$PSScriptRoot\installer\PCinema.iss"
if ($LASTEXITCODE -ne 0) { throw 'Installer build failed' }
