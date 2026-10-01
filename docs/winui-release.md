# WinUI 验证与发布

发布路线：Microsoft Store 的 MSIX。先完成软件验收，接近提交时再由用户完成个人注册和身份核验。当前 manifest 的 Identity / Publisher 是开发占位，未关联商店账号。

**最新用户要求：先看实际运行效果并验收功能，确认符合想法后再打包。** 已生成的本地未签名测试素材不发布、不安装，不作为验收成品；此后停止打包。CI 已改为只测试和构建开发版。

最新本机开发部署结果（2026-10-01）：用户要求实际解决预览后，已成功安装并运行自签名的 NetMaster.LocalDevelopment 测试 MSIX。SAC 保持开启。独立测试身份解决了同名 VS loose 注册导致的 0x80073CFB；原注册 / 数据保留。桌面和开始菜单入口为“NetMaster 开发测试”，用于本机验收。已查看三页、开始 / 取消配置及真实网页 / 日志，未完成真实认证保存或断网重连；不是正式发布。测试证书公钥已按系统管理员确认加入 TrustedPeople，私钥不可导出，30 天有效；证书与构建产物不提交 Git。不要再用旧的未打包 configuration-preview exe 作为本轮入口。

本机测试部署调研（2026-10-01）：微软推荐 MSIX 本地开发使用自签名证书与显式信任；这与正式发布签名是不同阶段。[官方指南](https://learn.microsoft.com/en-us/windows/msix/package/sign-msix-package-guide)。当前用户只要求调研，尚未生成 / 安装自签名测试包。另台电脑为远程使用，不安排其断网 / 注销测试。本机开发者模式已开启，SAC 仍启用；MSIX 能安装不等于所有组件能通过 SAC 运行检查。官方 SAC 签名要求采用受信任提供者证书，自签名不能作为本次拦截的确定修复。[SAC 签名要求](https://learn.microsoft.com/en-us/windows/apps/develop/smart-app-control/code-signing-for-smart-app-control)。

## 自动验证

在 Windows 仓库根目录执行：

```powershell
dotnet test windows/NetMaster.Core.Tests/NetMaster.Core.Tests.csproj -v:minimal
node --test tools/tests/portal-capture.test.cjs tools/tests/portal-layout.test.cjs
dotnet build windows/NetMaster.WinUI/NetMaster.WinUI.csproj -p:Platform=x64 -p:WindowsPackageType=None -p:GenerateAppxPackageOnBuild=false -p:OutDir=./bin/acceptance-preview/ -v:minimal
```

测试使用随机临时目录、虚构凭证与可控 HTTP / 网络服务；DPAPI 和当前用户命名管道使用真实 Windows API。不会访问学校认证接口、自动注销账号、修改用户自启或生产配置。测试通过不能代替真实校园网和安装流程验收。

打包脚本使用 Visual Studio MSBuild；当前开发机用 `dotnet build` 生成 StoreUpload 时，MSIX 工具出现符号转换及 System.Security.Permissions 依赖错误，使用 VS MSBuild 已通过。仍保持 .NET 8 与项目原有 Windows App SDK 版本。输出保存在被 Git 忽略的 `windows/NetMaster.WinUI/bin/store-packages-x64/`，包括 `.msixupload` 和未签名 `.msix`。脚本同时核对两个包内的 UI / Worker / Core、.NET 运行文件及 StartupTask 声明，缺失时构建失败。已修复初次打包遗漏 Worker.dll 的问题。不安装证书、不改安全策略、不自动提交商店。

GitHub Actions 配置仅测试和构建开发版，不打包、不创建 Release、不发布商店；远端 CI 尚未执行。

取得用户明确验收确认后才运行 `./tools/Build-StorePackage.ps1 -Architecture x64 -AfterAcceptance`；脚本未带确认参数会在构建前停止。这不是当前接续任务。

安装品牌素材来自仓库定稿 PNG / ICO，可用 `./tools/Generate-BrandAssets.ps1` 重新生成各尺寸磁贴及启动图。

## 发布前仍需实际验收

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

进入发布阶段由用户完成：个人开发者注册 / 身份核验、应用名称与商店产品身份；随后关联 manifest、准备隐私说明 / 商店截图和提交素材，再完成审核与正式 Release。个人资料只在微软官方注册页面提交，不发送给项目仓库或助手。

## 个人账号注册与签名说明（2026-10-01 核对）

- 从 [Microsoft Store 开发者入口](https://storedeveloper.microsoft.com/) 的新流程开始，选 Individual developer，使用个人 Microsoft 账号，按页面完成证件与自拍核验；该新流程目前免注册费。避免从旧 Partner Center 或 Visual Studio 注册入口进入旧流程。[微软个人账号步骤](https://learn.microsoft.com/en-us/windows/apps/publish/partner-center/open-a-developer-account)
- 账号就绪后，在 Partner Center 的 Apps & Games 中新建 MSIX/PWA 应用并预留可用的正式名称。2026-10-02 用户截图显示单独的 `NetMaster` 不可预留；先排查本账号是否已有此预留，再试带用途的名称（如 `NetMaster 校园网助手`），具体能否使用以 Partner Center 为准。名称确定后，在 Product identity 中获取商店分配的 Package/Identity/Name、Publisher、PublisherDisplayName，之后才关联项目 manifest。当前占位身份不能当作商店身份。[名称预留规则](https://learn.microsoft.com/en-us/windows/apps/publish/publish-your-app/msix/reserve-your-apps-name) · [商店身份字段](https://learn.microsoft.com/en-us/windows/apps/publish/view-app-identity-details)
- Store MSIX 通过审核后由微软重新签名；注册不会给开发者一张可导出、可用于任意站外包的签名证书。现有本机自签名开发测试证书也不是正式公开分发证书。若以后要在 GitHub Release 提供独立直装 MSIX，另需站外可信签名方案。[微软签名说明](https://learn.microsoft.com/en-us/windows/apps/publish/get-started)
- `.p12` / `.pfx` 是 PKCS#12 文件格式，可能包含证书和私钥，并不表示证书具有受信任的代码签名用途或属于本开发者。不购买来源不明的转售文件作为正式签名；公开代码签名应在需要站外直装时，按届时适用条件直接向可信 CA 或合规签名服务申请。当前 Store MSIX 路线无需购买证书。[PKCS#12 规范](https://www.rfc-editor.org/info/rfc7292/) · [微软签名选项](https://learn.microsoft.com/en-us/windows/apps/package-and-deploy/code-signing-options)
- 注册、预留名称可以先完成；实际关联身份、正式打包、上传与提交仍需先完成用户验收及发布前现场验证。个人证件、自拍和 Microsoft 账号凭据只在微软官方页面操作，不写入仓库。
