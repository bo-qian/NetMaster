param(
    [ValidateSet('x64', 'x86', 'ARM64')][string]$Architecture = 'x64',
    [switch]$AfterAcceptance
)
$ErrorActionPreference = 'Stop'
if (!$AfterAcceptance) { throw '先运行开发版并取得用户的界面与功能验收确认，再使用 -AfterAcceptance 打包。' }
$repoRoot = Split-Path $PSScriptRoot -Parent
$project = Join-Path $repoRoot 'windows/NetMaster.WinUI/NetMaster.WinUI.csproj'
$vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio/Installer/vswhere.exe'
if (!(Test-Path -LiteralPath $vswhere)) { throw '请安装 Visual Studio 的 WinUI 应用程序开发工作负载。' }
$msbuild = & $vswhere -latest -products '*' -requires Microsoft.Component.MSBuild -find 'MSBuild\**\Bin\MSBuild.exe' | Select-Object -First 1
if (!$msbuild) { throw '找不到 Visual Studio MSBuild。' }
$output = (Join-Path $repoRoot "windows/NetMaster.WinUI/bin/store-$Architecture/") + [IO.Path]::DirectorySeparatorChar
$packages = (Join-Path $repoRoot "windows/NetMaster.WinUI/bin/store-packages-$Architecture/") + [IO.Path]::DirectorySeparatorChar
& $msbuild $project /restore /nologo /v:minimal /p:Configuration=Release "/p:Platform=$Architecture" "/p:OutDir=$output" "/p:AppxPackageDir=$packages" /p:GenerateAppxPackageOnBuild=true /p:AppxPackageSigningEnabled=false /p:AppxSymbolPackageEnabled=false /p:UapAppxPackageBuildMode=StoreUpload /p:AppxBundle=Never
if ($LASTEXITCODE -ne 0) { throw "MSIX 打包失败，退出码 $LASTEXITCODE。" }
$upload = Get-ChildItem -LiteralPath $packages -Filter '*.msixupload' -Recurse -File | Select-Object -First 1
if (!$upload) { throw '构建未生成商店上传文件。' }
Add-Type -AssemblyName System.IO.Compression.FileSystem
$msix = Get-ChildItem -LiteralPath $packages -Filter '*.msix' -Recurse -File | Select-Object -First 1
if (!$msix) { throw '构建未生成 MSIX 文件。' }
function Test-Payload([IO.Compression.ZipArchive]$archive) {
    foreach ($name in @('NetMaster.WinUI.exe', 'NetMaster.Worker.exe', 'NetMaster.Worker.dll', 'NetMaster.Worker.deps.json', 'NetMaster.Worker.runtimeconfig.json', 'NetMaster.Core.dll', 'System.Security.Cryptography.ProtectedData.dll', 'coreclr.dll', 'hostfxr.dll', 'hostpolicy.dll')) {
        if (!$archive.GetEntry($name)) { throw "MSIX 缺少运行文件：$name" }
    }
    $reader = [IO.StreamReader]::new($archive.GetEntry('AppxManifest.xml').Open())
    try { [xml]$manifest = $reader.ReadToEnd() } finally { $reader.Dispose() }
    $startup = $manifest.SelectSingleNode("//*[local-name()='StartupTask']")
    if (!$startup -or $startup.TaskId -ne 'NetMasterGuardian' -or $startup.ParentNode.Executable -ne 'NetMaster.Worker.exe') { throw 'MSIX 后台启动声明不完整。' }
    if ($archive.GetEntry('AppxSignature.p7x')) { throw '当前脚本应生成未签名的提交素材，请核对打包设置。' }
}
$archive = [IO.Compression.ZipFile]::OpenRead($msix.FullName)
try { Test-Payload $archive }
finally { $archive.Dispose() }
$container = [IO.Compression.ZipFile]::OpenRead($upload.FullName)
try {
    $entries = @($container.Entries | Where-Object { $_.FullName.EndsWith('.msix') })
    if ($entries.Count -ne 1) { throw '商店上传文件内应包含一个 MSIX。' }
    $inner = [IO.Compression.ZipArchive]::new($entries[0].Open(), [IO.Compression.ZipArchiveMode]::Read, $false)
    try { Test-Payload $inner } finally { $inner.Dispose() }
}
finally { $container.Dispose() }
Write-Host '已核对 MSIX 和商店上传包内的 UI、后台、Core、.NET 运行文件与启动声明。'
Write-Host "已生成未签名的商店提交素材：$($upload.FullName)"
Write-Host '发布前需关联 Partner Center 身份并完成运行验收；此输出不能当作已签名的公开安装包。'
