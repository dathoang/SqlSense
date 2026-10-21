<#
.SYNOPSIS
    Installs (or updates) SqlSense into every SQL Server Management Studio 22 instance on this machine.

.DESCRIPTION
    Finds SSMS 22 with vswhere (falls back to the default install folder and SSMS.isolation.ini), checks that SSMS
    is closed, and runs SSMS's own VSIXInstaller.exe against SqlSense.vsix in this folder. The extension is
    installed per user, so administrator rights are not needed.

.PARAMETER Vsix
    Path of the .vsix to install. Default: SqlSense.vsix next to this script.

.PARAMETER InstanceId
    Install into these SSMS instance ids only (see SSMS.isolation.ini › InstallationID). Default: all SSMS 22 instances.

.PARAMETER CloseSsms
    Close running SSMS windows instead of stopping with an error. Unsaved query windows are lost.

.EXAMPLE
    .\Install.ps1
.EXAMPLE
    .\Install.ps1 -CloseSsms
#>
[CmdletBinding()]
param(
    [string]$Vsix,
    [string[]]$InstanceId,
    [switch]$CloseSsms
)

$ErrorActionPreference = 'Stop'
# Windows PowerShell 5.1 leaves $PSScriptRoot empty in param defaults, so default here.
if (-not $Vsix) { $Vsix = Join-Path $PSScriptRoot 'SqlSense.vsix' }
. (Join-Path $PSScriptRoot 'SsmsSetup.ps1')

if (-not (Test-Path -LiteralPath $Vsix)) { throw "Không tìm thấy file VSIX: $Vsix" }
$Vsix = (Resolve-Path -LiteralPath $Vsix).Path
$ext = Get-VsixIdentity $Vsix
Write-Host "SqlSense $($ext.Version)  ($($ext.Id))" -ForegroundColor Cyan

$instances = @(Get-Ssms22Instances)
if ($InstanceId) { $instances = @($instances | Where-Object { $InstanceId -contains $_.InstanceId }) }
if ($instances.Count -eq 0) {
    throw 'Không tìm thấy SQL Server Management Studio 22 (x64). Cài SSMS 22 trước: https://aka.ms/ssms/22'
}
foreach ($i in $instances) { Write-Host "  SSMS $($i.Version)  id=$($i.InstanceId)  $($i.Path)" }

Assert-SsmsClosed -Close:$CloseSsms

$installer = Join-Path $instances[0].Path 'Common7\IDE\VSIXInstaller.exe'
$ids = ($instances | ForEach-Object { $_.InstanceId }) -join ','
$log = 'SqlSense_install_' + (Get-Date -Format 'yyyyMMdd_HHmmss') + '.log'

Write-Host "Đang cài vào SSMS ($ids)..."
$p = Start-Process -FilePath $installer -ArgumentList @('/quiet', "/instanceIds:$ids", "/logFile:$log", "`"$Vsix`"") -Wait -PassThru
$logPath = Join-Path $env:TEMP $log

switch ($p.ExitCode) {
    0 {
        Write-Host "Đã cài SqlSense $($ext.Version)." -ForegroundColor Green
        Write-Host 'Mở SSMS 22, kiểm tra menu Tools › SqlSense. Hướng dẫn: HUONG-DAN-SU-DUNG.docx'
    }
    1001 {
        Write-Warning "Phiên bản $($ext.Version) đã được cài rồi. Muốn cài lại: chạy Uninstall.ps1 rồi Install.ps1, hoặc dùng bản VSIX có Version cao hơn."
    }
    default {
        Write-Error "VSIXInstaller trả về mã $($p.ExitCode). Xem log: $logPath"
        exit $p.ExitCode
    }
}
