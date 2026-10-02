# NetMaster 校园网助手 · Microsoft Store 首发资料草稿

本稿保存 `v2.0.0` / MSIX `2.0.0.0` 首次提交的中文文案。应用仍是上海大学校园网专用工具。2026-10-02 已将此中文文案及对应英文文案填入 Partner Center Submission 1 草稿；尚未正式送审。

## 商店列表（简体中文）

**产品名称：** NetMaster 校园网助手（已预留）

**简短介绍：** 在 Windows 上配置上海大学校园网登录，查看连接与后台守护状态，并在断线时尝试自动恢复认证。

**说明：**

NetMaster 是面向上海大学校园网用户的 Windows 桌面工具。首次使用时，在应用内的学校认证网页完成登录，确认并保存本次登录信息后，即可在概览页分别查看互联网连接与后台守护状态。

启用自动重连后，NetMaster 会按设定间隔检查连接；检测到需要重新认证时，后台守护会尝试使用已保存的登录信息恢复校园网连接。你可以随时暂停自动重连、关闭 Windows 登录后启动，或清除保存的登录信息。

应用还提供下载和上传测速（以 MB/s 显示平均、最高、最低速度）、延迟测量，以及可搜索、筛选和导出的运行日志。测速只在用户主动点击后进行，会产生网络流量，测试结果受当前网络与测试服务影响。

本应用需要能够访问上海大学校园网认证服务；离开相应网络时，配置和自动认证功能可能无法使用。NetMaster 由个人开发者独立开发，不代表上海大学、Microsoft 或网络测速服务的官方产品。登录信息保存在当前 Windows 用户环境中，并使用 Windows DPAPI 加密；具体处理方式见隐私说明。

**功能要点（在 Partner Center 中逐条填写，不含项目符号）：**

1. 在应用内完成校园网登录配置，确认后保存并启用守护
2. 分别显示互联网连接状态和后台守护状态
3. 按设定间隔检查连接，在需要时尝试自动恢复认证
4. 主动测速，查看下载和上传速度及延迟
5. 搜索、筛选、查看和导出运行日志
6. 支持浅色、深色与跟随系统主题

**此版本的新增内容：** 首次商店提交按微软说明留空；GitHub `v2.0.0` Release 可另写重构版说明。

**应用网站：** https://github.com/bo-qian/NetMaster

**支持入口：** https://github.com/bo-qian/NetMaster/issues

**隐私说明：** 复核 [隐私说明](privacy-policy.md) 后，可按 Partner Center 当前表单填写说明正文，或使用由开发者维护的稳定公开 HTTPS 页面。不要把尚可能变动的开发分支路径当成永久地址。

## 截图与图标

- 只使用实际运行的 WinUI 页面，不使用 `docs/design/` 的概念图，也不制造连接成功、测速结果或日志。
- Desktop 截图为 PNG，分辨率至少 1366 × 768，单张不超过 50 MB。至少 1 张，微软建议 4 张以上；仅上传实际支持的 Desktop 设备家族截图。
- 用户已明确同意公开使用真实日志页截图。原始 `.png` 实际为 JPEG 编码，Partner Center 拒绝后已转换为真正 PNG：忽略目录 `windows/NetMaster.WinUI/bin/store-listing-review/logs-real-20261002-converted.png`，2048 × 1104，SHA-256 `47ABEA93488BEBC46FD3ECC56D5C22DB0D0EFB2D0A5E0478172971EA1FE86906`；简中和英语（美国）列表各上传一张。截图含运行时间与事件，不含完整账号或密码。
- 后续可补概览、设置与配置页。概览虽已脱敏仍显示账号片段，配置页的内嵌学校网页可能显示完整账号；公开前必须逐张核对。截图中不应包含真实密码、令牌、完整账号、个人路径或可识别的私有日志。
- 包中已有 NetMaster 图标，`windows/NetMaster.WinUI/Assets/Square150x150Logo.scale-200.png` 实际尺寸为 300 × 300，可作为 1:1 商店图标候选；上传前核对与应用包显示一致。不要使用学校或 Microsoft 的官方标志。

## 提交草稿待核对项

- **定价和范围：** 用户已选择免费、全球所有市场、公开可搜索；工具只适用于有相应校园网访问条件的用户，商店文案已说明使用前提。
- **属性及年龄分级：** 建议选择与工具软件相符的类别；年龄分级问卷应按实际功能作答，不预填猜测答案。隐私项须如实说明会处理校园网登录信息。
- **包：** 修正后的 `NetMaster.WinUI_2.0.0.0_x64.msixupload` 已上传并通过 Partner Center 包预检；当前仅有 x64 候选包。
- **受限功能声明：** manifest 含 `runFullTrust`，如 Partner Center 要求解释，应说明 WinUI 桌面应用需要启动独立的本机守护进程、访问当前用户本地设置并执行网络检测与重连。该功能只在用户启用后工作，可在设置中停止。
- **认证测试说明：** 学校认证地址仅能在相应校园网环境使用。审核人员在普通外网下可查看概览、空状态、日志与设置，但无法完成校园网配置。微软提示需登录的应用应提供可用测试账号及操作说明，否则可能因无法完整测试而未通过认证；提交前需确定是否能提供**学校允许的专用测试环境和账号**，不能提交个人真实校园网凭证。若不能提供，应如实注明网络限制并预期可能收到测试问题。
- **发布控制：** 已选择审核通过后由用户手动发布；正式送审仍须用户审阅并明确同意。
- **待独立验证：** 真实断线恢复、Windows 登录后自启、升级 / 卸载和 Store 新构建后台组件的运行策略，不能由开发版 UI 验收或包结构检查代替。

## 官方依据

- [创建 MSIX 提交](https://learn.microsoft.com/en-us/windows/apps/publish/publish-your-app/msix/create-app-submission)
- [商店列表字段](https://learn.microsoft.com/en-us/windows/apps/publish/publish-your-app/msix/add-and-edit-store-listing-info)
- [截图尺寸与素材](https://learn.microsoft.com/en-us/windows/apps/publish/publish-your-app/msix/screenshots-and-images)
- [隐私与支持信息](https://learn.microsoft.com/en-us/windows/apps/publish/publish-your-app/msix/support-info)
- [提交选项及认证测试说明](https://learn.microsoft.com/en-us/windows/apps/publish/publish-your-app/msix/manage-submission-options)
