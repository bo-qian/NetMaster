# NetMaster Windows 验证与发布

**2026-10-03 认证结果：** Partner Center 产品总览显示 Submission 1“准备发布”，认证已通过，仍需点击“立即发布”才开始公开发布流程；该按钮未点击。认证包是下文记录的重命名前 `NetMaster.WinUI_2.0.0.0_x64`，不同于当前 `main` 的 `NetMaster.exe` 与数据迁移实现。当前新名称的开发 MSIX 已构建、验签，但尚未实际安装运行。建议先验收新构建，再备妥替换包、处理旧待发布提交并重新认证；旧提交暂不取消，公开发布与 GitHub Release 暂不执行。下文的“正在认证”表述保留为 2026-10-02 当时记录。

**2026-10-02 重命名提示：** 当前源码项目与程序集已更名为 `windows/NetMaster/NetMaster.csproj`、`NetMaster.exe`，默认数据目录改为 `%LocalAppData%/NetMaster` 并迁移上一代 Windows 应用数据。x64 无包构建、Core 与网页脚本测试通过；独立自签名开发测试 MSIX 已生成并核查签名及包内文件，尚未安装运行。下面记载的已提交包路径与哈希是历史事实，仍属当前正在认证的旧包。新版须取得用户对实际运行的确认，再构建并验证新的 Store 包，才能替换送审包；商店 Name `BoQian.NetMaster`、Publisher 和已预留显示名不改。此时尚未替换包、认证未通过，也未手动发布。

发布路线：Microsoft Store 的 MSIX。用户已在 Partner Center 建立 `NetMaster 校园网助手` 产品，并于 2026-10-02 确认当前开发版的本地验收没有问题。用户审阅 Submission 1 后明确授权送审；Partner Center 最近一次核对显示**正在认证**，提交步骤完成、预处理正在进行（4 步中的第 2 步）。用户另已决定立即合并 WinUI 源码，`feature/winui3-windows` 快进合并并推送到 `main`；后续审核修订在主线进行。正式商店发布仍设为手动，尚未公开上架或创建 GitHub Release。个人身份核验具体状态未独立检查。

**首发版本：** 用户指定 WinUI 新版对外以 **v2.0.0** 上线，与 Python 旧版 1.0.0 区分。Microsoft Store 候选 MSIX 的四段包版本固定为 **2.0.0.0**，第四段为 0；GitHub Release 标记和版本说明使用 **v2.0.0**。源码 manifest 与已安装的 `NetMaster.LocalDevelopment 1.0.0.37` 属于独立开发身份，不据此判断正式包的版本。后续更新必须递增商店包版本，不能重复提交相同版本。

当前顺序：等待微软完成预处理和认证，按审核反馈补充资料或修复 → 审核通过后由用户决定手动发布 → 核对正式商店安装链接与实际安装运行 → 创建 GitHub `v2.0.0` Release。已准备 [Release 文案草稿](release-notes-v2.0.0-draft.md)；不得在商店可安装前将其作为正式下载公告。用户验收的是当前开发运行版；真实断网恢复、Windows 登录后自启、升级 / 卸载及新构建后台组件的运行策略仍未独立验证，不将构建或商店包预检等同于这些检查通过。

**已上传的候选包（2026-10-02）：** 产物位于本机忽略目录 `windows/NetMaster.WinUI/bin/store-packages-x64/20261002-140252-691be6ba/NetMaster.WinUI_2.0.0.0_x64.msixupload`，大小 75,749,548 字节，SHA-256 `CFBAC9323D3525C3631682CF640209CCE085BEE94EF4F6929D45539B8D118D1C`。首包因安装显示名 `NetMaster` 未预留被预检拒绝；商店专用清单现将 Properties 和 VisualElements 的 DisplayName 均设为已预留的 `NetMaster 校园网助手`，重建后在 Partner Center 显示包验证完成。构建脚本仍核查 Name `BoQian.NetMaster`、Publisher `CN=3A8E9608-D9F3-48BD-BC94-F877AEC6081D`、PublisherDisplayName `Bo Qian`、Version `2.0.0.0`、Desktop 家族、运行文件与 StartupTask。源码开发身份未改。此上传包未在本机安装或运行，不能据此声称新构建后台通过 Smart App Control。

**Submission 1 送审内容：** 用户确定免费、全球所有市场、审核通过后手动发布。属性填为实用工具 / 工具，隐私说明采用 [本仓库说明](privacy-policy.md) 的正文，网站与支持入口指向本项目仓库和 Issues。IARC 问卷按工具实际功能填写，用户阅读并同意条款后已保存；重新进入分级详情页确认 IARC 分级 ID 已生成，全球 3+。简体中文和英语（美国）列表均已填写介绍、说明和 6 条功能要点，且各上传一张获用户同意的真实日志页截图。认证说明如实指出学校门户只在对应校园网络可用，未提供私人测试账号。`runFullTrust` 用途说明已填写。提交总览加载稳定后送审按钮可用；用户明确同意后已送审。Partner Center 显示“正在认证”，尚无审核结果。

