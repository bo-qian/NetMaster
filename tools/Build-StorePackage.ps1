param(
    [ValidateSet('x64', 'x86', 'ARM64')][string]$Architecture = 'x64',
    [switch]$AfterAcceptance
)
$ErrorActionPreference = 'Stop'
if (!$AfterAcceptance) { throw '先运行开发版并取得用户的界面与功能验收确认，再使用 -AfterAcceptance 打包。' }
$repoRoot = Split-Path $PSScriptRoot -Parent
$project = Join-Path $repoRoot 'windows/NetMaster.WinUI/NetMaster.WinUI.csproj'
$manifestPath = Join-Path $repoRoot 'windows/NetMaster.WinUI/Package.appxmanifest'
$identityPath = Join-Path $repoRoot 'windows/NetMaster.WinUI/StoreIdentity.json'
if (!(Test-Path -LiteralPath $identityPath -PathType Leaf)) {
    throw '尚未关联 Partner Center 的 Product identity：缺少 StoreIdentity.json。'
}
$storeIdentity = Get-Content -LiteralPath $identityPath -Raw | ConvertFrom-Json
if ([string]::IsNullOrWhiteSpace($storeIdentity.Name) -or
    [string]::IsNullOrWhiteSpace($storeIdentity.Publisher) -or
    [string]::IsNullOrWhiteSpace($storeIdentity.PublisherDisplayName) -or
    $storeIdentity.Name -eq '4e6619dc-7548-4e3f-8cb2-537e2dba258c' -or
    $storeIdentity.Publisher -eq 'CN=qianbo') {
    throw 'StoreIdentity.json 必须填写 Partner Center 的 Name、Publisher 和 PublisherDisplayName，不能使用开发占位值。'
}
[xml]$sourceManifest = Get-Content -LiteralPath $manifestPath -Raw
$families = @($sourceManifest.Package.Dependencies.TargetDeviceFamily | ForEach-Object { $_.Name })
if ($families.Count -ne 1 -or $families[0] -ne 'Windows.Desktop') {
    throw 'NetMaster 商店包必须仅声明 Windows.Desktop 设备家族。'
}
$sourceManifest.Package.Identity.Name = [string]$storeIdentity.Name
$sourceManifest.Package.Identity.Publisher = [string]$storeIdentity.Publisher
$storeVersion = '2.0.0.0'
$sourceManifest.Package.Identity.Version = $storeVersion
$sourceManifest.Package.Properties.PublisherDisplayName = [string]$storeIdentity.PublisherDisplayName
$identity = $sourceManifest.Package.Identity
$vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio/Installer/vswhere.exe'
if (!(Test-Path -LiteralPath $vswhere)) { throw '请安装 Visual Studio 的 WinUI 应用程序开发工作负载。' }
$msbuild = & $vswhere -latest -products '*' -requires Microsoft.Component.MSBuild -find 'MSBuild\**\Bin\MSBuild.exe' | Select-Object -First 1
if (!$msbuild) { throw '找不到 Visual Studio MSBuild。' }
$buildId = (Get-Date -Format 'yyyyMMdd-HHmmss') + '-' + [guid]::NewGuid().ToString('N').Substring(0, 8)
$output = (Join-Path $repoRoot "windows/NetMaster.WinUI/bin/store-$Architecture/$buildId/") + [IO.Path]::DirectorySeparatorChar
$packages = (Join-Path $repoRoot "windows/NetMaster.WinUI/bin/store-packages-$Architecture/$buildId/") + [IO.Path]::DirectorySeparatorChar
New-Item -ItemType Directory -Path $output -Force | Out-Null
$storeManifestPath = Join-Path $output 'Package.Store.appxmanifest'
$sourceManifest.Save($storeManifestPath)
& $msbuild $project /restore /nologo /v:minimal /p:Configuration=Release "/p:Platform=$Architecture" "/p:OutDir=$output" "/p:AppxPackageDir=$packages" "/p:NetMasterStoreManifest=$storeManifestPath" /p:GenerateAppxPackageOnBuild=true /p:AppxPackageSigningEnabled=false /p:AppxSymbolPackageEnabled=false /p:UapAppxPackageBuildMode=StoreUpload /p:AppxBundle=Never
if ($LASTEXITCODE -ne 0) { throw "MSIX 打包失败，退出码 $LASTEXITCODE。" }
$uploads = @(Get-ChildItem -LiteralPath $packages -Filter '*.msixupload' -Recurse -File)
if ($uploads.Count -ne 1) { throw "本次构建应生成一个商店上传文件，实际为 $($uploads.Count) 个。" }
$upload = $uploads[0]
Add-Type -AssemblyName System.IO.Compression.FileSystem
$msixes = @(Get-ChildItem -LiteralPath $packages -Filter '*.msix' -Recurse -File)
if ($msixes.Count -ne 1) { throw "本次构建应生成一个 MSIX 文件，实际为 $($msixes.Count) 个。" }
$msix = $msixes[0]
function Test-Payload([IO.Compression.ZipArchive]$archive) {
    foreach ($name in @('NetMaster.WinUI.exe', 'NetMaster.Worker.exe', 'NetMaster.Worker.dll', 'NetMaster.Worker.deps.json', 'NetMaster.Worker.runtimeconfig.json', 'NetMaster.Core.dll', 'System.Security.Cryptography.ProtectedData.dll', 'coreclr.dll', 'hostfxr.dll', 'hostpolicy.dll')) {
        if (!$archive.GetEntry($name)) { throw "MSIX 缺少运行文件：$name" }
    }
    $reader = [IO.StreamReader]::new($archive.GetEntry('AppxManifest.xml').Open())
    try { [xml]$manifest = $reader.ReadToEnd() } finally { $reader.Dispose() }
    if ($manifest.Package.Identity.Name -ne $identity.Name -or
        $manifest.Package.Identity.Publisher -ne $identity.Publisher -or
        $manifest.Package.Identity.Version -ne $storeVersion -or
        $manifest.Package.Properties.PublisherDisplayName -ne $sourceManifest.Package.Properties.PublisherDisplayName) {
        throw '生成的 MSIX 身份或版本与本次商店配置不一致。'
    }
    $packagedFamilies = @($manifest.Package.Dependencies.TargetDeviceFamily | ForEach-Object { $_.Name })
    if ($packagedFamilies.Count -ne 1 -or $packagedFamilies[0] -ne 'Windows.Desktop') {
        throw '生成的 MSIX 不是仅面向 Windows.Desktop。'
    }
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
Write-Host '此输出仅供 Partner Center 提交审核，不能当作已签名的公开安装包。'
