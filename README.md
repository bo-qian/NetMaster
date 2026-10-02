# NetMaster

上海大学校园网连接管理工具。Windows 新版使用 WinUI 3，Linux 版继续保留。

Windows WinUI 版提供校园网登录信息配置、断线自动恢复、登录后启动、网络测速和日志查看。联网状态与后台守护状态分别显示；登录信息按当前 Windows 用户加密保存。

## 使用说明

| 版本 | 说明 | 状态 |
| --- | --- | --- |
| [Windows 新版](windows/README.md) | 配置校园网、查看连接与守护状态、测速、查阅日志 | v2.0.0 商店认证中，尚未公开上架 |
| [Windows 旧版](windows/legacy/README.md) | Python 版本的使用说明 | 保留供旧用户参考 |
| [Linux 版](linux/README.md) | systemd 守护与终端界面 | 独立维护 |

Windows 新版不会自动导入旧版的登录信息或日志，切换时需要重新配置。v2.0.0 已提交 Microsoft Store 认证，审核通过后仍由开发者手动发布；当前没有公开的新版安装入口。仓库历史 Release 中的 `NetMaster.exe` 属于 Python 旧版，下载时请核对版本说明。

## Star History

<a href="https://www.star-history.com/?repos=bo-qian%2FNetMaster&amp;type=date&amp;legend=top-left">
  <picture>
    <source media="(prefers-color-scheme: dark)" srcset="https://api.star-history.com/chart?repos=bo-qian/NetMaster&amp;type=date&amp;theme=dark&amp;legend=top-left" />
    <img alt="NetMaster Star History" src="https://api.star-history.com/chart?repos=bo-qian/NetMaster&amp;type=date&amp;legend=top-left" />
  </picture>
</a>

## 开源协议

本项目采用 [MIT License](LICENSE) 开源。使用、修改和分发本项目时，请保留协议中的版权声明和许可声明。