**验收关口：** 用户此前要求先看实际运行效果，确认后再打包；2026-10-02 已明确确认当前开发版验收没有问题。`-AfterAcceptance` 现可用于候选包，但脚本会在缺少商店身份文件或其内容仍为占位时拒绝打包。此前生成的未签名测试素材不发布、不安装。CI 仍只测试和构建开发版。

本机开发部署历史（2026-10-01）：已成功安装并运行自签名的 NetMaster.LocalDevelopment 测试 MSIX。SAC 保持开启。独立测试身份解决了同名 VS loose 注册导致的 0x80073CFB；原注册 / 数据保留。桌面和开始菜单入口为“NetMaster 开发测试”，用于本机验收。测试证书公钥已按系统管理员确认加入 TrustedPeople，私钥不可导出，30 天有效；证书与构建产物不提交 Git。其后用户已确认当前开发版可进入发布准备，具体边界测试仍按上文标注。

本机测试部署调研（2026-10-01）：微软推荐 MSIX 本地开发使用自签名证书与显式信任；这与正式发布签名是不同阶段。[官方指南](https://learn.microsoft.com/en-us/windows/msix/package/sign-msix-package-guide)。当前用户只要求调研，尚未生成 / 安装自签名测试包。另台电脑为远程使用，不安排其断网 / 注销测试。本机开发者模式已开启，SAC 仍启用；MSIX 能安装不等于所有组件能通过 SAC 运行检查。官方 SAC 签名要求采用受信任提供者证书，自签名不能作为本次拦截的确定修复。[SAC 签名要求](https://learn.microsoft.com/en-us/windows/apps/develop/smart-app-control/code-signing-for-smart-app-control)。

## 自动验证

在 Windows 仓库根目录执行：

```powershell
dotnet test windows/NetMaster.Core.Tests/NetMaster.Core.Tests.csproj -v:minimal
node --test tools/tests/portal-capture.test.cjs tools/tests/portal-layout.test.cjs
dotnet build windows/NetMaster/NetMaster.csproj -p:Platform=x64 -p:WindowsPackageType=None -p:GenerateAppxPackageOnBuild=false -p:OutDir=./bin/acceptance-preview/ -v:minimal
```

测试使用随机临时目录、虚构凭证与可控 HTTP / 网络服务；DPAPI 和当前用户命名管道使用真实 Windows API。不会访问学校认证接口、自动注销账号、修改用户自启或生产配置。测试通过不能代替真实校园网和安装流程验收。

打包脚本使用 Visual Studio MSBuild；当前开发机用 `dotnet build` 生成 StoreUpload 时，MSIX 工具出现符号转换及 System.Security.Permissions 依赖错误，使用 VS MSBuild 已通过。仍保持 .NET 8 与项目原有 Windows App SDK 版本。输出保存在被 Git 忽略的 `windows/NetMaster/bin/store-packages-x64/`，包括 `.msixupload` 和未签名 `.msix`。脚本同时核对两个包内的 UI / Worker / Core、.NET 运行文件及 StartupTask 声明，缺失时构建失败。已修复初次打包遗漏 Worker.dll 的问题。不安装证书、不改安全策略、不自动提交商店。

GitHub Actions 配置仅测试和构建开发版，不打包、不创建 Release、不发布商店；远端 CI 尚未执行。

用户已明确确认验收；四个商店专用字段已写入 `windows/NetMaster/StoreIdentity.json`，并通过 `./tools/Build-StorePackage.ps1 -Architecture x64 -AfterAcceptance` 生成上述候选包。脚本未带确认参数、缺少身份文件或使用占位身份都会在构建前停止。它从源码 manifest 生成商店专用副本，不覆盖本地开发身份；每次使用独立输出目录避免误取旧包，并核查包内身份、显示名、Desktop 设备家族和必要运行文件。候选 `.msixupload` 只供 Partner Center 审核，不是已签名的站外安装包。

安装品牌素材来自仓库定稿 PNG / ICO，可用 `./tools/Generate-BrandAssets.ps1` 重新生成各尺寸磁贴及启动图。

## 发布前仍需独立核查的边界流程

| 流程 | 当前证据 | 仍需验证 |
|---|---|---|
| 配置与凭证 | 自动测试：DPAPI 往返、磁盘不含明文、损坏配置备份、失败保存不改变原配置 | 应用保存、退出、重开；不同 Windows 用户读取失败提示 |
| 检测与守护 | 可控测试：HTTP 状态 / 内容、认证重定向、并发查询、暂停取消、拒绝凭证停重试、设置唤醒 | 真实断网恢复、非校园网、主窗口关闭后的独立守护、进程单实例、休眠恢复 |
| 校园网登录 | 协议解析与限定捕获规则测试通过；WebView2 代码已编译 | 网页请求内容可读、成功 / 拒绝 / 已在线、queryString 与 Cookie 是否可独立复用、连续登录账号不混用 |
| 日志 | 保留、部分行、追加 / 截断、清除及缓存测试通过 | 实时开关、自动滚动关闭后的阅读、筛选、选中详情、复制与导出、大量记录与小窗口 |
| 测速 | 可控测试：完整 / 短响应 / 取消，缺失结果保持空值 | Cloudflare 实际可达性、测速耗时、取消和部分结果、不同窗口大小的数值显示 |
| 安装与自启 | x64 Release 和未签名 MSIX / 上传素材生成 | 合法签名 / 授权开发环境下安装、StartupTask、关闭自启、升级与卸载；x86 / ARM64 尚未构建验收 |

历史未打包预览确曾被 SAC 阻止。当前标准自签名本机开发 MSIX 1.0.0.19 正常安装运行，三个应用 DLL 与最终构建一致，SAC 保持开启；65 项 Core 与 11 项网页脚本测试通过。配置状态已统一，确认重配后清除旧登录信息、不留取消备份。应用真实下线 / 登录 / 新流程保存、断网恢复、登录后启动与用户完整验收仍待完成；本轮 UI 实查未提交真实认证或清除用户已保存凭证。仅用于开发验收，未正式发布。

## Store 与 GitHub Release

Microsoft Store 会在审核后为商店分发包重新签名。源码仓库生成的未签名 MSIX / msixupload 不是可直接公开安装的成品。[微软分发说明](https://learn.microsoft.com/en-us/windows/apps/package-and-deploy/choose-distribution-path)

上架后，符合条件的免费 MSIX 应用可以使用 Microsoft Store Web Installer：从产品网页或 Direct Store badge 下载轻量安装器，由 Store 下载并安装正式包。GitHub Release 可提供正式商店 / 官方安装入口；是否适合把安装器文件作为附件，需在产品上架后核对分发方式。独立离线 MSIX 附件的有效签名仍是单独的发布条件，不承诺商店自动给任意站外包签名。[官方安装器说明](https://learn.microsoft.com/en-us/windows/apps/distribute-through-store/how-to-use-store-web-installer-for-distribution)

Submission 1 已包含通过预检的 2.0.0.0 候选上传包、隐私说明、双语列表和一张各语言共用的真实日志页截图；用户审阅后已提交认证。个人资料只在微软官方页面提交，不发送给项目仓库或助手。学校允许的专用审核环境 / 账号尚未具备，认证可能要求补充；不使用个人真实校园网凭证。

## 个人账号注册与签名说明（2026-10-01 核对）

- 从 [Microsoft Store 开发者入口](https://storedeveloper.microsoft.com/) 的新流程开始，选 Individual developer，使用个人 Microsoft 账号，按页面完成证件与自拍核验；该新流程目前免注册费。避免从旧 Partner Center 或 Visual Studio 注册入口进入旧流程。[微软个人账号步骤](https://learn.microsoft.com/en-us/windows/apps/publish/partner-center/open-a-developer-account)
- 用户已在 Partner Center 的 Apps & Games 中建成 MSIX/PWA 草稿产品 `NetMaster 校园网助手`（截图日期 2026-10-02）；单独 `NetMaster` 此前不可预留。商店产品名与安装后的包显示名分属不同字段；微软建议两者一致以免困惑，应用内仍可使用 NetMaster 品牌。Product identity 三字段已写入独立 StoreIdentity.json；源码 manifest 的本地开发身份不当作商店身份。[名称预留规则](https://learn.microsoft.com/en-us/windows/apps/publish/publish-your-app/msix/reserve-your-apps-name) · [列表与安装名](https://learn.microsoft.com/en-us/windows/apps/publish/publish-your-app/msix/add-and-edit-store-listing-info) · [商店身份字段](https://learn.microsoft.com/en-us/windows/apps/publish/view-app-identity-details)
- Store MSIX 通过审核后由微软重新签名；注册不会给开发者一张可导出、可用于任意站外包的签名证书。现有本机自签名开发测试证书也不是正式公开分发证书。若以后要在 GitHub Release 提供独立直装 MSIX，另需站外可信签名方案。[微软签名说明](https://learn.microsoft.com/en-us/windows/apps/publish/get-started)
- `.p12` / `.pfx` 是 PKCS#12 文件格式，可能包含证书和私钥，并不表示证书具有受信任的代码签名用途或属于本开发者。不购买来源不明的转售文件作为正式签名；公开代码签名应在需要站外直装时，按届时适用条件直接向可信 CA 或合规签名服务申请。当前 Store MSIX 路线无需购买证书。[PKCS#12 规范](https://www.rfc-editor.org/info/rfc7292/) · [微软签名选项](https://learn.microsoft.com/en-us/windows/apps/package-and-deploy/code-signing-options)
- 用户已确认本地验收并授权提交；x64 候选包已上传并通过包预检，Submission 1 正在微软认证流程中。个人证件、自拍和 Microsoft 账号凭据只在微软官方页面操作，不写入仓库。
