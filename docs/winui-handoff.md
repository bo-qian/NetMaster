# NetMaster WinUI 开发交接

最后更新：2026-09-30（Asia/Shanghai）。每轮交流见 [开发日志](dev-log.md)，执行规则见仓库根目录 [AGENTS.md](../AGENTS.md)。

## 仓库与工作分支

- 仓库：<https://github.com/bo-qian/NetMaster>
- 开发分支：`feature/winui3-windows`，完成调试后再决定合并 `main`。
- 当前机器路径：`D:\OneDrive\Science_Research\Scripts\NetMaster`。其他电脑可以使用自己的目录，不依赖这个绝对路径。
- 旧版 Windows 程序：`windows/main.py`（Python + PySide6 + Selenium + requests，Nuitka 打包）。
- 新版解决方案：`windows/NetMaster.WinUI/NetMaster.WinUI.slnx`。

## 当前实际完成状态

| 项目 | 状态 / 证据 |
|---|---|
| Git 开发分支 | 已建立；在该分支继续开发 |
| WinUI 空项目 | 用户在 Visual Studio 创建，F5 已打开窗口 |
| 第一版 XAML 布局 | 已实现连接、测速和最近日志占位卡片；尚无三页导航与业务功能 |
| 深色标题栏 | 已设置 `AppWindow.TitleBar.PreferredTheme = TitleBarTheme.UseDefaultAppMode` |
| 标题栏与页面过渡不同步 | 已改用 WinUI `TitleBar` + `ExtendsContentIntoTitleBar`，按钮活动 / 非活动背景透明；用户确认问题解决 |
| 构建 | x64 Debug 构建通过，0 警告、0 错误；尚未验证其他架构、Release 或安装包 |
| 三页最终布局 | 用户已认可概览、校园网登录和日志效果图；尚未按效果图实现 |
| 原有登录 / 守护 | 仍在旧版 Python 程序中，尚未接入 WinUI |
| WebView2 登录、测速、实时后台日志 | 需求已确认，尚未实现 |
| 品牌 Logo | 已有 PNG / ICO / SVG 素材，新版图标尚未替换默认模板素材 |

**不要把当前占位界面当作完整改版。** 当前禁用按钮和预览提示用于避免把示例数据显示成真实结果。

## 已确认的界面

- 主导航仅三项：**概览 / 校园网登录 / 日志**。没有独立测速页面。
- 设置入口放在右上角齿轮；关于信息纳入设置。
- 深色参考采用原生 Fluent 控件、Mica 基底、圆角内容卡片。整体跟随系统主题；设置中可以选择跟随系统、浅色或深色。
- 保持已修复的标题栏：它与页面使用同一个 Mica 背景；窗口系统按钮仍交给 Windows。

| 页面 | 内容与行为 | 参考图 |
|---|---|---|
| 概览 | 独立的校园网连接状态和守护状态；重新检测；断网自动重连开关；Windows 登录后启动开关；下载 / 上传 / 延迟及开始测速；最近实时日志、查看全部入口 | [概览](design/overview-concept.png) |
| 校园网登录 | 左侧内嵌实际认证网页，可刷新或备用外部打开；右侧显示当前账号和登录信息状态；验证登录、保存并启用守护。未登录时使用等待状态，不能启用依赖凭证的操作 | [校园网登录](design/campus-login-concept.png) |
| 日志 | 搜索、级别与日期筛选、导出；实时更新开关；日志列表；自动滚动；选中记录详情与复制。概览摘要与这里的数据来自同一日志源 | [日志](design/logs-concept.png) |

概览原始效果图仍有测速、设置、关于侧栏项；**最新决定已删除这些项**，导航以登录页 / 日志页效果图为准，概览仅沿用内容布局。

图中网页只是示意，实际应由 WebView2 加载校园网网页，而不是复刻图中的账号密码表单。网页可能保持白色，应用本身仍按深色模式显示。

所有截图均为概念图，账号、日志、速度数值为示例，不能用于实际运行状态。

## 旧版需要保留的功能

从 `windows/main.py` 实际代码核对：

