# NetMaster Windows 版

上海大学校园网自动登录守护程序（Windows 版）。

## 快速开始

### 方式一：直接运行 EXE

下载 [Releases](../../releases) 中的 `NetMaster.exe`，双击运行即可。

### 方式二：从源码运行

```bash
git clone git@github.com:bo-qian/NetMaster.git
cd NetMaster/windows
pip install -r requirements.txt
python main.py
```

## 使用流程

1. **抓取凭证** — 点击 `🚀 启动抓包`，在弹出的 Edge 浏览器中登录校园网，凭证自动捕获
2. **测试配置** — 点击 `🛠️ 测试配置` 验证凭证有效
3. **生成 & 启动** — 点击 `📥 生成配置 & 更新任务`，再点 `▶️ 立即启动`
4. **后台静默运行** — 直接关闭窗口即可，已托管至 Windows 任务计划，开机自启

右上角显示绿色 **"🚀 正常守护中"** 即配置成功。

## 停止与卸载

重新运行 `NetMaster.exe`（或 `python main.py`），点击 `🗑️ 卸载任务` 彻底清理任务计划及所有配置文件。

## 文件说明

```
windows/
├── main.py             # 主程序（PySide6 GUI + daemon 模式）
├── convert_icon.py     # 图标格式转换工具
├── net.png             # 应用图标（PNG）
└── net.ico             # 应用图标（ICO）
```

## 技术架构

- **GUI**: PySide6（暗色主题）
- **守护进程**: Windows 任务计划 + `--daemon` 无头模式
- **抓包引擎**: Selenium + Edge WebDriver（自动匹配版本，从国内镜像下载）
- **打包方式**: Nuitka OneFile

`main.py` 支持两种运行模式：

| 模式 | 命令 | 说明 |
|------|------|------|
| GUI | `python main.py` | 图形管理界面 |
| Daemon | `python main.py --daemon --config <path>` | 后台守护，由任务计划自动调用 |

## 依赖

- Python 3.8+
- `PySide6`
- `selenium`
- `requests`
- Microsoft Edge 浏览器（抓包时需要）
