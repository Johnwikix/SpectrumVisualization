# SMAA 与时域重建验证

## 2026-09-29 启动预热与缩放回归

### 根因及实现

- 设置调用链 `SettingViewModel -> AppSettings.Changed -> CanvasPanel.ConfigureGpu -> GpuRenderer.Configure` 只提交状态；SDK 初始化、HLSL 编译及 GPU 等待在工作线程。可以确认首次切换会停顿渲染，未采集 UI 线程堆栈，不能把用户观察直接归因为 UI 线程同步等待。
- 旧 `GpuGraphics.Resize` 每次尺寸变化都销毁尺寸资源、重建 SDK 上下文并 `ClearAndPresent` 纯黑帧；SMAA 等 HLSL 也会重复编译。现在缓存静态 HLSL 字节码；窗口尺寸稳定 180 ms 才重建，拖动期间用已有表面的 XAML 变换缩放，重建之后只提交完整场景。
- 新尺寸首帧成功 Present 后重新绑定同一交换链，并在同一 UI 回调中提交表面物理尺寸和逆 DPI 变换。粒子仍按效果的实际输入/输出尺寸生成，媒体卡片保持逻辑坐标。首次启动建立交换链的黑色初始化保留，缩放时的黑帧删除。
- 启动预热使用独立低优先级线程、无窗口绑定交换链、`SpectrumAnalyzer(captureAudio: false)` 的静默输入；覆盖空间模式、可用 XeSS/FSR/DLSS 的 50% 和 100% 输入，以及 DLSS J/K/L/M。会实际 dispatch，随后释放临时纹理与 SDK 上下文；共享设备引用交给实时渲染器或在退出时释放。没有每个尺寸都常驻一套 SDK 上下文。
- 预热尚未完成时，实时宿主不创建或探测厂商 SDK，上次保存的 SR 选择暂用 FXAA 绘制；完成后应用最新设置。这样避免 NGX 设备级初始化/Shutdown 与另一上下文相互干扰，也不让首个窗口等待整个预热过程。SDK 同步创建不能中途强杀，退出采用取消后续步骤并异步等待当前调用结束。
- 预热覆盖产品使用的路径和代表性输入比例，不保证驱动/SDK 的所有内部排列组合都提前完成，也不能消除实际输出尺寸的纹理分配、SDK 上下文创建和 GPU 排空耗时。设备故障恢复时也会放掉预热引用，避免一直保留失效设备。

### 验证证据

- 本机 Intel Arc 140T，厂商能力掩码 `0x18`（XeSS/FSR）；D3D12 调试层，NativeAOT，禁用真实音频采集，实时探针注入合成频谱与触发数据。全部交换链均未绑定窗口。
- `--startup-resize`：重复 Start 只启动一次；后台预热同时运行真实 FXAA 帧；设备引用交接后 SR 创建/回读正常；支持模式预热掩码 `0x1F`。FXAA/SMAA/XeSS/FSR 的奇数和多组尺寸切换均断言 resize 本身不增加 Present 次数，回读有限且非空，调试层无错误，32 帧稳定循环 0 B/帧托管分配。
- 真实 `GpuRenderer` 离屏线程接收 20 次连续尺寸变化，仅发布最终 420×240；暂停期间改尺寸，恢复后正确应用 641×361；暂停屏障、异步停止、启动取消、重复停止和停止后不重启通过。没有用同步假渲染器替代异步边界。
- 一次最终运行预热约 672 ms，期间完成 594 次离屏帧；另一个 320×180 输出、67% 输入切换加首帧 CPU 包围耗时：FXAA 6.9 ms、SMAA 4.9 ms、XeSS 60.2 ms、FSR 42.5 ms。包含队列等待；不是屏幕 FPS。驱动缓存未清空、未隔离系统负载，这些数值不是首次安装冷启动保证，也没有据此宣称固定加速比例。
- `--presentation`：tearing/兼容标志、帧延迟、resize 后回读、稳定分配检查通过；`--reconstruction`：空间 AA、XeSS/FSR 1%–100% 多比例、历史重置、运动矢量、第二效果及不可用 DLSS 回退通过。首次从仓库根目录执行该旧探针时，回读图片输出目录不存在；改用它要求的 `doc/verification` 工作目录后通过，非渲染失败。
- 主工程 Debug 构建、主工程 Release NativeAOT/裁剪发布、探针 NativeAOT 发布通过。保留既有 nullable、缺失 publish profile、IL3000 与 SharpGen IL2104 警告。原生桥接未修改，后续发布使用已构建的桥接 DLL。

命令（工作目录 `doc/verification`；主工程发布在仓库根目录执行）：