- 打开 Edge、捕获校园网登录 POST 请求体，提取账号。
- 验证当前登录配置（调用校园网认证接口），区分成功、已在线和服务器拒绝。
- 保存登录配置并创建 / 更新 Windows 计划任务。
- 启动和停止后台守护；启动即检测网络，之后按检测间隔检测，断网时自动登录并复查网络。
- Windows **用户登录后**自动启动（旧版使用 `LogonTrigger`，不是无用户登录时的系统启动服务）。
- 关闭管理窗口后守护可以继续运行。
- 检测间隔默认 20 秒，日志保留默认 7 天，可选择配置 / 日志目录并记住上次目录。
- 移除后台任务及相关配置、日志、旧浏览器驱动。
- 旧版 UI 显示本次界面操作日志；守护日志另写按日期的文件。新版需要把真实后台日志接入界面，不能只沿用 UI 日志框。

旧版的“正常守护中”主要依赖计划任务运行状态。新版必须独立判断互联网可用性，显示断网、正在重连、登录失败等状态。

旧版请求体保存于 JSON，是可复用的敏感登录信息，不能因为包含编码后的密码字段就称它是安全的系统加密存储。新版本应规划 Windows 用户级安全存储；凭证不进入 Git 或开发日志。

## 建议接续顺序

1. 拉取本分支，打开 `.slnx`，F5 验证当前项目能运行。
2. 先实现三页导航、概览布局、设置入口与品牌图标，按已认可效果图对照。没有接入的业务展示真实空状态 / 禁用状态。
3. 将网络检测、配置、守护管理和日志访问从界面分离，确定现有 Python 逻辑迁移到 C# 的边界；避免 GUI 和守护都各自重复执行重连。
4. 接入 WebView2 实际校园网登录，确认能取得所需登录信息，再实现验证和安全保存。
5. 接入自动重连、启动 / 停止、登录后启动和真实日志，再连接概览与日志页。
6. 最后接入按需测速（下载、上传、延迟）、测速进度 / 结果与必要的历史摘要；选择实际可用的测速服务，不伪造结果。
7. 验证首次配置、已配置、断网、登录失败、关闭窗口后台运行、重新启动、更新 / 移除任务等真实流程，再准备 MSIX / EXE 安装与分发。

## 宿舍电脑环境与获取代码

需要 Windows、Visual Studio 2026 的 **WinUI 应用程序开发** 工作负载、WebView2 Runtime，以及项目运行所需的 .NET 8 Runtime。当前项目是 `.NET 8`，开发机已装 .NET 10 SDK 也能构建它；不要未经讨论直接改目标框架。

当前 `.csproj` 引用：`Microsoft.WindowsAppSDK 2.5.1`、`Microsoft.Windows.SDK.BuildTools 10.0.28000.2705`，以实际文件为准。NuGet 还原需要联网。

在新目录克隆：

```powershell
git clone --branch feature/winui3-windows https://github.com/bo-qian/NetMaster.git
cd NetMaster
```

已有仓库时，先检查状态，保留本地改动后再切换 / 同步分支：

```powershell
git status --short --branch
git fetch origin
git switch feature/winui3-windows
git pull --ff-only
```

在 Codex 中打开 **NetMaster 仓库根目录**，开始对话时要求先阅读 `AGENTS.md`、本文件和开发日志。不要求复制原电脑的整个聊天数据库或账号配置。

正常构建：

```powershell
dotnet build windows/NetMaster.WinUI/NetMaster.WinUI.csproj -p:Platform=x64 -v:minimal
```

完成工作或准备换电脑时，更新交接和开发日志，将相关修改提交并推送到本分支。另一台电脑再拉取，才能看到这些记录。**日志文件本身不会跨机器自动同步；同步通过 Git 完成。**

## 其他已决定事项

- MIT 协议及 README 的 Star History 已添加。用户接受当前 Star History 工具和右下角标识，保持现有方案。
- 品牌视觉基准见 `assets/branding/README.md`；目前 SVG 内嵌 PNG，不是纯矢量路径。
- Microsoft Store、签名和官方风格安装流程是后续分发方向，尚未实施。
- GitHub Contributors 曾有缓存与实际提交记录不一致的情况；本次交接未重新检查，不将其视为已验证解决。
