param([Parameter(Mandatory=$true)][string]$WorkPath, [Parameter(Mandatory=$true)][string]$TargetPath, [int]$ParentProcessId)
$ErrorActionPreference = 'Stop'
$cacheRoot = [IO.Path]::GetFullPath((Join-Path ([Environment]::GetFolderPath('LocalApplicationData')) 'PicRope\Updates'))
$workRoot = [IO.Path]::GetFullPath($WorkPath).TrimEnd('\')
$targetRoot = [IO.Path]::GetFullPath($TargetPath).TrimEnd('\')
$sourceRoot = Join-Path $workRoot 'contents\app'
$exePath = Join-Path $targetRoot 'PicRope.exe'
$backupRoot = Join-Path $workRoot 'backup'
$changed = New-Object 'Collections.Generic.List[object]'
$mayRestart = $false
function Copy-UpdateFile([string]$SourceFile, [string]$DestinationFile) {
    $inputStream = [IO.File]::OpenRead($SourceFile)
    try {
        $outputStream = [IO.File]::Create($DestinationFile)
        try { $inputStream.CopyTo($outputStream) } finally { $outputStream.Dispose() }
    }
    finally { $inputStream.Dispose() }
}
function Assert-PlainPath([string]$PathToCheck, [string]$AllowedRoot) {
    $resolved = [IO.Path]::GetFullPath($PathToCheck)
    if (-not ($resolved -eq $AllowedRoot -or $resolved.StartsWith($AllowedRoot + '\', [StringComparison]::OrdinalIgnoreCase))) { throw '更新文件超出安装目录。' }
    $ancestor = $resolved
    while ($ancestor -and $ancestor.Length -ge $AllowedRoot.Length) {
        if ((Test-Path -LiteralPath $ancestor) -and ((Get-Item -LiteralPath $ancestor -Force).Attributes -band [IO.FileAttributes]::ReparsePoint)) { throw '更新目录不能包含目录链接。' }
        $ancestor = Split-Path -Path $ancestor -Parent
    }
}
try {
    if (-not $workRoot.StartsWith($cacheRoot + '\', [StringComparison]::OrdinalIgnoreCase)) { throw '更新缓存目录无效。' }
    Assert-PlainPath $workRoot $cacheRoot
    if (-not (Test-Path -LiteralPath $exePath) -or -not (Test-Path -LiteralPath (Join-Path $sourceRoot 'PicRope.exe'))) { throw '更新文件不完整。' }
    Assert-PlainPath $targetRoot $targetRoot
    if ($ParentProcessId -gt 0) {
        $parent = Get-Process -Id $ParentProcessId -ErrorAction SilentlyContinue
        if ($parent -and -not $parent.WaitForExit(30000)) { throw '图绳尚未退出，请先保存图片并关闭程序。' }
    }
    $mayRestart = $true
    foreach ($file in Get-ChildItem -LiteralPath $sourceRoot -Recurse -File | Sort-Object FullName) {
        $relative = $file.FullName.Substring($sourceRoot.Length + 1)
        if ($relative -match '^(data|test-data)(\\|$)') { throw '更新包不能覆盖用户图片和设置。' }
        Assert-PlainPath $file.FullName $sourceRoot
        $destination = Join-Path $targetRoot $relative
        Assert-PlainPath $destination $targetRoot
        $backup = Join-Path $backupRoot $relative
        $existed = Test-Path -LiteralPath $destination
        if ($existed) {
            New-Item -ItemType Directory -Path (Split-Path $backup -Parent) -Force | Out-Null
            Copy-UpdateFile $destination $backup
        }
        $changed.Add([PSCustomObject]@{ Destination=$destination; Backup=$backup; Existed=$existed })
        New-Item -ItemType Directory -Path (Split-Path $destination -Parent) -Force | Out-Null
        Copy-UpdateFile $file.FullName $destination
    }
    $uninstallKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\PicRope'
    $registered = Get-ItemProperty -LiteralPath $uninstallKey -ErrorAction SilentlyContinue
    if ($registered -and [string]::Equals($registered.InstallLocation, $targetRoot, [StringComparison]::OrdinalIgnoreCase)) {
        $version = [Diagnostics.FileVersionInfo]::GetVersionInfo($exePath).ProductVersion
        Set-ItemProperty -LiteralPath $uninstallKey -Name DisplayVersion -Value $version
    }
    Start-Process -FilePath $exePath -WorkingDirectory $targetRoot -WindowStyle Hidden
    try { Assert-PlainPath $workRoot $cacheRoot; Remove-Item -LiteralPath $workRoot -Recurse -Force } catch { }
}
catch {
    $message = $_.Exception.Message
    Write-Error -Message ($message + "`r`n" + $_.InvocationInfo.PositionMessage) -ErrorAction Continue
    $restored = $true
    foreach ($file in $changed) {
        try {
            Assert-PlainPath $file.Destination $targetRoot
            if ($file.Existed) { Copy-UpdateFile $file.Backup $file.Destination }
            elseif (Test-Path -LiteralPath $file.Destination) { Remove-Item -LiteralPath $file.Destination -Force }
        }
        catch { $restored = $false }
    }
    if ($mayRestart -and $restored) { Start-Process -FilePath $exePath -WorkingDirectory $targetRoot -WindowStyle Hidden }
    Add-Type -AssemblyName System.Windows.Forms
    $recovery = if ($restored) { '原版本已保留，可稍后重试。' } else { '请重新运行安装包修复程序，图片和设置仍保留在 data 文件夹。' }
    [Windows.Forms.MessageBox]::Show(('更新未完成：' + $message + "`r`n" + $recovery), '图绳更新', 'OK', 'Warning') | Out-Null
    exit 1
}
