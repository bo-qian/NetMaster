# NetMaster：上海大学校园网自动登录软件

全自动管理 SHU 校园网登录，实现断网自动重连及开机自启，配置完成后即可后台静默运行。

## 功能亮点

- **断网自动重连** — 每 20 秒检测网络状态，断网立即自动登录
- **开机自启** — Linux systemd 守护 / Windows 任务计划，无需手动干预
- **加密凭证存储** — 不存明文密码，抓取浏览器加密后的 POST 请求体
- **统一日志文件** — 所有记录写入单一 `netmaster.log`，带完整时间戳，支持关键词搜索回溯断网历史
- **彩虹 ASCII 艺术 Banner** — 终端自适应宽度，宽屏显示彩色 Unicode 实心大字

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

> 建议终端宽度 ≥ 111 列以显示完整 banner，窄屏会自动降级为精简版。

详见 [linux/README.md](linux/README.md)。

## Windows 版快速开始

```bash
cd windows
pip install -r requirements.txt
python main.py
```

或直接下载 [Releases](../../releases) 中的 `NetMaster.exe`，双击运行。

详见 [windows/README.md](windows/README.md)。
