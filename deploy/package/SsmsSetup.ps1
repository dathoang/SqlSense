# Shared helpers for Install.ps1 / Uninstall.ps1 (dot-sourced). Windows PowerShell 5.1 compatible.

function Get-Ssms22Instances {
    $found = @{}
    $vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
    if (Test-Path -LiteralPath $vswhere) {
        $json = & $vswhere -all -prerelease -products Microsoft.VisualStudio.Product.Ssms -format json 2>$null
        if ($LASTEXITCODE -eq 0 -and $json) {
            foreach ($i in ($json | Out-String | ConvertFrom-Json)) {
                if ($i.installationVersion -like '22.*') {
                    $found[$i.instanceId] = [pscustomobject]@{
                        InstanceId = $i.instanceId; Path = $i.installationPath; Version = $i.installationVersion
                    }
                }
            }
        }
    }
    if ($found.Count -eq 0) {
        # vswhere missing or SSMS not registered: try the default folder.
        $default = Join-Path $env:ProgramFiles 'Microsoft SQL Server Management Studio 22\Release'
        $ini = Join-Path $default 'Common7\IDE\SSMS.isolation.ini'
        if (Test-Path -LiteralPath $ini) {
            $text = Get-Content -LiteralPath $ini -Raw
            $id = [regex]::Match($text, '(?m)^InstallationID=(\S+)').Groups[1].Value
            $ver = [regex]::Match($text, '(?m)^InstallationVersion=(\S+)').Groups[1].Value
            if ($id) { $found[$id] = [pscustomobject]@{ InstanceId = $id; Path = $default; Version = $ver } }
        }
    }
    $found.Values | Where-Object { Test-Path -LiteralPath (Join-Path $_.Path 'Common7\IDE\VSIXInstaller.exe') }
}

function Get-VsixIdentity([string]$Path) {
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $zip = [System.IO.Compression.ZipFile]::OpenRead($Path)
    try {
        $entry = $zip.GetEntry('extension.vsixmanifest')
        if (-not $entry) { throw "$Path không phải VSIX hợp lệ (thiếu extension.vsixmanifest)." }
        $reader = New-Object System.IO.StreamReader($entry.Open())
        try { [xml]$xml = $reader.ReadToEnd() } finally { $reader.Dispose() }
    } finally { $zip.Dispose() }
    $identity = $xml.PackageManifest.Metadata.Identity
    [pscustomobject]@{ Id = $identity.Id; Version = $identity.Version }
}

function Assert-SsmsClosed([switch]$Close) {
    $running = @(Get-Process -Name 'Ssms' -ErrorAction SilentlyContinue)
    if ($running.Count -eq 0) { return }
    if (-not $Close) {
        throw 'SSMS đang chạy. Đóng tất cả cửa sổ SSMS rồi chạy lại (hoặc thêm -CloseSsms để script tự đóng).'
    }
    Write-Warning 'Đang đóng SSMS...'
    $running | ForEach-Object { $_.CloseMainWindow() | Out-Null }
    $running | Wait-Process -Timeout 20 -ErrorAction SilentlyContinue
    Get-Process -Name 'Ssms' -ErrorAction SilentlyContinue | Stop-Process -Force
}
