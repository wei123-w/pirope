$ErrorActionPreference = 'Stop'
$installPath = [IO.Path]::GetFullPath((Join-Path ([Environment]::GetFolderPath('LocalApplicationData')) 'Programs\PicRope'))
$sourcePath = Join-Path $PSScriptRoot 'app'
if (-not (Test-Path -LiteralPath (Join-Path $sourcePath 'PicRope.exe'))) { throw '安装文件不完整，请先完整解压安装包。' }
$framework = Get-ItemProperty 'HKLM:\SOFTWARE\Microsoft\NET Framework Setup\NDP\v4\Full'
if ($framework.Release -lt 528040) { throw '请先安装 .NET Framework 4.8。' }
$running = @(Get-CimInstance Win32_Process -Filter "Name = 'PicRope.exe'")
if ($running.Count) { throw '请先从托盘退出图绳，再重新安装。' }
if ((Test-Path -LiteralPath $installPath) -and ((Get-Item -LiteralPath $installPath -Force).Attributes -band [IO.FileAttributes]::ReparsePoint)) { throw '安装目录不能是目录链接。' }
New-Item -ItemType Directory -Path $installPath -Force | Out-Null
Copy-Item -Path (Join-Path $sourcePath '*') -Destination $installPath -Recurse -Force
$exePath = Join-Path $installPath 'PicRope.exe'
$shell = New-Object -ComObject WScript.Shell
$shortcut = $shell.CreateShortcut((Join-Path ([Environment]::GetFolderPath('Desktop')) 'PicRope.lnk'))
$shortcut.TargetPath = $exePath
$shortcut.WorkingDirectory = $installPath
$shortcut.IconLocation = $exePath + ',0'
$shortcut.Description = '图绳：把截图挂在屏幕上沿，随手取用。'
$shortcut.Save()
$uninstallKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\PicRope'
New-Item -Path $uninstallKey -Force | Out-Null
New-ItemProperty -Path $uninstallKey -Name DisplayName -Value 'PicRope · 图绳' -PropertyType String -Force | Out-Null
New-ItemProperty -Path $uninstallKey -Name DisplayVersion -Value ([Diagnostics.FileVersionInfo]::GetVersionInfo($exePath).ProductVersion) -PropertyType String -Force | Out-Null
New-ItemProperty -Path $uninstallKey -Name InstallLocation -Value $installPath -PropertyType String -Force | Out-Null
New-ItemProperty -Path $uninstallKey -Name DisplayIcon -Value ($exePath + ',0') -PropertyType String -Force | Out-Null
$uninstallCommand = 'powershell.exe -NoProfile -ExecutionPolicy Bypass -File "' + (Join-Path $installPath 'uninstall.ps1') + '"'
New-ItemProperty -Path $uninstallKey -Name UninstallString -Value $uninstallCommand -PropertyType String -Force | Out-Null
New-ItemProperty -Path $uninstallKey -Name NoModify -Value 1 -PropertyType DWord -Force | Out-Null
New-ItemProperty -Path $uninstallKey -Name NoRepair -Value 1 -PropertyType DWord -Force | Out-Null
Start-Process -FilePath $exePath -WindowStyle Hidden
Write-Output '图绳安装完成，桌面已创建 PicRope 快捷方式。'
