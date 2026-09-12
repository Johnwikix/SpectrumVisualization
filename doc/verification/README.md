# 修复与验证记录

- Aurora Ring：增加封面随音频律动开关，默认开启，使用 MVVM 双向绑定并保存到配置。
- 设置同步：共享设置发送变更通知，主窗口/设置页效果选择同步；材质选中状态不再绑定无通知的静态属性。设置窗口只保留初始化、窗口生命周期和命令转发。
- 设置一致性：设备采样率改为显示实际 WASAPI 采样率；条数上限与渲染器统一为 512；幅度系数接入实际计算；音频设备重建后更新采样率与频段映射。
- Sonic：高度图按场景坐标采样，避免 backing texture 布局造成中心偏移；DDA 使用显式 LOD 0 和命中后退出的动态循环，取消固定 96 步截断。移除为补偿中心偏移而额外加入的 AGC，恢复参考 shader 的颜色输出。
- SMTC：统一文字和卡片缩放，调整垂直排列，给时间留出独立宽度；窗口大小改变时重建文字布局。
- FFT：更新保持音频回调驱动，平滑改为按经过的音频时间计算。
- 发布：使用稳定 .NET 10 SDK 完成 NativeAOT 发布；发布目标同步新的 resources.pri，防止加载旧 XAML 资源。

## 验证

实际启动 Debug 和 NativeAOT 发布程序，以 UI Automation 操作：

1. 主窗口 Sonic → Aurora → Sonic，已打开设置页的下拉框同步更新。
2. 设置页选择 Aurora，实际效果和配置同步更新。
3. 封面律动关闭、关闭设置、重开设置，开关仍关闭；测试后恢复开启。
4. 自定义亚克力单选项切换正确；测试后恢复原材质。
5. Sonic 自动旋转，在 2560×1393 窗口下观察多个角度，响应中心保持居中、无方形截断。
6. NativeAOT 发布版实际绘制 Sonic，设置窗口及新开关可正常使用，设置窗口关闭保存正常。

运行测量：FFT 平均间隔 10.53 ms（约 95 次/秒），单次最大间隔 63.01 ms；GC 采样总暂停 2.707 ms。修复版 Sonic 全尺寸自动旋转持续约 60 FPS，FFT 约 95–98 次/秒。帧率为 Draw 回调速率，包含正常调度抖动，不能作为所有显卡的性能保证。

详细日志：[results.txt](results.txt)、[render-metrics.log](render-metrics.log)。

截图：[修复前](baseline-wide.png)、[旋转角度 A](sonic-rotation-a.png)、[旋转角度 B](sonic-rotation-b.png)、[NativeAOT 发布版](native-sonic.png)。

发布程序：`bin/x64/Release/net10.0-windows10.0.26100.0/win-x64/publish/Spectrum.exe`

构建命令（工作目录为本目录，使用 global.json 选择稳定 SDK）：

```powershell
dotnet publish ../../WinExSpectrumTest.csproj -c Release -r win-x64 -p:Platform=x64 -p:WindowsPackageType=None -p:GenerateAppxPackageOnBuild=false -p:AppxPackage=false -p:EnableMsixTooling=false -p:PublishReadyToRun=false
```

构建通过；现有 nullable、旧 API 和版本信息读取警告仍存在。`git diff --check` 通过。

FFT 测量可重新运行 `dotnet run --project AudioProbe.csproj`。Debug 帧率采集在启动程序前设置环境变量 `SPECTRUM_DIAGNOSTICS=1`，日志输出在程序目录的 render-metrics.log。

