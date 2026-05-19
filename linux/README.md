# NetMaster Linux 版

上海大学校园网自动登录守护程序（Linux 版）。

## 一键安装

```bash
git clone git@github.com:bo-qian/NetMaster.git
cd NetMaster/linux
./install.sh
```

安装脚本自动适配主流 Linux 发行版（Fedora / Ubuntu / Debian / Arch 等），安装完成后新终端输入 `netmaster` 打开控制面板。

> 建议终端宽度 ≥ 111 列以显示完整彩虹 banner，窄屏自动降级为精简版。

## 使用流程

1. **抓取凭证** — 在 TUI 面板按 `[6]`，打开 Firefox 手动登录一次，自动捕获登录请求体
2. **测试登录** — 按 `[5]` 验证凭证有效
3. **启动守护** — 按 `[1]`，后台每 20 秒检测一次网络，断网自动重连

## TUI 控制面板

```
╔═══════════════════════════════════════════════╗
║   ███╗    ██╗  ███████╗  ...  ██████╗         ║
║   ████╗   ██║  ██╔════╝  ...  ██╔══██╗        ║
║   ...                                        ║
║         Campus Network Auto-Guardian          ║
╚═══════════════════════════════════════════════╝

  ─── 系统状态 ───
  ●  服务守护    运行中       ← 绿色=正常
  ⇱  启动方式    开机自启     ← 绿色=自动
  ●  网络连接    已联网
  ●  登录账号    23723856     ← 青色=信息
  ──────────────────────────
  仓库 https://github.com/bo-qian/NetMaster

  最近日志:
  │ [2026-05-19 07:00:03] NetMaster 守护进程启动
  │ [2026-05-19 10:26:12] >>> 登录成功

  操作:
  [1] 启动守护  [2] 停止守护  [3] 重启守护
  [4] 实时日志  [5] 测试登录  [6] 抓取凭证  [q] 退出
```

## 日志查看器 (按 [4])

- 显示日志文件路径、大小、总行数
- 最后 10 行事件着色（红=断网/失败，绿=成功，黄=启停）
- `[f]` 实时追踪 (tail -f)
- `[a]` 完整浏览 (less)
- `[g]` 关键词搜索（默认搜"断网"，快速定位断网时间）

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
    └── netmaster.log         # 统一日志文件（含完整日期时间戳）
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
