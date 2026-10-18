# Changelog

功能变更记录，最新在前；格式约定见根目录 AGENTS.md。

## 2026-10-18 切歌场景交接与左右布局

- `FoliaPlaybackService`、`FoliaSong`、`FoliaGpuEffect`：区分歌词查询中与查询结束；新曲文字和纹理准备完成前保留上一完整场景及其歌词位置，完成后整组替换，避免背景空档和歌曲信息闪现；无歌词、失败和空会话仍可正常结束等待。
- `FoliaMonet`：宽窗口按中线等分封面信息与歌词区域，封面依左列宽度和剩余高度缩放，不再固定为 380 DIP。
- `FoliaPendolo`：歌词环、渐变及机械部件统一平移，当前句从窗口中线开始，可用宽度同步限制在右半区；坐标保持 DIP。
- `FoliaGlyphShadow`：阴影卷积改为按像素 SIMD 并行，边界与不支持硬件加速的环境保留标量路径；逐字节一致性验证后沿用已有字形缓存。

## 2026-10-18 切歌等待、封面更新与印象歌词

- `FoliaPlaybackService`、`FoliaScenePreparation`：封面与歌词独立解析，同曲后到的封面不再被去重丢弃，切换效果复用已有封面；封面更新不取消相同文本的字形准备。
- `FoliaTextAtlas`、`FoliaGpuEffect`：字形生成使用最多四个后台工作单元，仅倾斜模式准备斜体；完整歌词准备期间保留已就绪的歌曲信息，避免长时间只有背景。
- `FoliaMonet`：印象模式改为宽窗封面/信息与歌词分栏、紧凑窗顶部封面；删除 MONET 标牌和装饰竖线，按字素拆分超长歌词并显示翻译。参考播放器 FlowWave 保留速度的临界阻尼滚动，真逐字歌词使用轻字浮、长音节缩放与辉光，逐行歌词整句高亮。
- `SettingViewModel.Folia`、`FontChoice`、`SettingWindow`、双语资源：系统字体改为带字体预览的下拉列表；缓存目录增加文件夹选择按钮；补齐全部歌词与动态背景选项的中文显示，持久化标识不变。
- `LyricsDiskCache`：未打包运行时默认目录回退到用户本地应用数据，预览使用自身目录，避免路径读取异常打断整页设置绑定。

## 2026-10-18 OriginalSound 只读歌词与独立网络缓存

- `MediaInfoService`、`Services/Lyrics`：根据 SMTC 来源 ID 识别 OriginalSound，以标题、艺术家和专辑匹配歌词，遵循播放器的文件/数据库优先级和本地格式顺序，兼容新旧歌词表、翻译开关与歌曲偏移；重复候选不猜测。
- `ReadOnlyLyricsDatabase`：使用禁止可写打开、删除和 WAL 共享内存访问的 SQLite VFS；不调用播放器服务、不迁移或写回数据库和配置，锁定/不兼容时回退，WAL 数据库不接入。
- `FoliaPlaybackService`、`FoliaSong`：支持 KRC/QRC/LRC/TTML，按 QQ 逐字、网易云逐行、QQ 逐行顺序回退；请求合并、取消、无结果短期去重、服务熔断与手动重试保持独立。
- 设置模型、`DataJsonService`、`SettingViewModel.Lyrics`、`SettingWindow`、双语资源：关于页可开关网络歌词缓存及配置目录，以标题/艺术家/专辑标识独立 JSON 缓存，只保存成功的网络结果，默认开启且不修改播放器目录。

## 2026-10-17 切歌峰值与资源释放

- `FoliaPixelBuffer`、`FoliaTextAtlas`、`FoliaScenePreparation`：大图集像素改为显式租约持有的原生缓冲，构造失败、取消、替换和退出均释放；大块像素回收移出渲染提交帧。
- `FoliaSceneUpload`、`FoliaGpuEffect`：使用最多 4 MiB 的复用上传缓冲，按帧限时复制 CPU 数据并分块录制 GPU 上传，候选纹理完成后才替换活动场景；时域历史在实际提交帧重置，取消上传等待既有帧屏障。
- `FoliaSourcePlanner`：最多 16 条 / 8 MiB 的不可变布局结果缓存，重复切模式、切效果时避免再次产生解释器临时对象；不复用可变解释器。
- `External/Folia/native-layout.js`：Cadenza 碰撞搜索按需计算候选，去除每次尝试的数组和结果对象；12 组布局输出与原算法逐字一致。
- `FoliaGpuEffect`、`FoliaPlaybackService`、`MediaInfoService`、`App`：释放后清除歌曲和布局引用，服务停止清除歌曲 / SMTC 快照并阻止迟到回调回写；退出时清理 CPU 缓存，保存设置失败也继续停止渲染。

## 2026-10-17 SMTC 切歌后台准备

