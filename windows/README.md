# NetMaster Windows 版（WinUI 3）

这里是 Windows 后续发布的主线实现。界面使用 WinUI 3，`NetMaster.Worker` 是独立后台守护进程，`NetMaster.Core` 承载网络检测、配置和日志等逻辑。当前仍处于开发验收阶段，正式 Microsoft Store 安装包尚未发布。

## 项目

| 路径 | 内容 |
| --- | --- |
| [`NetMaster.WinUI/`](NetMaster.WinUI/) | 界面与 MSIX 包项目 |
| [`NetMaster.Worker/`](NetMaster.Worker/) | 关闭主窗口后继续运行的守护进程 |
| [`NetMaster.Core/`](NetMaster.Core/) | 两个进程共用的业务逻辑 |
| [`NetMaster.Core.Tests/`](NetMaster.Core.Tests/) | 业务逻辑测试 |
| [`legacy/`](legacy/README.md) | 保留的 Python Windows 旧版及其独立说明 |

## 开发与验证

在 Windows 安装 Visual Studio 的 WinUI 应用程序开发工作负载和 WebView2 Runtime，打开 [`NetMaster.WinUI/NetMaster.WinUI.slnx`](NetMaster.WinUI/NetMaster.WinUI.slnx)，以 x64 构建和调试 WinUI 项目。也可在仓库根目录运行：

```powershell
dotnet test windows/NetMaster.Core.Tests/NetMaster.Core.Tests.csproj -v:minimal
dotnet build windows/NetMaster.WinUI/NetMaster.WinUI.csproj -p:Platform=x64 -v:minimal
```

开发版构建通过不代表已经完成安装、校园网登录、断网恢复或后台启动验收。正式发布步骤见 [`docs/winui-release.md`](../docs/winui-release.md)。WinUI 默认数据位于当前用户的 `LocalAppData/NetMaster/WinUI`；它不会读取旧版 Python 的凭证或日志。
