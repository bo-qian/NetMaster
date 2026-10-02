param(
    [string]$ReuseBackendFrom,
    [string]$BackendSourceCommit
)
$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path $PSScriptRoot -Parent
if ([bool]$ReuseBackendFrom -ne [bool]$BackendSourceCommit) {
    throw 'ReuseBackendFrom and BackendSourceCommit must be provided together.'
}
if ($ReuseBackendFrom) {
    $trustedBackend = (Resolve-Path -LiteralPath $ReuseBackendFrom).Path
    if (!(Test-Path -LiteralPath $trustedBackend -PathType Container)) { throw 'The trusted backend layout must be a directory.' }
    & git -C $repoRoot rev-parse --verify "$BackendSourceCommit^{commit}" *> $null
    if ($LASTEXITCODE -ne 0) { throw 'BackendSourceCommit is not a commit.' }
    foreach ($comparison in @(@('diff', '--quiet', $BackendSourceCommit, 'HEAD', '--', 'windows/NetMaster.Core', 'windows/NetMaster.Worker'),
                             @('diff', '--quiet', '--', 'windows/NetMaster.Core', 'windows/NetMaster.Worker'),
                             @('diff', '--cached', '--quiet', '--', 'windows/NetMaster.Core', 'windows/NetMaster.Worker'))) {
        & git -C $repoRoot @comparison
        if ($LASTEXITCODE -ne 0) { throw 'Backend source changed since the trusted build; rebuild and verify the backend.' }
    }
}
$project = Join-Path $repoRoot 'windows/NetMaster/NetMaster.csproj'
$manifestPath = Join-Path $repoRoot 'windows/NetMaster/Package.appxmanifest'
[xml]$manifest = Get-Content -LiteralPath $manifestPath -Raw
$publisher = [string]$manifest.Package.Identity.Publisher
$friendlyName = 'NetMaster local development test (30 days)'
$certificate = Get-ChildItem Cert:\CurrentUser\My -CodeSigningCert |
    Where-Object { $_.Subject -eq $publisher -and $_.FriendlyName -eq $friendlyName -and $_.HasPrivateKey -and $_.NotAfter -gt (Get-Date).AddDays(1) } |
    Sort-Object NotAfter -Descending | Select-Object -First 1
