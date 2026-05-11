# NetMaster Linux 版

上海大学校园网自动登录守护程序（Linux 版）。

## 一键安装

```bash
git clone git@github.com:bo-qian/NetMaster.git
cd NetMaster/linux
./install.sh
```

安装完成后，新终端输入 `netmaster` 打开控制面板。

## 使用流程

1. **抓取凭证** — 在 TUI 面板按 `[6]`，打开 Firefox 手动登录一次，自动捕获登录请求体
2. **测试登录** — 按 `[5]` 验证凭证有效
3. **启动守护** — 按 `[1]`，后台每 20 秒检测一次网络，断网自动重连

## 文件说明

```
~/.netmaster/
├── netmaster_daemon.py       # 守护进程（核心）
├── netmaster_tui.py          # 终端交互面板
├── capture_payload.py        # 凭证抓取工具
├── daemon_config.json        # 登录配置（含加密密码）
├── ctl.sh                    # 命令行管理脚本
├── geckodriver               # Firefox WebDriver
└── logs/                     # 运行日志
```

## 管理命令

```bash
netmaster                    # 打开 TUI 控制面板
~/.netmaster/ctl.sh start    # 启动守护
~/.netmaster/ctl.sh stop     # 停止守护
~/.netmaster/ctl.sh logs     # 实时日志
```

## 依赖

- Python 3.8+
- `requests` 库
- Firefox + geckodriver（仅抓包时需要）
