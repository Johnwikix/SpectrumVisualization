# 壁纸全屏与最大化遮挡自动暂停

壁纸模式下，每 500 ms 检查是否有一个可见、不透明的普通窗口完整覆盖壁纸，或同一显示器上的最大化窗口覆盖壁纸与显示器工作区的交集。确认覆盖后通过 `CanvasPanel.SetRenderingSuspended` 暂停当前宿主：Win2D 设置 `Paused`，D3D12 工作线程收到 `Active=false` 后进入事件等待。解除覆盖、最小化或隐藏覆盖窗口后恢复；退出壁纸模式时立即撤销暂停。

检测使用 DWM 的可见窗口边界，排除桌面/任务栏、最小化、隐藏、cloaked、layered/透明以及带复杂形状的窗口。只有 `IsZoomed` 且显示器句柄与壁纸一致时才允许排除任务栏预留区；普通窗口仍按完整壁纸边界判断。读取工作区失败时退回完整边界；读不到窗口边界时保持渲染。窗口仅在副屏全屏、普通窗口露出主屏边缘或最大化窗口露出工作区边缘时不暂停；多个窗口拼接覆盖不在检测范围。

暂停停止效果的 Update/Draw；音频分析和 SMTC 服务继续更新，恢复时读取最新状态。Win2D 恢复前调用 `ResetElapsedTime`，D3D12 唤醒后重置计时，避免补算暂停时间。暂停期间切换效果仍等待在途帧屏障，不依赖下一次 Draw，也不在 UI 线程同步等待 GPU。

## 2026-09-29 最大化修复与无窗口回归

根因：`DesktopWallpaperHost` 将壁纸铺满包括任务栏的整个显示器，旧检测却要求普通最大化窗口的 DWM 边界覆盖整个壁纸。只覆盖工作区的最大化窗口因此无法触发暂停。修复按每次轮询获取的显示器工作区判断同屏最大化窗口，并让简单窗口区域检查使用同一目标边界。显示桌面、隐藏/透明和副屏排除逻辑保留。

`WallpaperOcclusionProbe --geometry` 在 WinForms 初始化前进入纯几何测试，直接链接生产检测器，不创建窗口、不枚举桌面、不启动音频采集或主应用。27 项通过：四边任务栏、无预留区/两像素预留区、部分还原、一像素缺口、副屏坐标、负坐标、高 DPI 物理像素、显示器读取失败及无效/跨屏边界。这里的任务栏与 DPI 场景均为合成坐标，不能替代真实显示器验收。

工作目录 `doc/verification`（使用该目录固定的 .NET 10 SDK）：

```powershell
dotnet run --project WallpaperOcclusionProbe/WallpaperOcclusionProbe.csproj -c Release -- --geometry
dotnet build ../../WinExSpectrumTest.csproj -c Debug -r win-x64 -p:Platform=x64 -p:WindowsPackageType=None -p:SkipReconstructionNativeBuild=true --no-restore
dotnet publish ../../WinExSpectrumTest.csproj -c Release -r win-x64 -p:Platform=x64 -p:WindowsPackageType=None -p:SkipReconstructionNativeBuild=true -p:PublishAot=true -p:PublishTrimmed=true --no-restore -o ../../bin/wallpaper-maximized-publish
```

主工程 Debug 构建通过（75 项既有警告，0 错误）；Release NativeAOT + 裁剪发布通过，产物为 `bin/wallpaper-maximized-publish/Spectrum.exe`。保留既有 nullable/obsolete、缺失 publish profile、IL3000 与 SharpGen IL2104 警告，本次检测器没有新增警告。未修改原生重建桥，构建复用已存在的桥接 DLL。日志：本地忽略的 `wallpaper-maximized-build.log`、`wallpaper-maximized-publish.log`。

本次未启动主应用或执行实机呈现、GPU 占用/功耗采样。用户验收：两种效果分别进入壁纸模式，主屏普通程序最大化后应在约一个轮询周期内暂停；最小化或还原为部分窗口后恢复；副屏最大化不应暂停主屏；Win+D 应恢复。实机探针已加入有边框窗口最大化、保持、最小化及还原步骤，但本次未执行。此前全屏测试的 GPU 结果不作为本次最大化修复的功耗证据。

API 契约：[IsZoomed](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-iszoomed)、[MONITORINFO 工作区坐标](https://learn.microsoft.com/en-us/windows/win32/api/winuser/ns-winuser-monitorinfo)、[MonitorFromWindow](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-monitorfromwindow)。

## 2026-09-27 历史全屏实机验证

Debug 构建及 Release NativeAOT 发布通过，保留既有编译警告。使用独立 Debug 实例和真实 WinForms 覆盖窗口测试，测试后恢复原 Debug 配置及日志，未关闭原有 Release 实例。

Aurora 与 Sonic 均通过以下场景：部分覆盖、主屏不透明全屏、保持全屏、最小化、恢复全屏、半透明覆盖、隐藏、一像素桌面边缘、副屏全屏以及最终恢复。每段暂停的开始与结束 `totalFrames` 完全一致；恢复后两种效果均回到约 60 FPS。

- [Aurora 结果](wallpaper-occlusion-aurora.txt)
- [Sonic 结果](wallpaper-occlusion-sonic.txt)
- [渲染计数与 GPU 原始采样](wallpaper-occlusion-metrics.txt)

Sonic 测试进程的 GPU 3D 引擎占用在正常渲染时约 17–18%，稳定暂停区间连续四次采样为 0.0000%。例如 19:14:21.770 暂停、19:14:26.386 恢复，帧计数均为 2965；19:14:22.904 至 19:14:25.966 的四次 GPU 采样均为零。这是该进程的 GPU 活动测量，不是整机功耗或瓦数测量。

## 实机重跑（需要用户当次明确授权）

使用本目录的 .NET 10 SDK 配置构建，启动设置 `SPECTRUM_DIAGNOSTICS=1` 且 `WallpaperEnabled=true` 的 Debug 实例。用 `wallpaper-state.ps1` 获取该实例的 `WinExSpectrumTest` 窗口句柄，再运行：

```powershell
dotnet build WallpaperOcclusionProbe/WallpaperOcclusionProbe.csproj
./WallpaperOcclusionProbe/bin/Debug/net10.0-windows/WallpaperOcclusionProbe.exe <wallpaper-hwnd> <render-metrics.log-absolute-path> <result-txt-absolute-path>
```

测试会短暂显示全屏窗口，自动测试各状态后关闭。分别使用 `aurora-ring` 和 `sonic-topography` 配置重跑。

实现依据：[Win2D Paused](https://microsoft.github.io/Win2D/WinUI3/html/P_Microsoft_Graphics_Canvas_UI_Xaml_CanvasAnimatedControl_Paused.htm)、[ResetElapsedTime](https://microsoft.github.io/Win2D/WinUI3/html/M_Microsoft_Graphics_Canvas_UI_Xaml_CanvasAnimatedControl_ResetElapsedTime.htm)、[DWM 窗口属性](https://learn.microsoft.com/en-us/windows/win32/api/dwmapi/ne-dwmapi-dwmwindowattribute)。
