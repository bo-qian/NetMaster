# NetMaster 图标

- `netmaster-icon.png`：NetMaster 独立品牌主图，1254 × 1254，透明背景；主体是亮蓝 `#1677FF` 的圆角闪电 S。
- `netmaster-icon.ico`：由该 PNG 生成的 Windows 多尺寸图标。
- `netmaster-icon-32.png`、`netmaster-icon-16.png`：用于检查小尺寸可读性。
- `netmaster-icon.svg`：内嵌同一 PNG 的自包含 SVG，确保显示效果与 PNG 一致；它不是可编辑的纯矢量路径。

PNG 是当前正式视觉基准，标题栏、应用图标、任务栏、开始菜单、启动图和安装磁贴均从它生成。若以后需要纯矢量源稿，应参照这张 PNG 重新描绘并单独核对。

当前主体边界为 960 × 924，居中于 1254 × 1254 画布，最大方向占比约 77%；透明边距用于保证小尺寸显示完整。这个比例是结合实际任务栏视觉比较选定的，并非 Windows 规定的固定值。

更新主 PNG 后运行 `tools/Generate-BrandAssets.ps1`，会同步生成所有 WinUI PNG、标题栏图、ICO、SVG 和 16 / 32 像素检查图。