- `FoliaScenePreparation`、`FoliaTextAtlas`、`FoliaSourcePlanner`、各模式规划器：字形图集、歌词布局和封面取色移到可取消的后台任务，合并连续的歌曲信息更新；仅封面变化时复用图集和布局。
- `FoliaGpuEffect.Preparation`、`FoliaGpuEffect`：准备期间继续绘制背景，完成后在渲染线程安全提交并重置时域历史；快速切歌丢弃过时结果，退出等待后台准备结束。

## 2026-10-17 着色器管理、时域稳定性与 CPU 帧开销

- `Build/ShaderPrecompiler`、`ShaderAssets.targets`、`ShaderCompiler`：构建 / 发布预编译全部内置 HLSL，程序目录的校验缓存按完整源码、入口、目标及编译选项失效；损坏或缺失时回退编译，目录不可写时继续使用内存结果。
- `GpuPipelineCache`、`GpuDeviceLease`：共享设备上的管线复用，并缓存驱动管线；显卡标识、驱动、系统、着色器及管线版本参与键值，不兼容的驱动缓存自动重建，在最后一个设备租约释放时清理内存管线。
- `SettingViewModel.Shaders`、`SettingWindow`、双语资源：通用设置增加清理并重建命令、进度与失败状态；后台编译，渲染工作线程在安全边界重建资源，退出等待后台维护结束。
- `FoliaGlyphCache`、`FoliaTextAtlas`、`Native/FoliaText`：按实际字体轮廓缓存 MTSDF / 高斯字形瓦片，减少切歌重复生成；CPU 内存缓存有容量限制，磁盘产物位于程序目录。
- `FoliaSourcePlanner`：预解析并复用同一份内置布局脚本，每次规划使用独立解释器，避免切换模式重复解析源码和跨歌曲全局状态污染。
- `Stage.hlsl`、`FoliaShaderLibrary`、`Sora.hlsl`：修正 jitter 方向，在几何 / 背景栅格化时采样；响应掩码区分内容变化和静态边缘的覆盖变化，避免全画面持续拒绝历史；限制字形与辉光采样到本字的纹理范围，消除邻字串扰竖条。
- `FoliaGpuEffect`、`FoliaLumiereGeometry.Decor`：实例布局在预分配的 CPU 缓冲完成后连续上传，避免反复读取写合并上传堆；DPI 缩放移入 GPU，跳过没有飞行的字形轨迹采样。
- `GpuRenderer`、双语资源：覆盖层 CPU 工作时间扣除显式 GPU 等待及 Present，三项分开显示，关闭覆盖层时不启用额外计时。

## 2026-10-17 Folia 字形清晰度与视觉对齐

- `FoliaTextAtlas`、`FoliaDistanceField`、`Native/FoliaText`、`Stage.hlsl`：由字体贝塞尔轮廓生成半精度 MTSDF，以实际屏幕导数恢复字形边缘，修正固定低分辨率覆盖贴图放大模糊及逐字填充裁切范围。
- `FoliaLumiere*`、`FoliaBloom`：使用原版逐歌曲镜头 / 光位种子、窗口槽位、连续运镜、字形飞行 / 径迹 / 追光 / 星点、回声、浮尘、星雨、开场点亮及十字爆闪；图形和文字分别接入原版多级柔光，运行时复用数值状态与 GPU 资源。
- `FoliaGlyphShadow`、`FoliaTextAtlas`：辉光使用冷路径高斯覆盖，避免距离场光晕抹糊文字；缓存斜体轮廓，上传后释放距离场 CPU 像素，布局缓存仅保留图集身份。
- `FoliaFume`、`FoliaTilt`：补齐 Fume 种子背景 / 频带响应 / 整行辉光和打印章时窗，修正 Tilt 斜体轮廓、行高、逐字错落与唱后余辉。
- `FoliaCadenza`：布局改由原版规划器生成，使用预分配的片段动画状态，修正文字焦点高度与活动词缩放。
- `FoliaMonetDrift`、`FoliaBackgroundGeometry`、`Stage.hlsl`：修正 Monet 固定背景画布的双重裁切、原版漂移和模糊半径，以及通用背景的运动缓动、十字形和实心圆。
- `FoliaGlowWords`、`FoliaPartita`：Classic / 云阶采用渲染线程独占的逐词状态动画；云阶使用原版语义分句规划，避免暂停时随 SMTC 刷新重新计时。
- `FoliaLyricLayout.Score`、`FoliaLyricLayout.Chat`、`FoliaTextAtlas`：修正 Monet 响应式海报 / 封面 / 歌词栏布局、Cappella 气泡边距与时间戳，标题和时间戳保留原版固定字重。
- `FoliaDiorama`、`Stage.hlsl`：镜台字形使用四角透视插值，移植原版相机阻尼 / 阅读回正，修正三维偏航方向、文字基线与邻行雾化。
- `FoliaSonnet`、`FoliaTempera`、`Print.hlsl`：补齐商籁红青色散的 Screen 混合与默认辉光、凝彩默认的透镜 / 颗粒 / 暗角 GPU 链路。
- `FoliaLatent`、`FoliaCoverSampling`、Paper / Sora 着色器：修正 Latent 封面六色取色与独立暂停时钟、Paper 默认坐标 / 点阵枚举 / 噪声通道，以及 Sora 的 GPU 星点哈希。
- `Rendering/FoliaTextAssets.targets`：正式构建和发布自动构建 / 携带原生字形辅助库，保留 msdfgen 固定版本来源与许可。

