# Changelog

功能变更记录，最新在前；格式约定见根目录 AGENTS.md。

## 2026-09-29 修复 DLSS L/M 原生比例流水线闪烁

- `Rendering/TemporalReconstruction.cs`、`GpuGraphics.Pipeline.cs`：DLSS 路径先提交下一帧场景，再等待前次后处理完成后调用 SDK；修复重叠录制/执行造成的图像异常，保留场景与 SR 的 GPU 重叠，不更改比例或预设。
- `Rendering/GpuGraphics.cs`：明确 SDK 失败时还需排空已经先行提交的场景，再重建资源。
- `doc/verification/HdrProbe/`：增加 4K、J/K/L/M、99/100%、奇偶帧的无中途回读对照和 DLSS 场景重叠/失败回退检查；修正 CPU 误用 ComputeSharp 向量运算的断言，固定对照场景随机种子。复现与验证见 `doc/verification/reconstruction.md`。

## 2026-09-29 音域回响渲染与 SR 流水线解耦

- `Rendering/GpuGraphics.Pipeline.cs`、`GpuGraphics.cs`：音域回响启用 SR 时使用独立场景队列与 SR/合成队列，两套帧资源轮转；通过 GPU fence 传递依赖，CPU 仅在复用忙碌帧槽时等待，最多两帧在途。
- `Effects/Sonic/SonicGpuEffect.cs`、`SonicTopographyEffect.cs`、`Rendering/IGpuVisualizerEffect.cs`：预分配每帧场景、深度、运动矢量、响应遮罩、高度场和上传缓冲，保留有序的模拟与时域历史；仅音域回响实现缓冲契约，未接入 FG。
- `Rendering/GpuRenderer.cs`：暂停确认前排空 GPU；即使暂停后立即恢复，也完成原暂停屏障。缩放、模式切换和 SDK 失败回退在释放旧资源前等待所有在途帧。
- `doc/verification/HdrProbe/`：新增阻塞 SR 队列、忙碌帧槽保护、连续历史对照及在途故障回退探针；NativeAOT 和离屏验证结果见 `doc/verification/reconstruction.md`，实屏延迟与画质由用户验收。

## 2026-09-29 刷新率预设精简

- `Model/FrameRateSettings.cs`：预设列表移除 288/320/480，新增 30，`Normalize` 上限同步收紧到 240；现有 60 默认值与 0（无限帧率）保持不变。

## 2026-09-29 壁纸在普通窗口最大化时自动暂停

- `Helper/WallpaperOcclusionDetector.cs`：同屏最大化窗口按显示器工作区判断遮挡，修复任务栏区域未覆盖导致壁纸持续渲染；保留全屏、透明/隐藏窗口和副屏排除规则，轮询类名改用栈缓冲。
- `doc/verification/WallpaperOcclusionProbe/`：补充无窗口几何回归及最大化/最小化/还原的实机验收场景，验证范围见 `doc/verification/wallpaper-occlusion.md`。

## 2026-09-29 启动后台预热与音域回响缩放防闪烁

- `App.xaml.cs`、`Rendering/GpuShaderWarmup.cs`、`GpuDeviceLease.cs`、`ShaderCompiler.cs`：启动后低优先级离屏预热音域回响及可用 AA/SR，覆盖 DLSS J/K/L/M；缓存 HLSL 字节码，复用设备直到渲染器接手，退出异步取消并等待收尾，无新增音频采集。
- `Rendering/GpuRenderer.cs`、`GpuGraphics.cs`：预热期间用空间路径保持渲染，结束后应用当前 SR 选择；窗口尺寸稳定 180 ms 后合并重建，移除 resize 的纯黑 Present，尺寸与 SR 配置一并更新。
- `Rendering/GpuPanel.cs`：拖动时缩放已有交换链画面，新尺寸首帧后在同一 UI 回调提交绑定与逆 DPI 变换；媒体卡片仍使用逻辑坐标。
- `Effects/EffectDescriptor.cs`、`doc/verification/HdrProbe/`：独立注册描述类型供真实工作线程离屏回归复用；验证预热并发、取消退出、缩放事件合并、GPU 回读和稳定帧分配。主工程及探针 NativeAOT 发布通过，Arc 140T 覆盖 XeSS/FSR，实机拖动/DPI/画质与 RTX 上 DLSS 由用户验收，详见 `doc/verification/reconstruction.md`。

## 2026-09-28 解锁 D3D12 呈现并添加调试覆盖层

