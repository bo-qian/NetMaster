# NetMaster Linux 版

上海大学校园网自动登录守护程序（Linux 版）。

## 一键安装

```bash
git clone git@github.com:bo-qian/NetMaster.git
cd NetMaster/linux
./install.sh
```

安装脚本自动适配主流 Linux 发行版（Fedora / Ubuntu / Debian / Arch 等），安装完成后新终端输入 `netmaster` 打开控制面板。

## 使用流程

1. **抓取凭证** — 在 TUI 面板按 `[6]`，打开 Firefox 手动登录一次，脚本会自动截获浏览器发出的加密登录请求并保存为凭证，无需手动输入密码
2. **测试登录** — 按 `[5]` 验证凭证有效
3. **启动守护** — 按 `[1]`，后台每 20 秒检测一次网络，断网自动重连

## 日志查看

所有日志统一写入 `logs/netmaster.log`，每条记录带完整日期时间戳。在 TUI 面板按 `[4]` 进入日志查看器：

- 显示最后 10 行，断网/失败事件标红，成功标绿
- `[f]` 实时追踪 (tail -f)，`[a]` 完整浏览 (less)
- `[g]` 关键词搜索，默认搜"断网"快速定位历史断网时间

## 文件说明

```
~/.netmaster/
├── netmaster_daemon.py       # 守护进程（核心）
├── netmaster_tui.py          # 终端交互面板
├── capture_payload.py        # 凭证抓取工具
├── daemon_config.json        # 登录配置
├── ctl.sh                    # 命令行管理脚本
├── geckodriver               # Firefox WebDriver
└── logs/
    └── netmaster.log         # 统一日志文件
```

## 管理命令

```bash
netmaster                    # 打开 TUI 控制面板
~/.netmaster/ctl.sh start    # 启动守护
~/.netmaster/ctl.sh stop     # 停止守护
~/.netmaster/ctl.sh restart  # 重启守护
```

## 依赖

- Python 3.8+
- `requests` 库
- Firefox + geckodriver（仅抓包时需要）