```powershell
dotnet publish HdrProbe/HdrProbe.csproj -c Release -r win-x64 -p:PublishAot=true -p:PublishTrimmed=true -p:SkipReconstructionNativeBuild=true --no-restore -o HdrProbe/bin/startup-resize-aot
./HdrProbe/bin/startup-resize-aot/HdrProbe.exe --startup-resize
./HdrProbe/bin/startup-resize-aot/HdrProbe.exe --presentation
./HdrProbe/bin/startup-resize-aot/HdrProbe.exe --reconstruction
dotnet publish WinExSpectrumTest.csproj -c Release -r win-x64 -p:Platform=x64 -p:WindowsPackageType=None -p:SkipReconstructionNativeBuild=true --no-restore -o bin/startup-resize-publish
```

本地忽略日志：`startup-resize-final.log`、`presentation.log`、`reconstruction-final.log`、`app-warmup-publish.log`、`probe-warmup-publish.log`。未启动主应用、操作设置窗口、截图、播放/采集真实音频或切换显示器状态；真实第二窗口拖动的闪烁改善、跨 DPI/壁纸切换、HDR 观感和 RTX 上 DLSS 预热仍由用户验收。拖动中临时拉伸的画面会稍软，稳定后恢复准确像素尺寸。

参考：[Intel XeSS 初始化及管线预构建说明](https://www.intel.com/content/www/us/en/developer/articles/guide/xe-super-sampling-developer-guide.html)、[Microsoft XAML 交换链缩放及重新绑定约定](https://learn.microsoft.com/en-us/windows/uwp/gaming/directx-and-xaml-interop)。

## 2026-09-28 初始实现记录

日期：2026-09-28。范围：音域回响、共享 GPU 宿主、设置持久化和厂商适配层；不含插帧。

## 实现边界

- 空间路径：FP16 线性场景及粒子 -> 共享亮度映射 / 感知域准备 -> FXAA 或 SMAA 1x -> SDR / HDR10 编码。关闭 AA 时直接走共享输出。SMAA 使用上游三阶段着色器和 Area / Search 查找表，不是以模糊替代算法。
- 时域路径：带 jitter 的 FP16 地形、D32 深度、RG16F 运动矢量、R8 响应掩码 -> XeSS-SR / FSR 3.1 / DLSS -> 输出分辨率粒子 -> 共享亮度映射及 SDR / HDR10 编码。XAML 媒体卡片不参与降采样或时域历史。
- `ITemporalGpuEffect` 规定输入状态、当前到上一帧的不含 jitter 的输入像素运动矢量、常规 0..1 深度及效果提供的近远面 / 垂直 FOV。高度场历史由效果维护，SDK 上下文由渲染宿主管理；销毁前等待渲染队列完成。
- `TemporalReconstruction` 管理 jitter 序列、历史重置和无反射 C ABI。`Native/Reconstruction` 只适配 SDK，不创建窗口、交换链、采集设备或帧循环。
- 支持原生分辨率 AA、质量、均衡、性能四档，内部尺寸由各 SDK 查询。时域模式下禁用手动渲染比例；不可用或初始化 / 执行失败回退 FXAA，保留用户持久化选择。切换算法、质量、效果、尺寸、暂停恢复及长帧间隔会重置历史。
- 本次没有建立任意 shader 的动态插件系统。新增空间后处理可复用共享线性输入和最终编码；新增时域效果还必须实现深度、运动和历史契约，不能仅提供一张颜色纹理。
- 最低画质仍为 50% 渲染比例、80 网格、关闭 AA。旧配置默认值不变，新设置 `SonicUpscaleQuality` 默认 `quality`，只有选择时域算法时生效。

## 构建和复现

厂商 SDK 所需的 22 个文件现纳入仓库；新检出工作区可直接构建，仍可在仓库根目录逐一验证 SHA-256：

```powershell
./External/Upscalers/Restore.ps1 -VerifyOnly
./Native/Reconstruction/Build.ps1
```

缺失文件时才运行 `Restore.ps1`，它从固定官方提交恢复并校验下载内容。

原生适配层需要 PowerShell 7、VS 2026 C++ x64 工具集 v145 和 Windows SDK。正常项目构建会验证 SDK 并构建适配层，不会隐式联网下载。复用已构建且 ABI 匹配的适配层时可加 `SkipReconstructionNativeBuild=true`。使用 `EnableVendorReconstruction=false` 可构建仅含空间 AA 的版本，必须使用干净输出目录，避免旧 DLL 影响能力探测。

以下命令工作目录为 `doc/verification`，使用该目录的 .NET 10 SDK 约束：

```powershell
dotnet publish HdrProbe/HdrProbe.csproj -c Release -r win-x64 -p:PublishAot=true -p:PublishTrimmed=true -p:SkipReconstructionNativeBuild=true -o HdrProbe/bin/reconstruction-aot
./HdrProbe/bin/reconstruction-aot/HdrProbe.exe --reconstruction
./HdrProbe/bin/reconstruction-aot/HdrProbe.exe --benchmark-reconstruction
```

RTX 设备上可先运行 `--device-capabilities` 确认探针选中的 GPU，再用
`--reconstruction --dlss-only` 单独验证 DLSS 四档；完整回归仍使用
`--reconstruction`。驱动缓存和 SDK 数据目录需可写，否则本机沙箱内的
SDK 能力探测曾持续运行约十分钟仍未返回。

本次主项目还执行了以下 NativeAOT 无包发布，只生成文件，未启动主应用：

```powershell
dotnet publish ../../WinExSpectrumTest.csproj -c Release -r win-x64 -p:Platform=x64 -p:WindowsPackageType=None -p:AppxPackage=false -p:GenerateAppxPackageOnBuild=false -p:EnableMsixTooling=false -p:AppxPackageSigningEnabled=false -p:PublishAot=true -p:PublishTrimmed=true -p:SkipReconstructionNativeBuild=true -o HdrProbe/bin/app-reconstruction
```

SDK 部署、固定版本、体积和发布许可要求见 `External/Upscalers/README.md`。可选 DLL 合计约 160 MiB；DLSS 的商标、署名、终端许可和商业发布通知仍需发布者按 SDK 许可完成。尤其在向公开远端推送 SDK 文件前应核对 NVIDIA 的 SDK 再分发条件。本次没有代为通知、申请许可审批或发布软件。

## 隔离范围与修正

最终探针全部使用 `new SpectrumAnalyzer(captureAudio: false)` 和预分配合成输入。GPU 交换链未绑定到任何窗口；未启动主应用 / 设置窗口，未做 UI 自动化、桌面截图、真实呈现 FPS 测量、壁纸切换或显示器设置调整。探针的着色器图像来自离屏资源回读。

首轮排查静止运动矢量时发现：旧探针的无参 `SpectrumAnalyzer` 构造函数会隐式启动 WASAPI 环回采集，导致真实输入混入测试，超出了约定范围。相关进程已结束，未保存音频文件，并已向用户说明。随后新增显式无采集入口、修改 HdrProbe 的全部构造点并重新执行完整 AOT 回归和基准。下述证据只采用修正后的结果，不能将首轮描述为全程隔离测试。约束已写入根目录 `AGENTS.md`。

## 已完成证据

设备：Intel(R) Arc(TM) 140T GPU；本机 SDK 能力位 `0x18`，XeSS 和 FSR 可用，DLSS 不可用。

| 检查 | 结果 |
| --- | --- |
| 原生 C++ Release x64、SDK 文件校验 | 通过，C / C# 帧描述 ABI 为 80 字节 |
| 主项目及 HdrProbe NativeAOT / Trimmed 发布 | 通过；不是仅 Debug build |
| 画质预设、原子通知、旧配置迁移、算法 / 质量 JSON 往返 | 通过，使用源生成序列化 |
| Off / FXAA / SMAA 的 RGB10 输出回读 | 黑、200 / 400 nit 白、1000 / 200 nit 峰值、SDR gamma / 裁剪通过 |
| XeSS 四档、FSR 3.1 四档真实 SDK 执行 | 通过，输出有限且非空，实际活动模式与请求一致 |
| DLSS 四档不可用回退 | 均回退 FXAA；这不是 RTX 上的 DLSS 执行证据 |
| 运动矢量 | 静止场景不含 jitter，移动相机产生非零运动，reset 后清零；最终一次移动样本为 1.358 像素 |
| 斜边回读 | Off 0、FXAA 1409、SMAA 1205 个部分覆盖灰阶像素；证明边缘处理被执行，不据此给算法画质排名 |
| 模式 / 质量切换、历史重置、奇数尺寸及多次 resize / 释放 | 通过，含 801x451 和 960x540 |
| 第二 GPU 效果、候选效果初始化失败及恢复 Sonic | 通过 |
| D3D12 调试层 | 上述回归未发现 Error / Corruption |
| 双语资源静态核对 | 两份 resw 键集合一致，新增 8 项匹配代码 / XAML 引用 |

构建仍有 nullable / obsolete、`SelectedValuePath="Tag"` 的 WMC1510、既有 `Assembly.Location` 的 IL3000、SharpGen 的 IL2104 和缺失默认 publish profile 的 NETSDK1198 警告。发布通过不能替代运行时 UI / XAML 绑定验收。

未用驱动故障强制触发设备移除，也未在支持的设备上注入厂商 SDK 内部故障；这两类恢复分支只有代码检查和相邻生命周期测试，不能声称已做完整故障注入。

## 1440p 离屏耗时

最终 AOT 基准：输出 2560x1440、网格 80、相机自动旋转、合成频谱；每档预热 120 帧后采样 480 帧，关闭 D3D12 调试层。Off / FXAA / SMAA 使用 50% 输入尺寸；XeSS / FSR 使用 SDK 的性能档，各自查询内部尺寸，不能视为完全相同的输入负载。

计时为 CPU 包围整个 `Render` 的耗时，包含提交、未绑定窗口的交换链 Present 调用和 GPU fence 等待；不是 GPU timestamp，也不是桌面实际呈现 FPS。托管分配为同一线程 `GC.GetAllocatedBytesForCurrentThread` 的差值，不覆盖原生 SDK 或其他线程。

| 模式 | 平均 ms | P95 ms | P99 ms | 托管 B / 帧 |
| --- | ---: | ---: | ---: | ---: |
| 关闭 | 1.242 | 2.474 | 2.850 | 0.1 |
| FXAA | 1.618 | 1.911 | 2.137 | 0.0 |
| SMAA 1x | 3.146 | 4.756 | 5.166 | 0.0 |
| XeSS 性能 | 6.352 | 8.369 | 11.312 | 0.0 |
| FSR 3.1 性能 | 6.587 | 7.275 | 7.772 | 0.0 |

这是单机短时采样，受系统负载和功耗状态影响。修正隔离后的前一轮 XeSS / FSR 平均为 6.437 / 7.709 ms，说明不宜把一次测量当作稳定性能承诺。不能据此保证 1440p / 120 FPS，也不能宣称整个应用绝对零分配。

该场景的基础渲染已经较轻，时域重建的固定开销明显；最低档继续关闭 AA，没有自动把 XeSS / FSR 设为性能默认值。重建模式提供画质选择，不保证对所有设置都提升速度。

## RTX 5070 Ti 离屏补充验证（2026-09-28）

设备：NVIDIA GeForce RTX 5070 Ti，驱动 616.64。使用同一 NativeAOT / Trimmed
HdrProbe、`SpectrumAnalyzer(captureAudio: false)` 及合成频谱输入，未启动主应用、
设置窗口或真实音频采集。直接能力查询选中了 RTX 5070 Ti，能力位为 `0x38`
（XeSS、FSR、DLSS）。沙箱内首次能力探测长时间未返回；允许访问驱动缓存和
SDK 数据目录后约一秒完成。随后单独的 DLSS 四档和开启 D3D12 调试层的
完整离屏回归均以退出码 0 结束。

| DLSS 档位 | 实际活动模式 | 960×540 离屏回读 | 调试层回归 CPU 包围 Render 耗时 |
| --- | --- | --- | ---: |
| 质量 | DLSS | 有限、非空 | 0.770 ms / 帧 |
| 原生分辨率（DLAA） | DLSS | 有限、非空 | 0.845 ms / 帧 |
| 性能 | DLSS | 有限、非空 | 0.752 ms / 帧 |
| 均衡 | DLSS | 有限、非空 | 0.756 ms / 帧 |

每档预热 32 帧、计时 32 帧；上述短时 CPU 耗时包含提交与 fence 等待，
不代表显示器呈现 FPS 或 GPU timestamp。完整回归还通过历史重置、
801×451 奇数尺寸缩放及恢复、运动矢量、第二效果切换和资源释放，
未报告 D3D12 Error / Corruption。各档计时循环同线程托管分配读数为
0.0 B / 帧；不代表整个应用或原生 SDK 零分配。离屏回读不能证明
运动抗锯齿画质或 HDR 显示观感。

## 2026-09-28 RTX 5070 Ti：2160p 分段诊断

用户观察到 DLSS / XeSS 的每帧耗时和功耗高于 SMAA，FSR 的功耗变化较小。
本次只诊断和扩展离屏探针，未修改生产渲染路径、算法默认值或用户设置。

### 测量方法

- 新入口 `HdrProbe --profile-pipeline`；可加 `--reverse` 反转测试顺序、`--grid320` / `--grid80` 改变网格。每档新建无捕获的 `SpectrumAnalyzer(captureAudio: false)`、效果和宿主，绑定回调为空。
- RTX 5070 Ti；3840×2160 输出，默认网格 160，SDR 输出，相机自动旋转、合成特征与节拍。相机和音频时间序列相同，但效果内部的随机涟漪/粒子种子未固定，不是逐像素相同的场景。
- Release NativeAOT，D3D12 调试层关闭；每档预热 120 帧，采样 360 帧；模拟步长 1/120 秒，循环不限速。这不是 120 Hz 实屏测量，也不是等帧率功耗测试。
- `PipelineTiming.cs` 仅编译进探针，复用实际效果、着色器和 SDK，在与 `Render` / `ComposeAndPresent` 相同的成功路径上插入 D3D12 timestamp。SDK 错误直接失败，不把回退作为目标模式的成绩。未来生产路径改变时，应同步这个探针副本。
- GPU 分段为 scene、SR、overlay、output。scene 包含清理、地形和历史拷贝；空间 AA 模式的粒子在 scene 内。SR 含 SDK 调用及入口资源屏障；output 含空间 AA 的准备/算法和最终编码。GPU 合计仅覆盖 direct 队列，不含先行的 ComputeSharp 高度场计算、CPU 录制、Present、fence 唤醒或桌面合成。
- CPU 另测 PrepareFrame、命令录制、提交/Present/fence。每档随后调用未修改的生产 `Render` 360 帧交叉检查；这一阶段的模拟时间稍后，不能按逐帧完全一致比较。

### 结果

下表是网格 160、反向顺序复测的均值，单位 ms。SR 均为 SDK 性能档；SMAA 同时覆盖 50% 和原生输入。

| 模式 | 实际输入 | scene GPU | SR GPU | overlay GPU | output GPU | direct GPU 合计 | 原始 Render CPU 包围耗时 |
| --- | --- | ---: | ---: | ---: | ---: | ---: | ---: |
| Off 50% | 1920×1080 | 0.0676 | 0 | 0 | 0.0717 | 0.1394 | 0.3679 |
| FXAA 50% | 1920×1080 | 0.0668 | 0 | 0 | 0.1480 | 0.2148 | 0.4557 |
| SMAA 50% | 1920×1080 | 0.0667 | 0 | 0 | 0.3535 | 0.4202 | 0.6891 |
| SMAA 原生 | 3840×2160 | 0.1711 | 0 | 0 | 0.3619 | 0.5330 | 0.8066 |
| FSR 性能 | 1920×1080 | 0.0802 | 0.6498 | 0.0002 | 0.0962 | 0.8264 | 1.2009 |
| XeSS 性能 | 1670×940 | 0.0716 | 1.6825 | 0.0002 | 0.0877 | 1.8421 | 2.3629 |
| DLSS 性能 | 1920×1080 | 0.0829 | 2.0580 | 0.0002 | 0.1796 | 2.3207 | 2.7977 |

正向顺序首轮的 SR GPU 均值：FSR 0.7062、XeSS 1.9338、DLSS 2.7647 ms；SMAA 原生 direct 合计 0.6003 ms。CPU 耗时比复测波动大，不能把差值全部归因于 GPU fence 或用它代替 GPU timestamp。未锁定频率、功耗状态和其他系统负载，数值不是稳定性能承诺，但两轮瓶颈排序一致。

网格提高到 320 后，反向复测 scene / SR GPU 分别为：DLSS 0.2096 / 2.3520 ms，XeSS 0.1748 / 1.6320 ms，FSR 0.1884 / 0.6361 ms；SMAA 原生 direct 合计 0.6710 ms。附加几何、运动矢量和历史拷贝仍不是本场景主要开销。该轮 DLSS 后续未插桩 CPU 对照为 7.7877 ms，明显高于插桩阶段 CPU 3.8025 ms，保留此异常而不把它解释成确定的管线成本。

三轮采样循环同线程托管分配均为 0.0 B/帧；不代表整个应用、其他线程或 SDK 原生分配为零。粒子 overlay 在这些合成轨迹中很轻，不是密集粒子的性能上限。

### 结论与优化方向

1. **主要增量是 SDK 重建，而场景着色已很轻。** `GpuGraphics.Render` 将低分辨率场景交给 SDK 后，在 4K 上叠加粒子并编码输出。原生 SMAA 的 scene 仅约 0.17 ms，降低输入分辨率无法抵消 0.65–2.76 ms 的 SR 成本。`SonicGpuEffect.RecordScene` 仍绘制 `30 × grid²` 个顶点，输入分辨率降低也不会减少柱体数量。
2. **不存在 SR 与 SMAA/FXAA 重复执行。** `CreateSceneResources` 只为活动 FXAA/SMAA 分配 `_antialias`；时域路径使用独立 `_reconstructed`。SDK 上下文只在配置/尺寸改变时创建，不是每帧初始化。
3. **DLSS 性能档不是“最便宜的神经网络”承诺。** 本地 release DLL 版本为 310.9.1.0。桥接只设置输入质量档，未显式指定模型 preset；随仓库固定的 `nvsdk_ngx_defs.h` 将性能档默认标为 M，质量/均衡/DLAA 默认为 K，并说明默认行为可受 OTA 影响。可在后续独立实验中对照 K/M 的成本和运动画质；本次没有捕获驱动实际选择的 preset，也没有更改全局驱动设置。
4. **XeSS 的跨厂商路径有额外计算成本。** [Intel 的开发指南](https://github.com/intel/xess/blob/main/doc/xess_sr_developer_guide_english.md)说明 SR 是一系列计算着色器；[Intel 的架构说明](https://www.intel.com/content/www/us/en/developer/articles/technical/xess-velocity-and-luminance-adaptive-rasterization.html)区分 XMX 与 DP4a 路径。本机为 NVIDIA，不能把 Intel XMX 的成本假设套用到此处。实际性能档输入是 1670×940，不能把各家同名档位视为同样的输入负载。
5. **宿主有共用的串行化开销，值得单独优化。** `PrepareHeightField` 的上传及 ComputeSharp 同步 context 在绘制前完成；`ComposeAndPresent` 每帧 `WaitForGpu`，下一帧才会复用 allocator、上传区和纹理。网格 160 反向复测 Prepare CPU 为约 0.13–0.31 ms，另有录制和提交/唤醒成本。可考虑同队列记录高度场、GPU 队列依赖以及每帧 allocator/上传缓冲轮转，但不能直接删除等待，否则当前单份资源会被在途帧覆盖。这些改造的收益尚未测量，也不会省去 SDK 内部计算。
6. **功耗解释与实测分开。** 更长的 GPU 执行和不同运算单元负载，可以解释 DLSS/XeSS 更耗电的观察；[NVIDIA 对第二代 Transformer 的说明](https://www.nvidia.com/en-us/geforce/news/dlss-4-5-super-resolution-available-now/)也明确其计算工作量更高。但本次没有采集瓦数、锁定实际显示帧率或隔离其他 GPU 客户端，不能量化功耗差异或证明具体型号的能效。

复测命令（工作目录 `doc/verification`）：

```powershell
dotnet publish HdrProbe/HdrProbe.csproj -c Release -r win-x64 -p:PublishAot=true -p:PublishTrimmed=true -p:SkipReconstructionNativeBuild=true -o HdrProbe/bin/pipeline-profile-aot
./HdrProbe/bin/pipeline-profile-aot/HdrProbe.exe --profile-pipeline
./HdrProbe/bin/pipeline-profile-aot/HdrProbe.exe --profile-pipeline --reverse
./HdrProbe/bin/pipeline-profile-aot/HdrProbe.exe --profile-pipeline --grid320 --reverse
```

NativeAOT 发布通过；保留 SharpGen.Runtime 既有 IL2104 裁剪警告。三轮日志位于 `HdrProbe/bin/pipeline-profile-aot/grid160-forward.log`、`grid160-reverse.log`、`grid320-reverse.log`（本地产物，不入库）。静态差异检查通过。未启动主应用、操作 UI、采集真实音频、测量实屏 FPS 或验证显示器 HDR/运动画质。

## 2026-09-28 统一比例、显式 DLSS preset 与刷新率控制（首次实现）

本节是上述瓶颈调查之后的实现和复测；上一节的 SDK 质量档、默认 preset 和输入尺寸代表改动前状态。

**更正：本节首次实现把 SDK 推荐/动态分辨率范围误作固定输入限制，导致 DLSS 50% 以下和 XeSS 33% 以下被宿主提前回退。下述回退记录不是算法输入下限的证据；此问题已在下一节修复。**

### 行为与 SDK 契约

- 所有 AA/SR 共用 1%–100% 整数渲染比例滑块，输入宽高分别取 `max(1, floor(output × scale / 100))`。独立重建质量控件已移除。滑块停止变化 150 ms 后由渲染线程应用最后的比例，拖动期间继续绘制旧尺寸，减少 SDK 初始化和资源重建。
- 新保存的 `SonicResolutionVersion=1` 使用统一比例；缺少版本的旧 SR 配置按原质量档迁移为近似百分比，保留 `SonicUpscaleQuality` JSON 字段名。新 `SonicDlssPreset` 默认 K，可选 J/K/L/M，画质预设切换保留所选 DLSS 模型。
- 首次桥接遍历 SDK 质量枚举，查询 min/max 并过滤输入；无可用范围时返回 `-30`，宿主暂用 FXAA。后续实测证明，这个提前过滤会拒绝 SDK 本来可以执行的固定输入，已移除。
- 首次实现参考了 [XeSS 指南](https://github.com/intel/xess/blob/de0fb9c1c510661c571164e1418ceca8101dab69/doc/xess_sr_developer_guide_english.md)和 [DLSS 指南](https://github.com/NVIDIA/DLSS/blob/374959484e79a640feaba44c93ac8cfb0a03f5b5/doc/DLSS_Programming_Guide_Release.pdf) §3.2.2，却未区分同一上下文逐帧改变输入的 DRS 与本项目每次比例变化都会重建的固定尺寸上下文。将 optimal 创建尺寸和 DRS min/max 强套到后者，是本次回归的根因。
- DLSS 在所有 NGX 质量档的 preset 参数中显式设置 J/K/L/M。设置页显示应用请求的模型，未声称能读回驱动最终选用的模型；驱动覆盖仍可能影响结果。
- 刷新率下拉框提供 60/72/80/120/144/160/240/288/320/480 Hz 和无限帧率，旧的非标准有限值保留为额外选项。零表示取消应用定时等待，有限档移除旧的 120 Hz 截断。Win2D 无限档使用可变时间步，实际 Draw 仍受 Win2D/合成器调度影响。
- 只读参考了 `G:\SoftwareProject\winui\ComputeSharpDemo` 的呈现循环。其帧槽纹理和 allocator 轮转与本工程单份资源所有权不同；本次保留 `WaitForGpu`，无限档不等于取消在途资源同步，也不承诺屏幕呈现无限 FPS。参考工程未作修改。

### 已完成验证

- C++ 桥接、HdrProbe 与主工程无包 NativeAOT 发布通过。主工程使用独立 `obj/sr-controls/` 中间目录，避免旧 XAML 生成文件引用已移除的属性；主应用未启动。DLSS 与刷新率新下拉框使用强类型 `SelectedIndex` 绑定。
- AOT 源生成 JSON 覆盖全部算法、J/K/L/M、旧质量档迁移及新比例往返；比例覆盖 1/2/33/37/99/100，奇数和最小尺寸；刷新率覆盖全部档位和旧 90 Hz。
- 独立帧定时探针：120 Hz 测得约 119.99 waits/s，稳态托管分配 0 B；480 Hz 未被截断为 120 Hz，连续 1000 次无限档调用无定时等待，设置/停止信号可中断等待。这是定时器测试，不是实际显示帧率采集。
- 首次 D3D12 调试层测试在 960×540 输出覆盖三种 SR 的 100/75/50/34/33/1/67 比例；空间 AA 覆盖 75/1。XeSS 的 33%/1% 与 DLSS 的 34%/33%/1% 被宿主提前回退；FSR 包括 1%（9×5 输入）均执行成功。旧断言允许回退输出通过，未证明这些比例真的不受 SDK 支持，后续已改为活动算法必须与请求算法一致。
- 回退后调回 67% 均恢复；历史重置、801×451 奇数 resize 后恢复原尺寸、运动矢量、空间 AA 斜边及第二效果契约通过。DLSS 在同一宿主按 J→K→L→M→K 切换，使用 63%（604×340）输入，回读与 D3D12 调试检查通过。
- 两份 resw 键集合相同、无重复；新增 XAML `x:Uid` 与独立 `GetString` 资源键静态匹配，差异空白检查通过。既有 SharpGen 裁剪警告、其他下拉框的 XAML 属性路径提示、nullable/obsolete 等警告仍保留。

### DLSS preset 性能复测

RTX 5070 Ti，NativeAOT，网格 160，合成音频，输入统一 1920×1080，输出统一 3840×2160；关闭调试层，每档预热 120 帧，采样 360 帧，之后原始生产 `Render` 再运行 360 帧。正向 M/K/J/L、反向 L/J/K/M。单位 ms，GPU 数据来自 direct 队列 timestamp，不包含前置 ComputeSharp 阶段。

| 请求 preset | 正向 SR GPU | 反向 SR GPU | 反向 direct GPU 合计 | 反向生产 Render CPU 包围耗时 |
| --- | ---: | ---: | ---: | ---: |
| J | 1.3548 | 1.3615 | 1.5332 | 1.9901 |
| K（默认） | 1.3568 | 1.3594 | 1.5316 | 2.0340 |
| L | 2.4434 | 2.4752 | 2.6515 | 3.1526 |
| M | 1.9223 | 1.9355 | 2.1697 | 2.6350 |

K 相比 M 的 SR GPU 时间两轮均降低约 29%–30%，反向生产 Render CPU 包围耗时降低约 23%。J 与 K 的成本接近。所有 preset 采样循环同线程托管分配为 0.0 B/帧。未锁 GPU 频率或隔离其他系统负载；首轮部分时间与主工程发布重叠，反向复测时该发布已完成。

因此显式模型选择有实测收益，但 K 的 SR 阶段本身仍高于此前原生 SMAA 的完整 direct 队列时间（约 0.53 ms）。这不是消除了 SR 的固有开销，也未量化省电幅度。场景已有的 ComputeSharp 同步和每帧 fence 等待仍存在，多帧资源轮转是另一个需要独立验证的优化方向。离屏有限/非空回读不证明模型运动画质一致。

复测命令（工作目录 `doc/verification`）：

```powershell
dotnet publish HdrProbe/HdrProbe.csproj -c Release -r win-x64 -p:PublishAot=true -p:PublishTrimmed=true -p:SkipReconstructionNativeBuild=true -o HdrProbe/bin/sr-controls-aot
./HdrProbe/bin/sr-controls-aot/HdrProbe.exe --quality-controls
./HdrProbe/bin/sr-controls-aot/HdrProbe.exe --reconstruction
./HdrProbe/bin/sr-controls-aot/HdrProbe.exe --profile-pipeline --dlss-presets
./HdrProbe/bin/sr-controls-aot/HdrProbe.exe --profile-pipeline --dlss-presets --reverse
```

完整日志位于本地忽略目录 `HdrProbe/bin/sr-controls-aot/`：`reconstruction.log`、`presets-forward.log`、`presets-reverse.log`、`app-publish.log`。未启动主应用、操作 UI、采集真实音频、测量实屏 FPS/功耗或切换显示器设置；滑块/下拉框实机交互、无限档实际呈现、HDR 和运动画质仍由用户验收。

## 2026-09-28 低比例与 16×9 输入回归修复

用户指出 Ultra Performance 约为三分之一输入，并给出了 16×9 输入仍可运行 DLSS 的反例。重新验证确认上一节的输入限制解释错误，且提前过滤是本次实现引入的功能回归。

### 复现与根因

- RTX 5070 Ti、仓库固定的 SDK、D3D12 调试层、无音频采集探针。原始 DLSS 查询在 4K 输出时：Quality/Balanced/Performance 的 min 均为 1920×1080；Ultra Performance 的 optimal/min/max 均为 1280×720。960×540 输出同样为普通档最低 480×270、Ultra Performance 固定 320×180。
- 旧范围过滤因此拒绝 49%/40%/34%，也拒绝整数 33% 对应的 1267×712，而精确 1280×720 能创建。这与输出窗口太小无关；4K 同样复现。
- 强回归断言在修复前明确失败：960×540、49% 实际活动模式为 FXAA。此前只检查回读有限/非空并允许 `UnsupportedScale`，会把回退成功错当作验证完成。
- DLSS 改为用实际输入尺寸创建固定上下文，Evaluate 继续传同样尺寸；推荐查询仅选择最接近的质量初始化提示。XeSS 独立实验也证明推荐范围之外能 Execute，因此一并取消宿主提前过滤。保留真实 SDK 错误处理；DLSS 文档的 32×32 最低**输出**尺寸检查与输入下限无关。

### 最终离屏验证

- 输出分别为 960×540、3840×2160。DLSS/XeSS/FSR 覆盖 50/49/40/34/33/32/10/1/67/75/90/99/100%，输入尺寸必须精确等于滑块计算值；没有将 33% 偷换成精确三分之一或上调到 50%。
- 三者另测精确三分之一，以及 **16×9** 输入；DLSS 的 16×9 在 J/K/L/M 四个请求 preset 下分别执行。每例经过真实 SDK、生产资源创建与 Render、GPU 完成等待、输出回读和 D3D12 调试检查，断言活动算法必须等于请求算法、输出非空且数值有限。
- 16×9 是探针直接指定的像素尺寸，不改变产品 1%–100% 滑块范围。4K 的 1% 仍为 38×21。
- 极低比例 XeSS 的输出亮度明显下降；这些测试证明执行成功，不证明极端输入的视觉质量。推荐范围外的跨设备/跨版本表现也不能从本机一次实测推广为无条件保证。
- 原有模式切换、历史重置、奇数尺寸恢复、运动矢量、空间 AA 和第二效果回归重新执行。C++ 桥接、探针与主工程 NativeAOT 发布通过；保留既有构建警告。临时 `[DEBUG-*]` 插桩已移除。

命令（工作目录 `doc/verification`）：

```powershell
./HdrProbe/bin/dlss-size-aot/HdrProbe.exe --dlss-resolutions
./HdrProbe/bin/dlss-size-aot/HdrProbe.exe --xess-resolutions
./HdrProbe/bin/dlss-size-aot/HdrProbe.exe --fsr-resolutions
./HdrProbe/bin/dlss-size-aot/HdrProbe.exe --reconstruction
```

本地忽略目录 `HdrProbe/bin/dlss-size-aot/` 保留 `baseline.log` 原始 SDK 查询、`regression-before.log` 失败断言、三个 `*-resolutions-final.log`、`reconstruction-final.log` 及 `app-publish.log`。未启动主应用、采集真实音频或进行实机呈现/功耗/画质验收。

## 留给用户的实机验收

- Arc 140T 上真实 1440p / 120 FPS、持续运行功耗、帧时间稳定性和桌面合成负载。
- SMAA / XeSS / FSR 的运动细节、闪烁、重影、音频突变及粒子观感；离屏非空和有限值不等于感知画质合格。
- 设置页选择、可用性 / 回退提示、重启持久化、多 DPI、壁纸 / 小组件切换及 HDR 显示观感。
- RTX 设备上的 DLSS / DLAA 运动画质、模式切换 UI 与实际显示观感；离屏执行和生命周期已在上节验证。
- MSIX 打包 / 安装 / 发布验收及厂商发布许可要求；本次仅检查共享 Content 声明和无包发布输出，没有执行 MSIX 安装或认证。