- `Rendering/GpuGraphics.Presentation.cs`、`GpuGraphics.cs`：参考 ComputeSharpDemo，为交换链和 Present 配对启用支持的 `ALLOW_TEARING`，显式设置两帧呈现队列深度，resize 保留标志；渲染完成 fence 移至 Present 前，仍等待 GPU 完成后复用单份资源，兼容路径保留合成呈现。
- `Model/`、`Service/DataJsonService.cs`、`ViewModel/SettingViewModel.cs`、`View/SettingWindow.xaml`、`Strings/`：新增默认关闭的“调试覆盖层”开关，完整持久化及双语资源。
- `Rendering/RenderDebugStatistics.cs`、`ViewModel/RenderDebugViewModel.cs`、`Canvas/CanvasPanel.xaml*`、`Rendering/GpuPanel.cs`、`GpuRenderer.cs`：覆盖层分开显示渲染/提交 FPS、丢弃提交、CPU 帧/Present/GPU 等待耗时、输入输出尺寸、AA/preset、HDR 与呈现方式；Win2D 显示 Draw 统计。UI 每 500 ms 格式化，关闭、暂停和退出停止采样及定时器。
- `doc/verification/HdrProbe/`：离屏验证交换链标志、队列深度、兼容路径、resize、GPU 生命周期、统计与 AOT JSON；主工程 NativeAOT 发布通过，实屏帧率及覆盖层交互由用户验收，详见 `doc/verification/gpu-quality-aa.md`。

## 2026-09-28 移除画质预设并默认启用 FXAA

- `View/SettingWindow.xaml`、`ViewModel/SettingViewModel.cs`、`Model/SonicQualitySettings.cs`、`Strings/`：移除画质卡片、预设下拉框及无用的预设映射和双语资源，保留渲染比例、抗锯齿、网格及 DLSS 模型独立调节。
- `Model/SaveSetting.cs`、`Model/SonicQualitySettings.cs`：新配置和缺失字段默认 100% 渲染比例、FXAA；已有显式保存的比例和抗锯齿选择不覆盖。同步配置回归检查。

## 2026-09-28 修复 SR 低渲染比例被错误回退

- `Native/Reconstruction/Reconstruction.cpp`：修复上一轮把 SDK 推荐/动态分辨率范围当作固定输入硬限制的问题；DLSS 按实际输入尺寸创建，XeSS 直接提交指定尺寸，恢复 50% 以下及极低比例执行。
- `Rendering/GpuGraphics.cs`、`doc/verification/HdrProbe/`：抽出明确输入尺寸的资源创建边界，新增 49%/40%/34%/33%/1%、精确三分之一与 16×9 输入的真实 SR 回读回归，断言不能以 FXAA 回退代替成功；详细证据见 `doc/verification/reconstruction.md`。

## 2026-09-28 统一渲染比例、DLSS preset 与刷新率档位

- `View/SettingWindow.xaml`、`ViewModel/SettingViewModel.cs`、`Strings/`：移除独立重建质量，所有 AA/SR 共用 1%–100% 整数滑块；新增 DLSS J/K/L/M 选择，默认 K；刷新率改为 60/72/80/120/144/160/240/288/320/480 Hz 和无限帧率下拉框。
- `Model/`、`Service/DataJsonService.cs`：新配置统一持久化渲染比例和 DLSS preset；保留旧 JSON 字段，将旧 SR 质量一次性迁移为近似比例，保留旧的非标准刷新率。
- `Native/Reconstruction/Reconstruction.cpp`、`Rendering/TemporalReconstruction.cs`、`GpuGraphics.cs`：SDK 接受宿主指定的输入尺寸，查询 XeSS/DLSS 支持范围并遵循 DLSS 动态分辨率创建契约；不支持的尺寸暂退 FXAA，比例恢复后重试。
- `Rendering/GpuRenderer.cs`、`GpuFramePacer.cs`、`Canvas/CanvasPanel.xaml.cs`：移除 120 Hz 上限，无限档跳过应用定时限速；滑块停止变化 150 ms 后重建 GPU 资源。保留单份资源复用所需的 GPU 同步。
- `doc/verification/HdrProbe/`：扩展 NativeAOT 配置迁移、帧定时、比例/模型切换、GPU 回读与分阶段耗时验证；详细结果见 `doc/verification/reconstruction.md`，实屏帧率、功耗和画质由用户验收。

## 2026-09-28 抗锯齿下拉框名称简化

- `View/SettingWindow.xaml`：音域回响抗锯齿下拉框中 `XeSS-SR / XeSS AA` 简化为 `XeSS`、`DLSS / DLAA` 简化为 `DLSS`，不再区分超分与抗锯齿模式。

## 2026-09-28 SMAA 与 XeSS / FSR / DLSS 时域重建

