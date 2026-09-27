# 壁纸模式验证（2026-09-27）

托盘新增“壁纸模式（主屏幕）”勾选项。开关立即保存，重启恢复；首次使用保持小组件模式。

新增全屏遮挡自动暂停，详见[暂停/恢复与 GPU 验证](wallpaper-occlusion.md)。

## 实现

- 当前效果作为 Explorer 的子窗口覆盖主屏幕完整区域，隐藏标题栏、启用穿透、不抢焦点、不置顶。
- 传统桌面使用图标窗口之后的 WorkerW；新版 Windows 11 使用 Progman 下位于 SHELLDLL_DefView 后方的分层子窗口。
- 按物理像素定位，并转换到父窗口客户区坐标，支持副屏位于主屏左侧或上方的布局。每两秒检查实际屏幕矩形、桌面层级和宿主有效性。
- 关闭恢复原窗口边界及普通、锁定、全屏、最大化、最小化状态。桌面连接失败时尝试回退并给出托盘提示。
- 壁纸模式清除画布原有的 1 DIP 外边距，并关闭自定义标题栏扩展，消除 WinUI 内容宿主顶部额外的 1 物理像素保留区；退出模式时恢复这两项。托盘开关使用图片图标并保留勾选状态。
- 保留现有系统壁纸文件；退出前解除桌面挂载、停止定时器并清理托盘图标。

## 实测结果

环境：Windows 11 build 26200，主屏 3840×2160 / 150%，另有位于负坐标的副屏。使用 .NET SDK 10.0.401。

- Debug 构建通过；普通、锁定、全屏状态的 UI 回归通过。
- NativeAOT Release 发布成功；`wallpaper-regression.ps1` 的 33 项断言通过，覆盖启停、主屏边界、桌面父窗口与图标层级、左键保护、效果切换以及五种窗口状态恢复。
- 发布版启用后配置中 `WallpaperEnabled=true`；从托盘退出再启动，自动挂回主屏桌面。关闭后配置为 `false`。
- 壁纸模式下设置窗口可正常打开；Win+D 后效果仍显示在桌面图标下方。
- 原有 nullable、旧 API、单文件版本信息和发布配置文件警告仍存在；新增壁纸代码无构建错误。
- 未在传统 Windows 10 桌面或重启 Explorer 的场景实测；对应宿主发现、重新挂载和失败回退路径已实现。

边缘修复复现：外窗口与客户区均为 `(0,0,3840,2160)`，但修复前 XAML 内容宿主为 `(0,1,3840,2159)`。修复后三者完全一致，顶部中央第 0–5 行像素均为 `FF10162C`，未露出底层桌面。回归脚本新增客户区及 XAML 内容宿主覆盖检查，共 45 项断言。

效果：[主屏壁纸](wallpaper-primary.png)、[带图标的托盘菜单](wallpaper-tray.png)、[恢复后的小组件（UIA 标注）](wallpaper-restored.png)。

## 重跑

先启动一个当前版本的 Spectrum 实例。以下脚本使用本机安装的 lvt，通过系统托盘和 UIA 驱动程序，不调用应用内部方法。

```powershell
./wallpaper-regression.ps1
./wallpaper-state.ps1
```

`wallpaper-state.ps1` 只读取 Win32 状态。`wallpaper-tray.ps1` 支持可见托盘与隐藏图标区；使用 Shift+F10 打开菜单，因此全屏时也能验证。lvt 必须以实际菜单宿主 HWND 为目标，直接激活 WinUI 的弹出窗口 HWND 会使菜单关闭。

在本目录发布：

```powershell
dotnet publish ../../WinExSpectrumTest.csproj -c Release -r win-x64 -p:Platform=x64 -p:WindowsPackageType=None -p:GenerateAppxPackageOnBuild=false -p:AppxPackage=false -p:EnableMsixTooling=false -p:PublishReadyToRun=false
```

输出：`bin/x64/Release/net10.0-windows10.0.26100.0/win-x64/publish/Spectrum.exe`。

## 平台依据

[SetParent 文档](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-setparent)说明父窗口变更不会自动切换 WS_CHILD/WS_POPUP；解除挂载时需先 SetParent(NULL)，再清除 WS_CHILD。两步之间 GetParent 会返回桌面句柄，不能将其误判为失败。

Explorer 的桌面层消息是私有协议；新版 Windows 11 的桌面结构可参考 [Lively 的桌面宿主说明](https://github.com/rocksdanister/lively/blob/core-separation/src/Lively/Lively/Core/WinDesktopCore.cs)。实现对挂载失败进行检查并回退，不修改 Explorer 自身样式。
