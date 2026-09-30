# WinUI 验证与发布

发布路线：Microsoft Store 的 MSIX。先完成软件验收，接近提交时再由用户完成个人注册和身份核验。当前 manifest 的 Identity / Publisher 是开发占位，未关联商店账号。

**最新用户要求：先看实际运行效果并验收功能，确认符合想法后再打包。** 已生成的本地未签名测试素材不发布、不安装，不作为验收成品；此后停止打包。CI 已改为只测试和构建开发版。

## 自动验证

在 Windows 仓库根目录执行：

```powershell
dotnet test windows/NetMaster.Core.Tests/NetMaster.Core.Tests.csproj -v:minimal
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

本机先前 Smart App Control 阻止旧业务预览加载未签名 Core DLL。没有关闭保护或重复尝试绕过。随后用户正常打开 acceptance-preview，反馈当前已连接。最新配置流程使用单独 configuration-preview 输出；本轮实际启动尝试被新 UI.dll 的签名策略阻止（CodeIntegrity 3077 / 3033、.NET Runtime 0x800711C7），没有取得新页面截图。两版运行证据分别记录：旧版能启动不代表新编译组件必然被允许，新版启动受阻也不否定旧版用户反馈。新配置流程、现场认证重配 / 断网重连与签名安装仍需验收。

## Store 与 GitHub Release

Microsoft Store 会在审核后为商店分发包重新签名。源码仓库生成的未签名 MSIX / msixupload 不是可直接公开安装的成品。[微软分发说明](https://learn.microsoft.com/en-us/windows/apps/package-and-deploy/choose-distribution-path)

上架后，符合条件的免费 MSIX 应用可以使用 Microsoft Store Web Installer：从产品网页或 Direct Store badge 下载轻量安装器，由 Store 下载并安装正式包。GitHub Release 可提供正式商店 / 官方安装入口；是否适合把安装器文件作为附件，需在产品上架后核对分发方式。独立离线 MSIX 附件的有效签名仍是单独的发布条件，不承诺商店自动给任意站外包签名。[官方安装器说明](https://learn.microsoft.com/en-us/windows/apps/distribute-through-store/how-to-use-store-web-installer-for-distribution)

进入发布阶段再请用户完成：个人开发者注册 / 身份核验、应用名称与商店产品身份；随后关联 manifest、准备隐私说明 / 商店截图和提交素材，再完成审核与正式 Release。当前不需要提交个人资料。