- `Rendering/SmaaPass.cs`、`External/SMAA/`：接入上游 SMAA 1x 三通道与查找表，保留 FXAA 和关闭选项；SDR/HDR 共用亮度映射与最终编码出口。
- `Rendering/TemporalReconstruction.cs`、`Native/Reconstruction/`、`External/Upscalers/`：新增 NativeAOT 兼容的 XeSS-SR、FSR 3.1、DLSS/DLAA 适配，固定 SDK 提交与校验和；不含插帧，初始化/执行失败回退 FXAA。
- `Effects/Sonic/`、`Rendering/Gpu*`：提供深度、相机和地形运动矢量、响应掩码及历史重置；粒子在时域重建后以输出分辨率叠加。
- `Model/`、`Service/DataJsonService.cs`、`ViewModel/SettingViewModel.cs`、`View/SettingWindow.xaml`、`Strings/`：新增原生 AA、质量、均衡、性能重建模式和硬件可用状态；同步持久化、双语资源及第三方署名，旧配置与画质预设默认值不变。
- `Audio/SpectrumAnalyzer.cs`、`AGENTS.md`、`doc/verification/HdrProbe/`：增加显式无音频采集的探针入口，固定仅离屏验证约定；离屏 D3D12 调试层、运动矢量/斜边/输出回读与 NativeAOT 探针通过。本机无 RTX，DLSS 仅完成编译和不可用回退验证，发布者仍需完成 SDK 发布要求；详见 `doc/verification/reconstruction.md`。

## 2026-09-28 GPU 宿主抽取、音域回响画质与抗锯齿

- `Rendering/Gpu*`、`Rendering/IGpuVisualizerEffect.cs`、`Effects/IVisualizerEffect.cs`、`Canvas/CanvasPanel.xaml.cs`：提取独立 GPU 宿主与效果协议；注册信息决定宿主、创建工厂及媒体覆盖层，保留 Win2D 的极光之环和两个持久化效果 ID。
- `Effects/Sonic/SonicGpuEffect.cs`、`SonicTerrainSource.cs`：地形改用实例化柱体光栅化与深度缓冲，直接写入 FP16 场景，移除生产路径的全屏 DDA、FP32 地形中间图及复制；粒子资源归音域回响所有。
- `Effects/Sonic/Shaders/HeightFieldShader.cs`、`SonicTopographyEffect.cs`：音频提升曲线、涟漪时间衰减移至逐帧计算；闲置波为零时跳过对应噪声计算。
- `Model/`、`Service/DataJsonService.cs`、`ViewModel/SettingViewModel.cs`、`View/SettingWindow.xaml`、`Strings/`：新增性能、均衡、高和自定义画质，独立渲染比例及关闭/FXAA 设置；预设以一个设置快照生效并持久化。旧配置保持 100% 渲染比例、原网格与关闭抗锯齿。
- `Rendering/ColorOutputPipelines.cs`、`External/FXAA/`：共享 SDR/HDR10 输出；引入带原始许可的 FXAA 3.11，在亮度映射后处理边缘，PQ/SDR 编码及抖动仍在最终输出阶段；XAML 媒体层不降采样。
- `Rendering/GpuFramePacer.cs`、`GpuRenderer.cs`：高精度可唤醒帧定时，避免 120 Hz 等待逐帧向整毫秒取整；未成功呈现的帧不计入诊断帧数。
- `doc/verification/HdrProbe/`：扩展配置、第二 GPU 效果、输出回读及性能探针。NativeAOT 发布与首轮探针已通过；按用户要求停止后续验证，实屏 1440p / 120 FPS、完整 UI 联动和 HDR 观感尚待用户验收，详见 `doc/verification/gpu-quality-aa.md`。

## 2026-09-27 音域回响 HDR 与双渲染宿主

- `Rendering/`、`Effects/Sonic/`：音域回响迁移到 ComputeSharp / D3D12，浮点场景统一合成粒子后输出 SDR 或 HDR10；检测窗口所在显示器并在 HDR 不可用时回退 SDR。
- `Canvas/CanvasPanel.xaml.cs`、`Effects/AuroraRingEffect.cs`：极光之环保留 Win2D / SDR；切换等待在途帧完成，暂停非活动宿主及媒体刷新，退出异步释放 GPU 后再释放音频服务。
- `Model/`、`Service/DataJsonService.cs`、`ViewModel/SettingViewModel.cs`、`View/SettingWindow.xaml`：新增 HDR 开关、白点亮度与最大亮度滑块及实际输出状态；默认关闭、200 / 1000 nit，旧配置自动使用默认值，效果 ID 不变。
- `Control/SonicMediaCard.xaml`、`ViewModel/SonicMediaViewModel.cs`：封面、标题和进度移至 XAML SDR 图层，解码串行处理并丢弃过期结果。
- `Strings/`：中文显示名称统一为“极光之环”“音域回响”，英文资源及 HDR 无障碍名称同步维护。
- `Manager/SettingManager.cs`：HDR 参数停止调整 500 ms 后保存；保存串行化并使用临时文件原子替换，退出保留同步落盘。
- `WinExSpectrumTest.csproj`：移除 ComputeSharp.D2D1，保留极光使用的 Win2D；无包发布同时复制资源索引及对应 XBF，排除探针构建产物的递归打包。
- `doc/verification/HdrProbe/`：GPU 回读、生命周期与 NativeAOT UI 回归；详细验证证据和设备范围见 `doc/verification/hdr.md`。
