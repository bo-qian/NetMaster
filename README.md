# NetMaster：上海大学校园网自动登录软件

全自动管理 SHU 校园网登录，实现断网自动重连及开机自启，配置完成后即可后台静默运行。

## 版本

| 平台    | 目录         | 说明                        |
|---------|-------------|-----------------------------|
| Linux   | `linux/`    | systemd 守护 + TUI 终端面板 |
| Windows | `windows/`  | 任务计划 + PySide6 GUI      |

## Linux 版快速开始

```bash
cd linux && ./install.sh
```

然后打开新终端输入 `netmaster`，按 `[6]` 抓取凭证，按 `[1]` 启动守护。

详见 [linux/README.md](linux/README.md)。

## Windows 版快速开始

```bash
cd windows
pip install -r requirements.txt
python main.py
```

或直接下载 [Releases](../../releases) 中的 `NetMaster.exe`，双击运行。

详见 [windows/README.md](windows/README.md)。
