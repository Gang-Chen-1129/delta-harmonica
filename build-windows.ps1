$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$projectPath = Join-Path $root 'DeltaHarmonica/DeltaHarmonica.csproj'
[xml]$project = Get-Content $projectPath
$version = [string]$project.Project.PropertyGroup.Version
if ([string]::IsNullOrWhiteSpace($version)) { throw 'Project version is missing' }
$output = Join-Path $root 'dist/三角洲口琴-Windows-x64-便携版'
if (Test-Path $output) { Remove-Item $output -Recurse -Force }
dotnet publish $projectPath -c Release -r win-x64 --self-contained true -p:PublishSingleFile=false -o $output
if ($LASTEXITCODE -ne 0) { throw 'dotnet publish failed' }
$songsOutput = Join-Path $output 'songs'
New-Item -ItemType Directory -Path $songsOutput -Force | Out-Null
Copy-Item (Join-Path $root 'songs/音阶示例.txt') $songsOutput
Copy-Item (Join-Path $root 'README.md') $output
Copy-Item (Join-Path $root '三角洲口琴-使用说明.md') $output
Copy-Item (Join-Path $root 'LICENSE') $output
$archive = Join-Path $root "dist/三角洲口琴-v$version-Windows-x64-便携版.zip"
$githubArchive = Join-Path $root "dist/DeltaHarmonica-v$version-Windows-x64-portable.zip"
Get-ChildItem (Join-Path $root 'dist') -Filter '三角洲口琴-v*-Windows-x64-便携版.zip' |
    Remove-Item -Force
Get-ChildItem (Join-Path $root 'dist') -Filter 'DeltaHarmonica-v*-Windows-x64-portable.zip' |
    Remove-Item -Force
Get-ChildItem (Join-Path $root 'dist') -Filter 'DeltaHarmonica-v*-win-x64-portable.zip' |
    Remove-Item -Force
Compress-Archive -Path $output -DestinationPath $archive -CompressionLevel Optimal
Copy-Item $archive $githubArchive
$hash = (Get-FileHash -Algorithm SHA256 $archive).Hash.ToLowerInvariant()
Write-Host "已生成 $archive"
Write-Host "GitHub 上传用文件：$githubArchive（发布时设置中文显示标签）"
Write-Host "SHA-256: $hash"
