param([string]$ArchivePath)
$ErrorActionPreference = 'Stop'
if (-not $ArchivePath) {
    $version = [Diagnostics.FileVersionInfo]::GetVersionInfo((Join-Path $PSScriptRoot '..\build\PicRope.exe')).ProductVersion
    $ArchivePath = Join-Path $PSScriptRoot ('..\releases\PicRope-' + $version + '-windows-x64.zip')
}
Add-Type -AssemblyName System.IO.Compression.FileSystem
$archive = [IO.Compression.ZipFile]::OpenRead([IO.Path]::GetFullPath($ArchivePath))
try {
    $files = @($archive.Entries | Where-Object { $_.Name } | ForEach-Object { $_.FullName.Replace('\','/') })
    if ($files -match 'stale-review-fixture|(^|/)(data|test-data|EditorCache)/') { throw '发布包包含残留测试数据或个人缓存' }
    $expected = @(
        'Install.cmd','install.ps1','app/uninstall.ps1','app/update.ps1',
        'app/PicRope.exe','app/PicRope.exe.config','app/Microsoft.Web.WebView2.Core.dll',
        'app/Microsoft.Web.WebView2.Wpf.dll','app/WebView2Loader.dll','app/LICENSE','app/NOTICE',
        'app/editor/index.html','app/editor/editor.js','app/editor/editor.css',
        'app/editor/translations.js','app/editor/filerobot-image-editor.min.js',
        'app/third-party-notices/Filerobot/LICENSE','app/third-party-notices/Filerobot/THIRD-PARTY-NOTICES.txt',
        'app/third-party-notices/WebView2/LICENSE.txt','app/third-party-notices/WebView2/NOTICE.txt'
    )
    if (Compare-Object ($expected | Sort-Object) ($files | Sort-Object)) { throw '发布包文件清单不符合要求' }
    foreach ($entry in $archive.Entries | Where-Object { $_.Name -match '\.(ps1|cmd|js|css|html|config)$' }) {
        $reader = New-Object IO.StreamReader($entry.Open())
        try { $content = $reader.ReadToEnd() } finally { $reader.Dispose() }
        if ($content.Contains('PRIVATE_TEST_FIXTURE') -or $content -match '(?i)(?<![A-Z0-9_])[A-Z]:[\\/]|/(Users|home)/') {
            throw ('发布包包含个人路径或测试内容：' + $entry.FullName)
        }
    }
    Write-Output ('发布包检查通过：' + $files.Count + ' 个白名单文件，无测试数据、个人路径或浏览器缓存。')
}
finally { $archive.Dispose() }
