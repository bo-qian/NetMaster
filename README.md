# NetMaster

上海大学校园网连接管理工具。Windows 版正在从 Python 旧版迁移到 WinUI 3；Linux 版继续保留。

Windows WinUI 版提供校园网登录信息配置、断线自动恢复、登录后启动、网络测速和日志查看。联网状态与后台守护状态分别显示；配置仅保存在当前 Windows 用户的数据目录中。

## 项目结构

| 目录 | 内容 | 状态 |
| --- | --- | --- |
| [`windows/`](windows/README.md) | WinUI 3 应用、独立守护进程、业务代码及测试 | Windows 后续发布主线；正式版尚未发布 |
| [`windows/legacy/`](windows/legacy/README.md) | Python + PySide6 旧版 Windows 源码与说明 | 保留供旧用户和源码参考，不再作为新版构建入口 |
| [`linux/`](linux/README.md) | systemd 守护与 TUI | 独立维护 |

WinUI 版与旧 Windows 版使用独立的数据目录，不会自动导入旧版配置。后续正式版计划通过 Microsoft Store 分发；仓库现有的历史 Release 若包含 `NetMaster.exe`，它属于 Python 旧版。正式版尚在验收，发布计划见[说明](docs/winui-release.md)。

## 从源码运行

- **Windows WinUI 开发版：**参见 [windows/README.md](windows/README.md)。需要 Windows、Visual Studio 的 WinUI 工作负载及 WebView2 Runtime。
- **Windows Python 旧版：**参见 [windows/legacy/README.md](windows/legacy/README.md)。
- **Linux：**参见 [linux/README.md](linux/README.md)。

## Star History

<a href="https://www.star-history.com/?repos=bo-qian%2FNetMaster&amp;type=date&amp;legend=top-left">
  <picture>
    <source media="(prefers-color-scheme: dark)" srcset="https://api.star-history.com/chart?repos=bo-qian/NetMaster&amp;type=date&amp;theme=dark&amp;legend=top-left" />
    <img alt="NetMaster Star History" src="https://api.star-history.com/chart?repos=bo-qian/NetMaster&amp;type=date&amp;legend=top-left" />
  </picture>
</a>

## 开源协议

本项目采用 [MIT License](LICENSE) 开源。使用、修改和分发本项目时，请保留协议中的版权声明和许可声明。
