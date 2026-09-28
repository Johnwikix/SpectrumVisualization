# Changelog

功能变更记录，最新在前；格式约定见根目录 AGENTS.md。

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
