$ErrorActionPreference = 'Stop'
$installPath = [IO.Path]::GetFullPath((Join-Path ([Environment]::GetFolderPath('LocalApplicationData')) 'Programs\PicRope'))
if (-not [string]::Equals([IO.Path]::GetFullPath($PSScriptRoot), $installPath, [StringComparison]::OrdinalIgnoreCase)) { throw '卸载程序必须位于图绳安装目录。' }
if ((Get-Item -LiteralPath $installPath -Force).Attributes -band [IO.FileAttributes]::ReparsePoint) { throw '安装目录不能是目录链接。' }
$exePath = Join-Path $installPath 'PicRope.exe'
$running = @(Get-CimInstance Win32_Process -Filter "Name = 'PicRope.exe'" | Where-Object { $_.ExecutablePath -eq $exePath })
if ($running.Count) {
    Start-Process -FilePath $exePath -ArgumentList '--quit' -WindowStyle Hidden -Wait
    foreach ($process in $running) {
        $active = Get-Process -Id $process.ProcessId -ErrorAction SilentlyContinue
        if ($active -and -not $active.WaitForExit(10000)) { throw '图绳仍在运行，请先处理未保存的图片并退出。' }
    }
}
$shortcutPath = Join-Path ([Environment]::GetFolderPath('Desktop')) 'PicRope.lnk'
if (Test-Path -LiteralPath $shortcutPath) {
    $shortcut = (New-Object -ComObject WScript.Shell).CreateShortcut($shortcutPath)
    if ($shortcut.TargetPath -eq $exePath) { Remove-Item -LiteralPath $shortcutPath -Force }
}
$runKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'
foreach ($name in @('PicRope')) { Remove-ItemProperty -LiteralPath $runKey -Name $name -ErrorAction SilentlyContinue }
Remove-Item -LiteralPath 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\PicRope' -Recurse -Force -ErrorAction SilentlyContinue
Remove-Item -LiteralPath $installPath -Recurse -Force
Write-Output '图绳已卸载，程序、收集副本、设置、缓存和桌面快捷方式已清理。'