if (!$certificate) {
    $certificate = New-SelfSignedCertificate -Type Custom -Subject $publisher -FriendlyName $friendlyName `
        -CertStoreLocation 'Cert:\CurrentUser\My' -KeyUsage DigitalSignature -KeyAlgorithm RSA -KeyLength 2048 `
        -KeyExportPolicy NonExportable -NotAfter (Get-Date).AddDays(30) `
        -TextExtension @('2.5.29.37={text}1.3.6.1.5.5.7.3.3', '2.5.29.19={text}')
}
$vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio/Installer/vswhere.exe'
if (!(Test-Path -LiteralPath $vswhere)) { throw 'Visual Studio MSBuild is required.' }
$msbuild = & $vswhere -latest -products '*' -requires Microsoft.Component.MSBuild -find 'MSBuild\**\Bin\MSBuild.exe' | Select-Object -First 1
if (!$msbuild) { throw 'Visual Studio MSBuild was not found.' }
$developmentRoot = Join-Path $repoRoot 'windows/NetMaster/bin/local-development'
$output = (Join-Path $developmentRoot 'output') + '/'
$packages = (Join-Path $developmentRoot 'packages') + '/'
New-Item -ItemType Directory -Path $developmentRoot -Force | Out-Null
$certificatePath = Join-Path $developmentRoot 'NetMaster-development.cer'
Export-Certificate -Cert $certificate -FilePath $certificatePath -Force | Out-Null
# Locate SignTool before building so executable code is signed before MSIX
# computes the integrity catalog, rather than signing only the final container.
[xml]$projectXml = Get-Content -LiteralPath $project -Raw
$buildToolsVersion = ($projectXml.Project.ItemGroup.PackageReference | Where-Object Include -eq 'Microsoft.Windows.SDK.BuildTools' | Select-Object -First 1).Version
$nugetRoot = $env:NUGET_PACKAGES
if (!$nugetRoot) { $nugetRoot = Join-Path $env:USERPROFILE '.nuget/packages' }
$sdkBin = Join-Path $nugetRoot "microsoft.windows.sdk.buildtools/$buildToolsVersion/bin"
$sdkTools = Get-ChildItem -LiteralPath $sdkBin -Directory | Sort-Object Name -Descending |
    Where-Object { Test-Path -LiteralPath (Join-Path $_.FullName 'x64/makeappx.exe') } | Select-Object -First 1
if (!$sdkTools) { throw 'The referenced Windows SDK packaging tools were not found.' }
$makeappx = Join-Path $sdkTools.FullName 'x64/makeappx.exe'
$signtool = Join-Path $sdkTools.FullName 'x64/signtool.exe'
& $msbuild $project /restore /nologo /v:minimal /p:Configuration=Debug /p:Platform=x64 "/p:OutDir=$output" "/p:AppxPackageDir=$packages" `
    /p:GenerateAppxPackageOnBuild=true /p:AppxPackageSigningEnabled=true "/p:PackageCertificateThumbprint=$($certificate.Thumbprint)" `
    "/p:NetMasterDevelopmentCertificateThumbprint=$($certificate.Thumbprint)" "/p:NetMasterDevelopmentSignTool=$signtool" `
    /p:AppxSymbolPackageEnabled=false /p:UapAppxPackageBuildMode=SideloadOnly /p:AppxBundle=Never
if ($LASTEXITCODE -ne 0) { throw "Development package build failed: $LASTEXITCODE" }
$package = Get-ChildItem -LiteralPath $packages -Filter 'NetMaster_*.msix' -File -Recurse | Sort-Object LastWriteTime -Descending | Select-Object -First 1
if (!$package) { throw 'Development MSIX was not generated.' }
# Give the test installation its own identity so it can coexist with the
# Visual Studio loose registration. The repository's manifest stays unchanged.
$layout = Join-Path $developmentRoot ('layout-' + [guid]::NewGuid().ToString('N'))
& $makeappx unpack /p $package.FullName /d $layout /o *> (Join-Path $developmentRoot 'unpack.log')
if ($LASTEXITCODE -ne 0) { throw 'Development package extraction failed. See unpack.log.' }
foreach ($binary in @('NetMaster.exe', 'NetMaster.dll', 'NetMaster.Worker.exe', 'NetMaster.Worker.dll', 'NetMaster.Core.dll')) {
    $binaryPath = Join-Path $layout $binary
    $binarySignature = Get-AuthenticodeSignature -LiteralPath $binaryPath
    if ($binarySignature.Status -ne 'Valid' -or $binarySignature.SignerCertificate.Thumbprint -ne $certificate.Thumbprint) {
        throw "Development binary signing failed: $binary"
    }
    & $signtool verify /pa /c (Join-Path $layout 'AppxMetadata/CodeIntegrity.cat') $binaryPath
    if ($LASTEXITCODE -ne 0) { throw "Integrity catalog verification failed: $binary" }
}
$layoutManifestPath = Join-Path $layout 'AppxManifest.xml'
[xml]$layoutManifest = Get-Content -LiteralPath $layoutManifestPath -Raw
$layoutManifest.Package.Identity.Name = 'NetMaster.LocalDevelopment'
$installedDevelopment = Get-AppxPackage -Name NetMaster.LocalDevelopment
if ($installedDevelopment) {
    $installedVersion = [version]$installedDevelopment.Version
    if ($installedVersion.Revision -ge 65535) { throw 'Development version revision is exhausted.' }
    $layoutManifest.Package.Identity.Version = "$($installedVersion.Major).$($installedVersion.Minor).$($installedVersion.Build).$($installedVersion.Revision + 1)"
}
$layoutManifest.Package.Properties.DisplayName = 'NetMaster 开发测试'
foreach ($visual in $layoutManifest.SelectNodes("//*[local-name()='VisualElements']")) {
    $visual.SetAttribute('DisplayName', 'NetMaster 开发测试')
}
$layoutManifest.Save($layoutManifestPath)
foreach ($footprint in @('AppxSignature.p7x', 'AppxBlockMap.xml')) {
    $ownedFile = Join-Path $layout $footprint
    if (Test-Path -LiteralPath $ownedFile) { Remove-Item -LiteralPath $ownedFile }
}
if ($ReuseBackendFrom) {
    $trustedCatalog = Join-Path $trustedBackend 'AppxMetadata/CodeIntegrity.cat'
    foreach ($binary in @('NetMaster.Core.dll', 'NetMaster.Worker.exe', 'NetMaster.Worker.dll')) {
        $sourcePath = Join-Path $trustedBackend $binary
        if (!(Test-Path -LiteralPath $sourcePath)) { throw "Trusted backend is missing $binary" }
        & $signtool verify /pa /c $trustedCatalog $sourcePath *> $null
        if ($LASTEXITCODE -ne 0) { throw "Trusted backend catalog verification failed: $binary" }
        Copy-Item -LiteralPath $sourcePath -Destination (Join-Path $layout $binary) -Force
    }
    foreach ($data in @('NetMaster.Worker.deps.json', 'NetMaster.Worker.runtimeconfig.json')) {
        Copy-Item -LiteralPath (Join-Path $trustedBackend $data) -Destination (Join-Path $layout $data) -Force
    }
    $catalogPath = Join-Path $layout 'AppxMetadata/CodeIntegrity.cat'
    Remove-Item -LiteralPath $catalogPath
    $newCatalog = Join-Path $developmentRoot ('CodeIntegrity-' + [guid]::NewGuid().ToString('N') + '.cat')
    New-FileCatalog -Path $layout -CatalogFilePath $newCatalog -CatalogVersion 2.0 | Out-Null
    & $signtool sign /fd SHA256 /sha1 $certificate.Thumbprint $newCatalog *> $null
    if ($LASTEXITCODE -ne 0) { throw 'Updated development catalog signing failed.' }
    Copy-Item -LiteralPath $newCatalog -Destination $catalogPath
    foreach ($binary in @('NetMaster.exe', 'NetMaster.dll', 'NetMaster.Core.dll', 'NetMaster.Worker.exe', 'NetMaster.Worker.dll')) {
        & $signtool verify /pa /c $catalogPath (Join-Path $layout $binary) *> $null
        if ($LASTEXITCODE -ne 0) { throw "Updated catalog verification failed: $binary" }
    }
}
$isolatedPackagePath = Join-Path $developmentRoot 'NetMaster.LocalDevelopment.msix'
& $makeappx pack /d $layout /p $isolatedPackagePath /o *> (Join-Path $developmentRoot 'pack.log')
if ($LASTEXITCODE -ne 0) { throw 'Development package creation failed. See pack.log.' }
& $signtool sign /fd SHA256 /sha1 $certificate.Thumbprint $isolatedPackagePath
if ($LASTEXITCODE -ne 0) { throw 'Development package signing failed.' }
$package = Get-Item -LiteralPath $isolatedPackagePath
$signature = Get-AuthenticodeSignature -LiteralPath $package.FullName
if (!$signature.SignerCertificate -or $signature.SignerCertificate.Thumbprint -ne $certificate.Thumbprint) { throw 'Package signer does not match the development certificate.' }
Add-Type -AssemblyName System.IO.Compression.FileSystem
$archive = [IO.Compression.ZipFile]::OpenRead($package.FullName)
try {
    foreach ($name in @('NetMaster.exe', 'NetMaster.dll', 'NetMaster.Worker.exe', 'NetMaster.Worker.dll', 'NetMaster.Core.dll', 'coreclr.dll', 'AppxSignature.p7x')) {
        if (!$archive.GetEntry($name)) { throw "Development package is missing $name" }
    }
} finally { $archive.Dispose() }
$metadata = [ordered]@{
    PackagePath = $package.FullName
    PackageSha256 = (Get-FileHash -LiteralPath $package.FullName -Algorithm SHA256).Hash
    CertificatePath = $certificatePath
    CertificateThumbprint = $certificate.Thumbprint
    Publisher = $publisher
    PackageName = 'NetMaster.LocalDevelopment'
    Expires = $certificate.NotAfter.ToString('o')
}
if ($ReuseBackendFrom) {
    $metadata.BackendSourceCommit = $BackendSourceCommit
    $metadata.BackendCoreSha256 = (Get-FileHash -LiteralPath (Join-Path $layout 'NetMaster.Core.dll') -Algorithm SHA256).Hash
    $metadata.BackendWorkerSha256 = (Get-FileHash -LiteralPath (Join-Path $layout 'NetMaster.Worker.dll') -Algorithm SHA256).Hash
}
$metadata | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $developmentRoot 'deployment.json') -Encoding UTF8
Write-Host 'Development-only signed package prepared. No trust store changes or installation performed.'
$metadata | ConvertTo-Json
