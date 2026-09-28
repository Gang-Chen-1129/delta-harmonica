$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$projectPath = Join-Path $root 'DeltaHarmonica/DeltaHarmonica.csproj'
[xml]$project = Get-Content $projectPath
$version = [string]$project.Project.PropertyGroup.Version
if ([string]::IsNullOrWhiteSpace($version)) { throw 'Project version is missing' }
$output = Join-Path $root 'dist/DeltaHarmonica-win-x64-portable'
if (Test-Path $output) { Remove-Item $output -Recurse -Force }
dotnet publish $projectPath -c Release -r win-x64 --self-contained true -p:PublishSingleFile=false -o $output
if ($LASTEXITCODE -ne 0) { throw 'dotnet publish failed' }
$songsOutput = Join-Path $output 'songs'
New-Item -ItemType Directory -Path $songsOutput -Force | Out-Null
Copy-Item (Join-Path $root 'songs/scale-example.txt') $songsOutput
Copy-Item (Join-Path $root 'README.md') $output
Copy-Item (Join-Path $root 'README.zh-CN.md') $output
Copy-Item (Join-Path $root 'LICENSE') $output
$archive = Join-Path $root "dist/DeltaHarmonica-v$version-win-x64-portable.zip"
Get-ChildItem (Join-Path $root 'dist') -Filter 'DeltaHarmonica*win-x64-portable.zip' |
    Remove-Item -Force
Compress-Archive -Path $output -DestinationPath $archive -CompressionLevel Optimal
$hash = (Get-FileHash -Algorithm SHA256 $archive).Hash.ToLowerInvariant()
Write-Host "已生成 $archive"
Write-Host "SHA-256: $hash"
