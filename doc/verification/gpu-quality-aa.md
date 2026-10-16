# GPU 效果、画质与 FXAA 实现记录

按时间记录 GPU 宿主、画质设置与验证证据。2026-09-28 的初始记录保留在下方历史部分。

## 2026-10-17 通用 GPU 配置与覆盖层计时

- 运行时配置为 `AppSettings.GpuQuality` / `GpuQualitySettings`；音域回响的 `SonicGridSize` 保留为效果私有细节。`SaveSetting.SonicRenderScalePercent` / `SonicAntiAliasing` / `SonicDlssPreset` 的旧 JSON 名称保持不变，仍执行 `SonicResolutionVersion` 的旧比例迁移。
- `EffectDescriptor` 仅允许效果提供可选 `GetDetail`。`CanvasPanel` 统一通过 `GetSceneOptions()` 取得通用配置，新增 GPU 效果无需再手动接 AA / DLSS / 渲染比例。深度与运动矢量仍属于 `ITemporalGpuEffect` 契约；没有时域输入时回退到 FXAA。
- 抗锯齿、DLSS 模型、渲染比例与独立状态卡移入通用设置，三项各有说明；GPU 画质不作用于 Win2D 效果。音频响应强度滑块上限由 250 提高到 400。主窗口最大化旁新增壁纸按钮，与托盘绑定同一个 ViewModel 命令和运行时状态。
- 覆盖层继续每 500 ms 在 UI 线程格式化文本；渲染线程只记录值。新增 GPU 时间戳每 250 ms 至多采样一次，复用一对 query 和常驻映射的 16 字节 readback；只在 post 队列 fence 已完成后读取，不为诊断新增 GPU / CPU 等待。关闭后停止计时、数值记录与 UI 定时器，已分配的查询资源在渲染器排空后释放。
- SMAA 计时包含输出尺寸准备和 SMAA passes；XeSS / FSR / DLSS 仅覆盖重建，不含后叠加粒子。FXAA 已融合输出编码，显示为“抗锯齿＋输出 GPU”，不改变渲染链路来拆分它。实现依据 [D3D12 timestamp 文档](https://learn.microsoft.com/en-us/windows/win32/direct3d12/timing)。

验证：主工程 Debug 构建、主工程 Release NativeAOT/Trimmed 发布及探针 NativeAOT/Trimmed 发布通过。保留既有 nullable、旧 API、IL3000、SharpGen IL2104、缺失发布配置及 Satori 实验性提示。初次发布遇到旧还原缓存的 10.0.12 / 10.0.13 不一致，使用本机缓存重新还原后验证；没有修改项目依赖版本。

离屏探针使用 `SpectrumAnalyzer(captureAudio: false)` 与合成频谱，交换链未绑定窗口。D3D12 调试层下，全部五种 AA / 重建算法取得有效时间戳；关闭 AA 无样本。另一个最小 GPU 效果正确继承通用比例与 SMAA，并在缺少时域输入时回退。计时开关、尺寸 / 算法切换、样本只消费一次、稀疏样本均值、双队列重叠与注入 SDK 失败后的排空 / 回退均通过。探针的预期 `Temporal reconstruction dispatch failed (-1)` 日志来自主动注入的失败场景。

两种离屏呈现标志路径（允许 tearing / 兼容模式）的 resize、排空、回读及 0 B 热循环检查也通过。中英文资源静态检查覆盖键集合一致性、独立字符串键、覆盖层 16 个格式参数以及附加属性的完整命名空间语法（[WinUI 本地化说明](https://github.com/MicrosoftDocs/windows-dev-docs/blob/docs/hub/apps/windows-app-sdk/mrtcore/localize-strings.md)）。

命令（工作目录 `doc/verification`，使用其中的稳定 SDK 配置；NativeAOT 发布仅生成文件）：

```powershell
$env:DOTNET_CLI_TELEMETRY_OPTOUT='1'
$env:NUGET_SCRATCH=(Join-Path (Resolve-Path ../..) 'obj/nuget-scratch')
dotnet publish HdrProbe/HdrProbe.csproj -c Release -r win-x64 --no-restore -p:PublishAot=true -p:PublishTrimmed=true -p:SkipReconstructionNativeBuild=true -o HdrProbe/bin/gpu-common-aot
./HdrProbe/bin/gpu-common-aot/HdrProbe.exe --quality-controls
./HdrProbe/bin/gpu-common-aot/HdrProbe.exe --debug-timing --debug
./HdrProbe/bin/gpu-common-aot/HdrProbe.exe --debug-timing
./HdrProbe/bin/gpu-common-aot/HdrProbe.exe --presentation
```

实机待验收：中英文设置页布局 / 状态 / 保存重开、响应强度 400、标题栏壁纸按钮与托盘恢复及窗口尺寸 / DPI 下命中区域。未启动主应用、操作桌面、采集真实音频或切换显示器模式。以下离屏耗时不能当作屏幕 FPS，也不能证明 XAML 覆盖层合成完全没有开销。

### 离屏性能证据（2026-10-17）

RTX 5070 Ti，.NET 10 NativeAOT，960×540 输出、67% 输入、网格 80、合成频谱、SDR。关闭 D3D12 调试层；每组预热 240 帧、记录 6000 帧，按关 / 开 / 开 / 关顺序运行，两端排空 GPU。下表为同状态两组的平均 `Render` 墙钟耗时（含末尾排空摊销），没有启动主应用或呈现到屏幕。

| 算法 | 统计关闭 ms / 帧 | 统计开启 ms / 帧 | 开启组实际 GPU 样本数 |
| --- | ---: | ---: | --- |
| Off | 0.21903 | 0.21736 | 0 / 0 |
| Fxaa | 0.22103 | 0.22276 | 5 / 6 |
| Smaa | 0.23730 | 0.23819 | 6 / 5 |
| Fsr | 0.33783 | 0.33694 | 8 / 8 |
| XeSS | 0.44173 | 0.44007 | 10 / 11 |
| Dlss | 0.47486 | 0.46760 | 11 / 11 |

24 组渲染热循环均测得 0 B 当前线程托管分配；单独数值记录约 18.44 ns / 次。当前场景未见一致的离屏耗时回归，不能据此宣称零开销：功耗 / 频率及系统调度未固定，且未包含 UI 线程格式化、XAML 布局 / 合成和屏幕呈现。覆盖层文本保持 500 ms 一次；GPU timestamp 本身也有成本，250 ms 低频采样只将它限制在少量帧中。逻辑长度为 16 字节的 readback 实际 GPU 内存分配由 D3D12 对齐规则决定。

原始日志：`gpu-common-timing-benchmark.log`、`gpu-common-aot-debug.log`、`gpu-common-quality.log`、`gpu-common-build.log`、`gpu-common-publish.log`（本地产物，不入库）。

## 历史行为（2026-09-28）

当次用户在验证过程中要求停止后续验证、由用户自行验收；本节只记录当时停止前实际取得的证据。

| 档位 | 内部渲染比例 | 地形网格 | 抗锯齿 |
| --- | --- | --- | --- |
| 性能 | 50% | 80×80 | 关闭 |
| 均衡 | 75% | 120×120 | FXAA |
| 高 | 100% | 160×160 | FXAA |
| 自定义 | 由独立选项决定 | 由独立选项决定 | 由独立选项决定 |

- 比例按每个轴缩放，因此性能档在 2560×1440 输出时，内部场景为 1280×720；这是升采样输出，不是原生 1440p。
- 档位由实际参数匹配得出，不再单独保存容易失配的档位字段。手动改变参数后，不匹配预设即显示自定义。
- 新增 JSON 字段为 `SonicRenderScalePercent`、`SonicAntiAliasing`。旧配置默认为 100%、关闭 AA，已有 `SonicGridSize` 保留。
- 档位不会修改刷新率、HDR 开关、白点、峰值、涟漪/流星开关。120 FPS 仍须把现有刷新率选项设为 120。
- 主窗口输出尺寸与媒体覆盖层保持物理像素/DPI 规则；仅 GPU 场景按比例缩小。FXAA 准备图按输出尺寸分配，关闭时不分配该图。

## 接入更多 GPU 效果

1. 实现 `IGpuVisualizerEffect`：在渲染线程初始化和调整尺寸，`PrepareFrame` 完成私有 compute 工作，`RecordScene` 向宿主提供的 FP16 RTV 记录绘制。
2. 场景输出必须是不透明的线性 Rec.709，参考白为 1，允许高光超过 1。效果负责自身 PSO、descriptor heap、深度和私有资源屏障，不负责交换链或输出编码。
3. 在 `EffectRegistry` 增加稳定 ID、本地化键、`EffectHost.Gpu`、静态创建工厂、可选的 `GetDetail`（仅效果细节参数）和是否使用媒体覆盖层；通用渲染比例、抗锯齿和 DLSS 预设由宿主统一读取 `AppSettings.GpuQuality`。增加效果不需要在 `CanvasPanel`、`GpuRenderer` 或 `GpuGraphics` 增加按 ID 判断的分支。
4. 添加两个语言资源中的显示名称及效果自己的设置项。不要改变已发布的两个效果 ID。

公共宿主负责设备、直接队列、交换链、FP16 目标、缩放、FXAA、SDR/HDR10 输出和帧节奏；`SonicGpuEffect` 独占地形/粒子绘制资源。`IVisualizerEffect` 仍属于 Win2D。此抽取不是运行时 shader 插件系统或通用后处理图，新后处理算法仍需在公共输出阶段明确接入。

## 已有证据

- 实际探针设备：Intel(R) Arc(TM) 140T GPU (15GB)，驱动 32.0.101.8826。名称中的 15GB 不等同独立显存；系统当前桌面是 2880×1800 / 120 Hz。
- Debug 编译通过；Release NativeAOT 无包发布通过。第一次使用旧还原缓存发生 10.0.0 AOT 与 10.0.12 运行库不匹配，重新还原后发布成功，未为此修改项目包版本。保留既有可空性、XAML 路径、Assembly.Location 及 SharpGen 裁剪警告。
- `HdrProbe` 通过旧配置默认值、三个档位归一化和 JSON 回写、一次性设置通知、自定义匹配、非法值边界、奇数/最小内部尺寸检查。
- AA 关闭和开启均通过真实 RGB10 回读：黑色、200/400 nit 白点、1000/200 nit 峰值及 SDR gamma/裁剪。
- 第二个最小 GPU 效果可接入并恢复 Sonic；初始化失败的候选效果被释放且不替换旧效果；内部尺寸独立于交换链；重复创建/缩放/SDR-HDR切换/释放通过。
- 预热后的探针渲染及高精度帧等待测得 0 B/帧托管分配；这不等于整应用、冷启动、设置变更或驱动零分配。
- 发布版设置页实际显示了性能、关闭、50%、80×80，中文标签无原始资源键。主屏未启用 HDR，界面报告 SDR 回退。完整档位联动回归未完成，lvt 在弹出框切换后遇到失效的 UIA RuntimeId，未将其认定为产品故障或通过证据。
- 新旧场景回读对照发现并修正面朝向后，主体几何与颜色一致；一次 960×540 / 160 网格强特征静态对照，最大通道误差均值 0.00488，误差大于 0.05 的像素约 0.96%。不同 GPU 算术路径的闪点不是逐像素一致。此对照早于最后一次高度场常量外提，完整视觉回归仍待验收。

### 已测离屏耗时

下表为 2560×1440 交换链输出、自动旋转，360 帧预热后 1200 帧样本的 `Render` 调用墙钟时间，包含 GPU 完成等待。强特征由探针注入原分析器缓冲和节拍/流星计数，不等于真实 WASAPI 音乐回归。测量期间有发布工作，供电、功耗和温度未固定。没有 GPU timestamp、PresentMon 显示帧率或长时稳定性证据。

| 档位/场景 | 均值 ms | P95 ms | P99 ms |
| --- | ---: | ---: | ---: |
| 性能，静音 | 1.214 | 1.845 | 2.170 |
| 均衡，静音 | 2.612 | 3.172 | 3.627 |
| 高，静音 | 3.302 | 4.024 | 4.628 |
| 100% / 160 / AA 关闭，静音 | 2.178 | 2.906 | 3.374 |
| 性能，强特征 | 1.184 | 1.793 | 2.114 |
| 均衡，强特征 | 2.584 | 3.152 | 3.535 |
| 高，强特征 | 3.245 | 3.915 | 4.336 |
| 100% / 160 / AA 关闭，强特征 | 2.430 | 3.242 | 3.688 |

改动前静音 1440p / 160 网格的 120 帧样本均值为 10.703 ms、P95 11.696 ms，样本数与新路径不同，只能作为初步参考。所有数据都不是实屏 FPS；最低档满足 1440p / 120 FPS 的目标尚未完成验收，档位也尚未进行向上画质校准。

## 留给用户的验收范围

- 本机 2560×1440 输出、刷新率 120、性能档，在真实音频、闲置波、涟漪和流星场景中的实际呈现与帧时间。
- 档位联动、自定义状态、重启保存、FXAA 运动边缘/HDR 高光、缩放/DPI 与媒体卡片对齐。
- 快速效果切换、壁纸模式、最小化恢复、设置连续变化、启动中退出与设备丢失。
- 实际 HDR 显示、其他显卡、不同电源/温度状态、MSIX 安装包及长时运行。

收到“无需验证”后已停止测试并结束独立验证应用。最后的日志命名和 HDR 文案收尾未重新构建。


## 2026-09-28 无限呈现与调试覆盖层

本节记录后续修复；上方早期“已停止验证”和旧画质预设描述是历史状态。当前默认值为 100% + FXAA，画质预设卡片已移除。

### 调用链差异与修复

用户反馈无限档仍与窗口刷新率一致。只读对照 `G:\SoftwareProject\winui\ComputeSharpDemo` 的 `HdrSwapChainRenderer`，发现本项目此前仅移除了应用定时等待，交换链仍为 `Flags=None`，Present 没有 `ALLOW_TEARING`，且每帧 fence Signal 在 Present 后。

- 修复前离屏配置断言明确失败：RTX 5070 Ti 的 DXGI factory 报告 tearingSupported=True，而实际交换链 flags=None。
- 现在查询 factory tearing 支持，为支持的 composition chain 启用 `FrameLatencyWaitableObject | AllowTearing`，显式设置 `MaximumFrameLatency=2`。渲染线程不等待 frame-latency handle；该句柄用 SafeWaitHandle 释放。若 composition 拒绝 tearing 标志，使用关闭 tearing 的兼容链并在覆盖层显示该状态。
- Present 使用 `SyncInterval=0`、`DoNotWait` 和匹配创建标志的 `AllowTearing`；resize 保留原 flags。[Microsoft 的 D3D12 交换链文档](https://learn.microsoft.com/en-us/windows/win32/direct3d12/swap-chains)和 [VRR 文档](https://learn.microsoft.com/en-us/windows/win32/direct3ddxgi/variable-refresh-rate-displays)说明了这组支持查询、创建、呈现与 resize 约束。
- 单帧资源完成 fence 在 ExecuteCommandLists 后、Present 前 Signal，随后仍等待该 fence 才复用场景、上传区及 allocator。resize/释放仍执行完整队列 drain。没有直接复制参考工程的多帧纹理环，也没有删除单份 SR/效果资源所需的同步。
- 无限档继续跳过应用定时器，有限档沿用原有精度限速。这次变更针对音域回响的独立 D3D12 宿主；极光之环的 CanvasAnimatedControl 仍由 Win2D/合成器调度，不能据此宣称它已经解除显示调度限制。

### 覆盖层

通用设置新增默认关闭的“调试覆盖层”，在渲染区域左上显示。开关经 AppSettings、SaveSetting、DataJsonService 和源生成 JSON 持久化，双语资源齐全。覆盖层不接收鼠标命中，使用主题资源及 DIP 布局，避免随 GPU 渲染比例缩放。

- D3D12 显示渲染 FPS、DXGI 接受的提交 FPS、采样区间内丢弃提交数、CPU Render 包围耗时、CPU Present 调用耗时、CPU fence 等待耗时、实际输入/输出像素、活动 AA/DLSS 请求 preset、HDR 与 tearing/兼容状态。
- “GPU 等待”是 CPU 等待 GPU fence 的墙钟时间，不是 GPU timestamp。CPU 帧耗时不含应用限速等待。提交成功也不代表每帧都被显示器扫描输出，界面明确区分这一点。
- Win2D 显示 Draw 回调频率与 CPU Draw 耗时，并标明由 Win2D/合成器调度。
- 渲染线程仅向共享统计对象写入值，UI 每 500 ms 采样并生成文本。关闭、效果切换、暂停、宿主隐藏和退出时停止统计与 UI 定时器；不采样真实音频或创建额外 GPU 读回。

### 已完成验证及边界

- NativeAOT 离屏探针 `--presentation` 通过：默认 tearing 路径和主动禁用 tearing 的兼容路径、显式队列深度、960×540→801×451→960×540 resize、回读、D3D12 调试检查及释放。
- 开启 CPU 计时的渲染循环预热 32 帧后，96 帧测得同线程托管分配 0 B；统计 Record 连续 1000 次测得 0 B。仅指测试覆盖的热路径，不包含 UI 每 500 ms 更新文本的分配、冷启动或厂商原生内存。
- 统计测试区分 100 次渲染 / 70 次提交 / 30 次丢弃，验证关闭不记录、重新启用清除旧样本、默认关闭与 true 的 AOT JSON 回写。
- 原有 `--reconstruction` 通过所有 AA/SR 模式、输入比例、DLSS preset、历史重置、奇数 resize、运动矢量、空间斜边及第二效果回归。主工程无包 NativeAOT 发布通过，保留既有 nullable、XAML 属性路径、Assembly.Location 和 SharpGen 裁剪警告。
- 两份资源键及格式占位符静态匹配，XAML 编译与差异检查通过。未启动主应用、操作设置或采集实际显示帧率；离屏成功不证明实屏上限已经解除。覆盖层显示、DPI/主题/壁纸模式与真实无限档呈现交由用户验收。

构建与复测入口（工作目录 `doc/verification`）：

```powershell
dotnet publish HdrProbe/HdrProbe.csproj -c Release -r win-x64 -p:PublishAot=true -p:PublishTrimmed=true -p:SkipReconstructionNativeBuild=true -o HdrProbe/bin/presentation-aot
./HdrProbe/bin/presentation-aot/HdrProbe.exe --presentation
./HdrProbe/bin/presentation-aot/HdrProbe.exe --reconstruction
```

本地日志：`HdrProbe/bin/presentation-aot/presentation.log`、`reconstruction.log`、`app-publish.log`。参考工程未修改。
