# HDR 验证与维护

实现参考 `G:\SoftwareProject\winui\ComputeSharpDemo` 的 ComputeSharp / D3D12 设备互操作、HDR10 输出与显示器检测。极光之环保留 Win2D SDR，音域回响使用独立线程和不透明 SwapChainPanel。两个宿主共享原音频分析器，切换先等待旧宿主的在途帧退出；非活动宿主保留有界资源，不继续绘制。

音域回响路径：ComputeSharp FP32 地形 → FP16 线性 Rec.709 场景与预乘粒子合成 → SDR sRGB 或 Rec.2020 / ST.2084 PQ → RGB10 交换链。普通线性白对应白点 nit，超过白点的高光平滑压缩到峰值。封面和文字为 Windows 合成的 SDR XAML 图层，不随 HDR 亮度参数放大。最大亮度是应用输出上限，不是显示器自动校准。

配置默认关闭 HDR，白点 200 nit（80–500），峰值 1000 nit（80–4000）。白点升到峰值以上会同步提高峰值；峰值降到白点以下会降低白点。非法浮点值回到默认值。开关表达用户意图，`HdrStatus` 单独报告实际输出；当前显示器每 500 ms 检测，未启用系统 HDR 时回到 SDR。

## 2026-09-27 实测

- Windows 11、NVIDIA GeForce RTX 5070 Ti，主显示器 Windows HDR 已开启，150% DPI；实际窗口报告 HDR10。
- GPU 探针调用生产着色器和输出 PSO：80 / 160 / 320 网格输出有限、非黑且不透明；回读 RGB10 像素，黑色、200 / 400 nit 白色、1000 / 200 nit 峰值和 SDR gamma / 截断均通过（允许量化及抖动造成 2 个 RGB10 码值误差）。
- 真实交换链三轮创建 / 释放，每轮四种尺寸，反复 HDR / SDR 切换通过。预热后各 60 帧、256×144 场景的 `GC.GetAllocatedBytesForCurrentThread` 差值均为 0 字节/帧；这不代表整个应用或尺寸变化时没有分配。
- GPU 探针的 JIT 和 NativeAOT 可执行文件均通过上述检查；主应用完成 Release NativeAOT / 裁剪发布。
- 24 次并发异步保存后同步保存退出快照，等待全部任务后重新读取，仍为最后配置；不存在旧请求覆盖退出快照。
- lvt 实际操作 NativeAOT 窗口：中文名称、亮度互相约束、HDR10 状态、关闭 HDR 后实际 SDR、十次连续效果切换、最大化 / 恢复及恢复极光显示通过。大窗口截图确认地形、粒子和媒体卡片位置正常。
- 最终构建从保存的音域回响 / HDR 配置冷启动后重复关键 UI 回归通过；最小化、恢复及正常关闭通过，渲染中的进程完成异步收尾并退出。
- 现有 `wallpaper-regression.ps1` 的 45 项断言全部通过，包括壁纸期间效果切换、不抢焦点、无顶部缝隙、锁定 / 全屏 / 最大化 / 最小化状态恢复。
- 测试副本使用验证目录下的独立 appsettings.json；未修改已安装应用的配置或系统 HDR 开关。

断言输出见 [hdr-results.txt](hdr-results.txt)。

## 重跑

从本目录运行（使用此目录的稳定 .NET SDK 配置）。先关闭本次验证副本，避免发布时原生 EXE 被占用。

```powershell
dotnet run --project HdrProbe/HdrProbe.csproj -c Release
dotnet publish HdrProbe/HdrProbe.csproj -c Release -p:PublishAot=true -p:PublishTrimmed=true -p:UseAppHost=true -o HdrProbe/bin/native
./HdrProbe/bin/native/HdrProbe.exe
dotnet publish ../../WinExSpectrumTest.csproj -c Release -r win-x64 -p:Platform=x64 -p:WindowsPackageType=None -p:AppxPackage=false -p:GenerateAppxPackageOnBuild=false -p:EnableMsixTooling=false -p:AppxPackageSigningEnabled=false -p:PublishAot=true -p:PublishTrimmed=true -o HdrProbe/bin/app
# 启动上面的 Spectrum.exe 后，传入其实际 PID（需要正常桌面权限）：
./HdrProbe/UiRegression.ps1 -TargetProcess <PID>
./wallpaper-regression.ps1
```

无包发布需要资源索引引用的应用 XBF 同步复制。沙箱桌面下托盘库的 `IsShownInSwitchers` 返回 E_NOTIMPL，且 UIA 无法枚举桌面；实际 UI 验证使用正常桌面权限。截图用于布局检查，不能作为绝对亮度测量。

剩余范围：没有实测跨 SDR/HDR 双屏、运行中关闭系统 HDR、显示器热插拔、GPU 设备移除或 Windows 10。默认签名证书在本机不可用，因此验证使用无包 NativeAOT 发布，没有生成签名 MSIX。现有 nullable / 旧 API 警告和 SharpGen.Runtime 的反射注册裁剪警告仍存在；直接 COM 调用及 ComputeSharp float4 资源已经过原生探针验证。
