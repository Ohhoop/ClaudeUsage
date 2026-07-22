param([switch]$Install)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $MyInvocation.MyCommand.Path

dotnet test (Join-Path $root 'ClaudeUsage.sln') -c Release --nologo -v minimal
if ($LASTEXITCODE -ne 0) { exit 1 }

dotnet publish (Join-Path $root 'src\ClaudeUsage') -c Release -r win-x64 --no-self-contained /p:PublishSingleFile=true /p:DebugType=None -o (Join-Path $root 'dist')
if ($LASTEXITCODE -ne 0) { exit 1 }

$exe = Join-Path $root 'dist\ClaudeUsage.exe'
"Publie: $exe ({0:N0} octets)" -f (Get-Item $exe).Length

if ($Install) {
    $target = Join-Path $env:LOCALAPPDATA 'Programs\ClaudeUsage'
    New-Item -ItemType Directory -Force $target | Out-Null
    $installed = Join-Path $target 'ClaudeUsage.exe'
    Get-Process ClaudeUsage -ErrorAction SilentlyContinue | Stop-Process -Force
    Start-Sleep -Milliseconds 500
    Copy-Item $exe $installed -Force
    Start-Process $installed
    "Installe et lance: $installed"
}