## 2026-10-17 Folia 原生交换链歌词与动态背景

- `Effects/Folia`、`EffectRegistry`：新增 `folia` GPU 效果，提供 13 种歌词模式、5 种内置背景和 Nomand 的 5 种图像处理；字形图集只在歌曲 / 字体变化时生成，逐帧由 D3D12 着色器绘制。
- `External/Folia`：迁入原版 Paper / Lumiere 着色器、绘光 100 套灯光配方与表情资源；原版商籁、凝彩、镜台、Pendolo、Fume、Tilt 的纯布局规划在冷路径编译成原生数组，保留来源及许可。
- `Services/FoliaPlaybackService.cs`、`External/Lyricify.Lyrics.Helper`：按 SMTC 歌名、歌手和时长匹配 QQ 歌词，优先 QRC、回退 LRC；处理逐字时间、译文、封面、迟到请求、重试与关闭取消。
- `Rendering`、`Canvas/CanvasPanel.xaml.cs`：新效果共享 HDR、抗锯齿、DLSS 预设和渲染比例；补齐 DPI 布局比例与歌曲 / 模式 / 字体切换后的时域历史重置。
- `AppSettings`、`SaveSetting`、`DataJsonService`、`SettingViewModel.Folia.cs`、`SettingWindow.xaml`、两份资源：新增独立 Folia 设置页，保持 MVVM 和源生成持久化。

## 2026-10-17 通用 GPU 画质、抗锯齿计时与壁纸快捷按钮

- `Model/GpuQualitySettings.cs`、`AppSettings.cs`、`Service/DataJsonService.cs`：将渲染比例、抗锯齿与 DLSS 预设独立为通用 GPU 配置；保留原 JSON 字段名与旧版比例迁移，默认值不变。
- `Effects/EffectDescriptor.cs`、`EffectRegistry`、`Canvas/CanvasPanel.xaml.cs`：GPU 宿主统一读取画质配置；新效果仅注册工厂及可选细节参数，时域算法仍须实现深度 / 运动矢量契约。
- `View/SettingWindow.xaml`、`ViewModel/SettingViewModel.cs`、两份 `Resources.resw`：画质选项移至通用页，分别补充说明与当前状态；音域回响音频响应强度上限由 250 提高到 400。
- `Rendering/GpuGraphics.Timing.cs`、`RenderDebugStatistics.cs`、`ViewModel/RenderDebugViewModel.cs`：覆盖层新增每 250 ms 采样的 GPU 抗锯齿 / 重建耗时，只读取已完成 fence，不增加同步等待；FXAA 明示包含输出编码，关闭覆盖层后停止采样。
- `MainWindow.xaml`、`Control/NotifyIconControl`、`SettingViewModel.cs`：最大化旁新增壁纸按钮，与托盘共用 ViewModel 命令及设置状态；附本地化工具提示和无障碍名称。
- `doc/verification/HdrProbe`：覆盖通用配置继承、旧配置迁移、计时切换 / 资源生命周期、双队列和失败回退、分配与离屏耗时；实屏 UI、壁纸交互及覆盖层合成开销由用户验收。

## 2026-09-29 主窗口默认尺寸放大

- `MainWindow.xaml.cs`：启动默认窗口尺寸由 512×512 改为 1024×1024（`AppWindow.Resize` 单位为物理像素，显示缩放非 100% 时视觉尺寸仍按比例缩小）。

## 2026-09-29 设置页新增重置所有设置

- `View/SettingWindow.xaml`、`SettingWindow.xaml.cs`：通用页底部新增"重置所有设置"卡片，点击弹出 ContentDialog 确认（默认焦点在取消）后执行重置。
- `ViewModel/SettingViewModel.cs`：新增 `ResetSettingsCommand`；壁纸模式开启时先经 `SetWallpaperMode(false)` 正规退出，再重置、按默认值补发效果切换与窗口主题/材质。
- `Service/DataJsonService.cs`：加载映射提取为 `ApplySettings` 供启动加载与重置复用；新增 `ResetSettingAsync` 以 `SaveSetting` 默认值写入运行时并落盘。
- `Strings/zh-CN`、`Strings/en-us`：同步新增卡片、按钮与对话框资源键。

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
