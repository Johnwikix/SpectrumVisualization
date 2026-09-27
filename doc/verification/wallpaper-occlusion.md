# 壁纸全屏遮挡自动暂停（2026-09-27）

壁纸模式下，每 500 ms 检查是否有一个可见、不透明的普通窗口完整覆盖壁纸所在的主屏区域。确认覆盖后暂停共享 `CanvasAnimatedControl`；解除覆盖、最小化或隐藏覆盖窗口后恢复。退出壁纸模式时立即撤销暂停。

检测使用 DWM 的可见窗口边界，排除桌面/任务栏、最小化、隐藏、cloaked、layered/透明以及带复杂形状的窗口。读不到窗口信息时保持渲染。窗口仅在副屏全屏或仍露出主屏边缘时不暂停；多个窗口拼接覆盖不在本次检测范围。

暂停停止效果的 Update/Draw；音频分析和 SMTC 服务继续更新，恢复时读取最新状态。恢复前调用 `ResetElapsedTime`，避免补算暂停时间。暂停期间切换效果时，旧资源仍通过游戏循环线程的任务队列释放，不依赖下一次 Draw。

## 验证

Debug 构建及 Release NativeAOT 发布通过，保留既有编译警告。使用独立 Debug 实例和真实 WinForms 覆盖窗口测试，测试后恢复原 Debug 配置及日志，未关闭原有 Release 实例。

Aurora 与 Sonic 均通过以下场景：部分覆盖、主屏不透明全屏、保持全屏、最小化、恢复全屏、半透明覆盖、隐藏、一像素桌面边缘、副屏全屏以及最终恢复。每段暂停的开始与结束 `totalFrames` 完全一致；恢复后两种效果均回到约 60 FPS。

- [Aurora 结果](wallpaper-occlusion-aurora.txt)
- [Sonic 结果](wallpaper-occlusion-sonic.txt)
- [渲染计数与 GPU 原始采样](wallpaper-occlusion-metrics.txt)

Sonic 测试进程的 GPU 3D 引擎占用在正常渲染时约 17–18%，稳定暂停区间连续四次采样为 0.0000%。例如 19:14:21.770 暂停、19:14:26.386 恢复，帧计数均为 2965；19:14:22.904 至 19:14:25.966 的四次 GPU 采样均为零。这是该进程的 GPU 活动测量，不是整机功耗或瓦数测量。

## 重跑

使用本目录的 .NET 10 SDK 配置构建，启动设置 `SPECTRUM_DIAGNOSTICS=1` 且 `WallpaperEnabled=true` 的 Debug 实例。用 `wallpaper-state.ps1` 获取该实例的 `WinExSpectrumTest` 窗口句柄，再运行：

```powershell
dotnet build WallpaperOcclusionProbe/WallpaperOcclusionProbe.csproj
./WallpaperOcclusionProbe/bin/Debug/net10.0-windows/WallpaperOcclusionProbe.exe <wallpaper-hwnd> <render-metrics.log-absolute-path> <result-txt-absolute-path>
```

测试会短暂显示全屏窗口，自动测试各状态后关闭。分别使用 `aurora-ring` 和 `sonic-topography` 配置重跑。

实现依据：[Win2D Paused](https://microsoft.github.io/Win2D/WinUI3/html/P_Microsoft_Graphics_Canvas_UI_Xaml_CanvasAnimatedControl_Paused.htm)、[ResetElapsedTime](https://microsoft.github.io/Win2D/WinUI3/html/M_Microsoft_Graphics_Canvas_UI_Xaml_CanvasAnimatedControl_ResetElapsedTime.htm)、[DWM 窗口属性](https://learn.microsoft.com/en-us/windows/win32/api/dwmapi/ne-dwmapi-dwmwindowattribute)。
