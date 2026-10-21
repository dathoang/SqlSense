<#
.SYNOPSIS
    Removes SqlSense from every SQL Server Management Studio 22 instance on this machine.
.PARAMETER CloseSsms
    Close running SSMS windows instead of stopping with an error.
#>
[CmdletBinding()]
param([switch]$CloseSsms)

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'SsmsSetup.ps1')

$extensionId = 'SqlSense.Ssms22.6f2d0c55-3b1e-4f4e-9d7a-1c8a52b7e0a1'

$instances = @(Get-Ssms22Instances)
if ($instances.Count -eq 0) { throw 'Không tìm thấy SQL Server Management Studio 22.' }
Assert-SsmsClosed -Close:$CloseSsms

$installer = Join-Path $instances[0].Path 'Common7\IDE\VSIXInstaller.exe'
$ids = ($instances | ForEach-Object { $_.InstanceId }) -join ','
$log = 'SqlSense_uninstall_' + (Get-Date -Format 'yyyyMMdd_HHmmss') + '.log'

Write-Host "Đang gỡ SqlSense khỏi SSMS ($ids)..."
$p = Start-Process -FilePath $installer -ArgumentList @('/quiet', "/instanceIds:$ids", "/logFile:$log", "/uninstall:$extensionId") -Wait -PassThru

switch ($p.ExitCode) {
    0    { Write-Host 'Đã gỡ SqlSense.' -ForegroundColor Green }
    1002 { Write-Warning 'SqlSense chưa được cài.' }
    default {
        Write-Error "VSIXInstaller trả về mã $($p.ExitCode). Xem log: $(Join-Path $env:TEMP $log)"
        exit $p.ExitCode
    }
}
# Settings (Tools › Options), snippets and Documents\SqlSense\ConnectionColors.txt are kept.
