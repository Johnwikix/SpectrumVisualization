# Changelog

功能变更记录，最新在前；格式约定见根目录 AGENTS.md。

## 2026-10-24 修复标题栏关闭按钮绕过后台驻留

- `MainViewModel`、`MainWindow.xaml`、`MainWindow.xaml.cs`：自绘“×”按钮改为关闭窗口请求，与系统关闭共用 `RequestClose`；后台开关开启时隐藏小组件并暂停渲染，关闭时走完整退出，壁纸模式保持运行，托盘“退出”继续完整退出。
- 回归：隔离探针直接执行 XAML 绑定的真实命令、系统关闭处理与托盘命令，先复现标题栏误退，再验证修复；实机窗口与托盘交互仍由用户验收。

## 2026-10-23 单实例、后台驻留与壁纸故障日志

- `Program`、`SingleInstance`：XAML 初始化前建立互斥单实例与全局异常记录；重复启动通知已有实例，启动中的通知保留到窗口就绪，壁纸和锁定模式下打开设置。
- `AppLog`、应用异常处理路径：日志写入 MSIX LocalState/Logs，包含完整异常、HRESULT、调用位置及启动/退出过程；后台有界写入、重复异常汇总、每日/4 MiB 轮转并保留最多 30 个文件，致命异常同步刷盘。
- `DesktopWallpaperHost`、`MainWindow.Wallpaper`：记录 Explorer 宿主发现、窗口样式、DPI、挂接/恢复阶段及失败的 Win32 调用，便于定位客户机器的壁纸启动失败。
- 设置与托盘：新增“关闭后在后台运行”开关和“打开日志目录”按钮，中英文本地化同步；默认开启后台驻留（旧设置缺少字段时同样生效），托盘“退出”仍完整释放资源并结束进程。
- 验证：Debug x64 构建、关闭 Store 上传打包步骤后的 NativeAOT 发布、日志/跨进程互斥隔离探针通过；实机 MSIX、壁纸显示与设置交互由用户验收，尚未复现客户机器的壁纸故障。

## 2026-10-23 滑块数值常显与效果资源释放

- `SettingWindow.xaml`、`SliderValueTextConverter`：50 个 Folia 滑块左侧常显实时数值，使用编译函数绑定与提示共用的小数格式，保留原范围、步进和双向设置绑定。
- `CanvasPanel`：跨 Win2D／GPU 宿主切换时，在回调／帧屏障后停止并释放旧宿主、效果及媒体卡片；临时暂停仍保留当前资源。
- `FoliaPlaybackService`：明确离开歌词效果时丢弃上一首歌词和封面；透明封面仅由最终主题准备合成，移除服务重复保留的整幅像素数组。
- `FoliaScenePreparation`、`FoliaCoverPalette`、`FoliaCpuCacheLease`：最后一个准备工作者／封面取色任务结束后释放字形、布局及钟摆网格的 CPU 缓存；重叠的新效果仍可复用缓存，避免取消中的旧任务重新填充已清理的缓存。
- `GpuPanel`、`SonicMediaViewModel`、`AuroraRingEffect`：GPU 停止后可靠释放 COM 桥；媒体卡片取消原生解码并等待实际完成再释放流，丢弃已过期的极光封面，避免旧效果销毁后继续解码和保留图片。
- 验证：Debug／NativeAOT 发布通过；NativeAOT／Satori 离屏覆盖 36 次切歌、13 种歌词模式、4 次 GPU 效果切换及加载中取消，旧歌曲／图集可回收，渲染器销毁后本进程 GPU 用量归零；GPU 工作者暂停／恢复／异步停止与重建缩放回归通过，宿主／封面状态桩另有 29 项通过。实机界面、切换观感与进程内存曲线由用户验收，未把有界缓存保留表述为持续增长泄漏。

## 2026-10-23 切歌准备期间保持动态背景

- `FoliaGpuEffect.Transition`、`FoliaGpuEffect.Preparation`：背景快照延后到新场景上传完成后采集，准备与上传期间继续播放旧场景，避免静态快照长时间覆盖动态背景；连续切歌取消旧候选的等待状态，保留 0.45 秒过渡与时域重置屏障。
- `FoliaTemperaPrimitives`、`FoliaTemperaTriangulation`、`FoliaSourcePlanner`、原版脚本适配：凝彩的小多边形首轮 Earcut 和逐顶点变换改由冷路径 C# 回调计算，减少解释器临时数值与切片／展开数组；复杂多边形与孔洞仍走原版算法，图元顺序与布局输出保持一致。
- 离屏对照（NativeAOT／Satori、960×540、48 行合成歌词、4 次切歌）：凝彩歌词载入观察窗口中的托管分配约 313–406→190–215 MiB；这是合成场景的加载开销，不代表屏幕 FPS 或实际播放器切歌延迟。
- 验证：主工程 Debug／NativeAOT 发布、布局与 Earcut 对照、快速切歌及 FSR 离屏回归通过；设置交互与实机显示由用户验收。

## 2026-10-23 辞曲新境数值设置改用滑块

- `SettingWindow.xaml`：50 个 Folia 数值输入改为原生 Slider，保留范围和双向绑定；细调参数步进 0.01、强度 0.05、较大尺寸 0.1，色阶／字号百分比／字重分别为 1／5／100，键盘步进与拖动吸附一致。
- `SliderValueTextConverter`：滑块提示最多显示两位小数，避免 float 转 double 后暴露精度尾数；沿用设置卡片的本地化标题作为无障碍标签。

## 2026-10-23 在线歌词翻译按应用语言显示

- `App`、`FoliaPlaybackService`：复用应用界面语言判断，非中文用户隐藏在线匹配及其缓存的中文译文，保留歌词正文与时间轴，缓存仍保存完整内容。
- 播放器文件（含内嵌翻译）和数据库歌词视为用户内容，不受应用语言限制，继续遵循播放器翻译开关；播放器无可用歌词时的在线／缓存回退仍应用语言过滤。

## 2026-10-23 自动音节强调、系统明暗与切歌分配优化

- `EnhancedLrcParser`、`FoliaSong`：支持 `<mm:ss.x/xx/xxx>` 增强 LRC 的绝对音节时间、重复行时间、offset 和缺失尾边界；用户提供的 `01_17_[I].lrc` 从 23 行均无音节同步修复为 23 行全部同步，正文不再残留时间标签。
- `FoliaSyllableEmphasis`、`FoliaEmphasisColor`、主题解析与原生字形布局：按真实音节时长、局部／整曲中位数及音节数估计选择延长音，采用 OKLCH、色域收缩与显示对比度校正自动生成强调色；同词不同位置独立判断，普通行级 LRC 不虚构音节时间，不使用 AI 情绪或语义关键词分析。
- 设置模型、`SettingViewModel.FoliaTheme`、设置页及两份资源：移除手动关键词规则和自定义取色器，旧自定义来源迁移为封面配色；保留旧 JSON 字段名以兼容配置。明暗版本新增“跟随系统”，默认仍为深色，持久化保留 system 偏好。
- `FoliaSystemThemeService`、`AppSettings.FoliaTheme`、应用生命周期：仅在跟随系统时订阅系统颜色通知，向渲染端发布已解析的不可变明暗快照；退出、显式明暗选择及初始化失败时解绑。
- `FoliaGlyphStore`、`FoliaTextAtlas`：图集完全命中时跳过 Win2D 设备和工作字体初始化；`FoliaSourcePlanner.Pendolo`、`FoliaDioramaNumericArrays` 去除解释器 typed-array 中间复制，`FoliaFumeFontSearch` 保留原版 8 轮搜索与最终布局、避免构造试字号布局图。
- `FoliaCompiledResult`、布局缓存：大型不可变 JSON 压缩保留，缓存预算从 16 MiB 降至 4 MiB；降低模式切换后的保留与采样堆峰值，部分模式冷路径压缩分配增加约 0.5–1.2 MiB，未声称所有模式分配下降。
- 性能检查（Release JIT、960×540、合成歌词／封面、每模式两次切歌）：烟岚约 62.26→33.36 MiB、钟摆约 56.46→28.67 MiB、镜台约 26.43→18.50 MiB／次；凝彩采样托管堆峰值约 83.06→74.42 MiB，本轮本地 GPU 最大采样约 259.83→251.69 MiB。同进程交替纯布局基准（预热后各 7 次）烟岚编译中位数 107.44→56.60 ms，凝彩压缩增加约 2.82 ms；场景墙钟准备时间波动明显。凝彩、商籁的脚本图形／镜头计算仍是主要冷路径分配来源，不将离屏耗时当屏幕 FPS 或播放器端到端切歌延迟。
- 验证：Debug 构建与主工程／探针 Release NativeAOT 发布、增强 LRC 边界、源生成设置往返、系统取值／解绑、原版数值及 Float32 对照、210 组烟岚字号边界通过；NativeAOT 的 13 种歌词各 120 稳定帧渲染线程分配为 0，5 背景过渡／FSR、取消／失败恢复／DPI 离屏回归通过。36 次切歌后的旧歌曲／图集可回收，渲染器销毁后本进程 GPU 占用归零；既有依赖裁剪／AOT 警告仍在，实际显示、HDR 和交互由用户验收。

## 2026-10-23 原版主题、歌词专色与平滑切歌

- `FoliaThemeSettings`、设置三层映射、`SettingViewModel.FoliaTheme`、设置页及中英文资源：新增原版预设、封面和谐明暗配色、自定义明暗四色、重新生成及手动关键词专色；保持原生 GPU 宿主，不加入 AI 情绪、静止或网页背景。
- `Build/FoliaThemes`、`themes.js`、`FoliaThemePlanner`、`FoliaThemeResolver`：复用上游非 AI 生成与关键词范围算法，在后台生成确定的明暗主题；同曲迟到歌词复用配色，明暗切换复用已生成的主题对，透明封面按当前底色合成。
- 原生歌词绘制、`FoliaTextAtlas`、布局适配：关键词按原版字词时序着色，商籁保留正文并为关键词光晕单独着色，镜台在后台校正关键词可读性；凝彩与钟摆的预计算图形同步主题。
- `FoliaMonet`：保留现有信息区与弹簧滚动，高亮行、译文和模糊使用 0.3 秒 Sine-Out 过渡；仅当高／宽大于 1.1 时采用竖屏布局。
- `FoliaGpuEffect.Transition`、着色器及管线版本：切歌背景使用 0.45 秒平滑混合，连续切歌从当前混合结果接续；只缓存无歌词的背景，FP16 快照最多 518400 像素，尺寸变化后释放。
- `FoliaSourcePlanner.Pendolo`、`FoliaPendolo`、`FoliaGlowWords`、场景准备与 GPU 宿主：钟摆网格直接传递浮点数据并复用相同几何；按歌词容量分配离场缓冲，释放旧封面引用和闲置的绘光泛光／潜流目标／钟摆上传资源。
- 兼容性：旧配置默认使用封面暗色主题，现由原版和谐配色生成器决定颜色；已有歌词模式、背景 ID 与字体设置保持兼容。
- 性能对照（本次功能接入后的优化前后，Release、960×540、合成数据）：钟摆单次切歌准备约 263–275 ms → 118–139 ms，分配约 111 MB → 56 MB；同曲主题更新约 29.7 ms → 0.043 ms。切歌仍有冷路径分配，这些耗时不是屏幕呈现 FPS。
- 验证：主工程 Debug 与 Release NativeAOT 发布、源生成配置兼容检查、上游主题／镜台对照、网格逐浮点一致性、13 种歌词和 5 种背景的 NativeAOT 离屏回归通过；13 种歌词开启关键词后各 120 个稳定帧测得渲染线程托管分配为 0。36 次切歌后旧歌曲／图集可回收，渲染器销毁后本进程 GPU 占用归零；切歌过渡、FSR 重置及 D3D12 资源状态检查通过。现有依赖的裁剪／AOT 警告仍在；实际显示、HDR 与交互由用户验收。

## 2026-10-23 封面取色与辞曲新境主题同步

- `AuroraCoverPalette`、`AuroraRingEffect`：修正均色重复除以 255 导致主色接近黑色，显式清空色相直方图；灰阶、透明及无封面使用确定的回退，不再沿用上一封面的配色。
- `FoliaCoverPalette`、`FoliaPlaybackService`、`FoliaSong`：封面与原版代表色、可读暗色主题一起在后台准备并原子发布；保留透明度用于取色，上传前按舞台底色合成，潜流复用已提取的颜色；同曲封面移除、无效解码及迟到请求遵循曲目版本守卫。
- `FoliaScenePreparation`、`FoliaSourcePlanner`、`native-layout.js`、凝彩／时计／镜台渲染：主题色进入布局缓存与绘制，修正固定配色、反色墨纸色及雾色不同步，封面更新继续复用字形图集。现有配置字段与模式列表不变，有封面时歌词配色现在跟随封面。
- 验证：主工程 Debug 构建及 .NET 10 Release NativeAOT 发布通过；NativeAOT 离屏回归覆盖 13 种歌词／5 种背景、16 组当前上游取色对照、透明 PNG 解码、封面移除和迟到请求。实际显示、HDR 观感及交互由用户验收。

## 2026-10-22 MainWindow 标题栏命令化（MVVM）

- `ViewModel/MainViewModel.cs`（新）：标题栏命令（最小化 / 最大化 / 关闭 / 全屏 / 锁定）与图标状态（`MaximizeGlyph` / `FullScreenGlyph`）；全屏命令的 `CanExecute` 挂壁纸模式守卫（监听 `AppSettings.WallpaperEnabled`，UI 线程刷新），命令按项目既有约定经 `App.MainWindow` 回跳，行为与原 Click 守卫一致。
- `MainWindow.xaml`：五个标题栏按钮由 Click 事件改为命令绑定，最大化 / 全屏图标改为 OneWay 属性绑定（移除 `MaximizeIcon` / `FullScreenIcon` 的命令式赋值）；`LockBtn` / `FullScreenBtn` 名称保留，AutomationId 不变，`wallpaper-regression.ps1` 不受影响。
- `MainWindow.xaml.cs`：五个 Click 处理器收敛为窗口公共入口（`MinimizeWindow` / `ToggleMaximize` / `ToggleLock` / `ToggleFullScreen`），`UpdateMaximizeGlyph` 与 `SetFullScreen` 改为向 MainViewModel 推送状态；拖动区、锁定光标轮询、壁纸宿主等窗口实现不动。

## 2026-10-22 托盘"切换效果"改为 MVVM 效果子菜单

- `Control/NotifyIconControl.xaml`：托盘"切换效果"由单击循环改为子菜单，直接列出三个效果（极光之环 / 音域回响单选，辞曲新境再嵌一级子菜单选择 13 种歌词模式）；子菜单通过 `MenuFlyoutExtensions.ItemsSource` 绑定 ViewModel 生成，保留原有 `E71D` 图标（`MenuFlyoutSubItem.Icon`，Windows App SDK 1.5+ 可用）。
- `Extensions/MenuFlyoutExtensions.cs`（新）、`Model/MenuModel.cs`（新）：移植 music_player 的 MenuFlyout 绑定扩展；挂在 `MenuFlyout` 上按 Opening 重建，挂在 `MenuFlyoutSubItem` 上按 Loaded（菜单每次展开）重建，重建前执行 `PrepareCommand` 刷新模型，只重建宿主自身 Items，不影响同菜单静态条目；`MenuModel` 增加 GroupName / IsChecked（RadioMenuFlyoutItem）与 AutomationId。
- `ViewModel/SettingViewModel.TrayMenu.cs`（新）：`TrayEffectMenu` 模型树（效果名走 `EffectRegistry.GetCatalog`，歌词模式名走 `FoliaModeName_*` 资源键），`PrepareTrayEffectMenu` 展开前刷新选中态；`SelectEffect` / `SelectFoliaLyricMode` 命令复用 `VisualEffect`、`FoliaLyricMode` 属性写 `AppSettings` 并持久化，选择歌词模式同时进入辞曲新境。
- `Control/NotifyIconControl.xaml.cs`：移除效果菜单相关的命令与勾选同步代码，恢复仅处理壁纸 / 解锁状态。
- `Strings/zh-CN`、`Strings/en-us`：新增 13 个无属性后缀的 `FoliaModeName_*` 资源键（供代码取词，`GetString` 不能用属性后缀键），两份资源已静态核对匹配；效果名复用 `EffectRegistry` 既有键。
- `doc/verification/wallpaper-tray.ps1`：`Cycle` 动作适配新结构——展开"切换效果"子菜单后点击 `SwitchEffect_sonic-topography` 并按 Esc 收起（单选项不再自动关闭菜单）；脚本改动未经实机验证，需按验证范围由用户实跑确认。

## 2026-10-21 HDR 白点上限与设置控件宽度统一

- `View/SettingWindow.xaml`、`Model/AppSettings.cs`：HDR 白点亮度上限由 500 提高到 1000 nit（滑杆与运行时钳制同步调整，存量配置 ≤500 不受影响）；白点与峰值的联动规则不变。
- `View/SettingWindow.xaml`：全部设置页的 ComboBox / TextBox / NumberBox 宽度统一为 220（原 160/180/230/280/130 混用），仅影响布局。
- `doc/verification/HdrProbe/Program.cs`：补 1000 nit 钳制断言；`HdrProbe.csproj` 补入 `Services/Lyrics/LyricsContent.cs`（`SettingsJsonContext` 引用的类型此前缺失，探针独立构建已修复）；`README.md`：白点范围同步为 80–1000 nit。

## 2026-10-21 设置页子标题分区

- `View/SettingWindow.xaml`：辞曲新境页参照关于页样式拆分为"舞台 / 歌词模式专属设置 / 动态背景专属设置 / 歌词字体 / 歌词来源"五个 TextBlock 子标题分区；烟岚、凝彩、绘光、商籁、镜台、时计及实时动感归入歌词模式分区，潜流 / 图像背景归入动态背景分区，绑定与持久化不变。
- `View/SettingWindow.xaml`：通用页新增"效果 / 画质与渲染 / 音频 / 外观 / 高级"子标题（调试覆盖层与重置归入高级分组），极光之环页新增"旋转与封面 / 频谱 / 文字"，音域回响页新增"主题与地形"。
- `Strings/zh-CN`、`Strings/en-us`：同步新增 14 个子标题资源键，两份资源已静态核对匹配。

## 2026-10-20 倾斜与商籁首次进入提速

- `FoliaTilt`、`FoliaTextAtlas`、`FoliaScenePreparation`：先按正常字体度量规划倾斜片段，仅准备实际绘制的斜体字形；窗口、缩放、字体及歌词变化时重新检查并补齐，沿用既有字形租约和 GPU 批次预算。
- `FoliaSonnetCameraSamples`、`FoliaSourcePlanner`、原版布局适配：商籁镜头数值采样改为直接 C# 回调，保留每镜头 241 个样本、五点焦点平滑、原插值及呼吸轨迹。
- 验证：NativeAOT 离屏同曲 960×540 的首次进入延迟，倾斜约 433→156 ms、商籁约 530→125 ms；126 组镜头向量输出逐字一致，斜体度量、尺寸／缩放切换及取消释放回归通过，实机响应由用户验收。

## 2026-10-20 辞曲新境切换与切歌延迟

- `FoliaScenePreparation`、`FoliaSceneUpload`：取消场景固定 40 ms 等待，资源已就绪的步骤直接完成；封面、模式纹理与字形并行上传，仍在既有帧屏障后交接和释放。
- `FoliaCadenzaSearch`、`FoliaSourcePlanner`、原版布局适配：心境碰撞搜索改为直接 C# 数值回调；商籁仅计算选中镜头并去除中间 JSON 往返，布局结果缓存上限由 8 MiB 调整为 16 MiB，避免时计网格挤出其他模式。
- `FoliaPlaybackService`、`MediaInfoService`、`FoliaGpuEffect.Preparation`：刷新媒体时钟后立即查询歌词，取消固定 120 ms 等待，先发布歌词再写缓存；新歌标题和封面可在歌词查询结束前显示，重复候选不重启上传。

## 2026-10-20 辞曲新境效果行为对齐

- `FoliaLineTiming`、`FoliaLineHandoff`、歌词布局：区分当前演唱行与场景锚点，补齐短句提示、整行进出场、下一句预告和时计间奏／尾奏锚点。
- `FoliaSong`、`FoliaGlowWords`：按原版文本重复规则标记副歌，复用语义／标点排版，补齐逐字辉光、副歌扩散圈及环抱交错起伏。
- `FoliaChatMotion`、`FoliaCadenza`：补齐清唱消息弹簧位移与心境音量驱动漂移。
- `FoliaDiorama`：补齐当前字与唱后残影、辉光、连续曲面传播波及粒子尺寸／亮度响应；音频沿用现有频谱。
- `FoliaSonnetTextDecor`、原版几何生成边界：补齐文字固定几何和四种开放框装饰；`FoliaTempera` 补齐段落三类转场及独立后处理参数。
- `FoliaBackgroundGeometry`、`FoliaFume`：通用背景暂停保持漂移与旋转位置、按频段缩放；烟岚几何背景默认关闭，与原版一致。
- 设置模型、映射、ViewModel、双语资源、设置页：新增背景滤镜、镜台、凝彩及绘光参数，全部收入默认折叠分组；仅通知实际变化的绑定属性。新增字段缺省采用 Folia 默认值，保留既有 JSON 字段名。
- 兼容性与验证：烟岚几何背景缺省关闭，同样作用于未包含新字段的存量配置；Release NativeAOT 发布、12 模式合成数据离屏回读、40 组原版时序向量及源生成序列化往返通过。实机交互、HDR 与运动观感由用户验收。

## 2026-10-20 镜台与时计设置分组

- `SettingWindow.xaml`、双语资源：镜台与时计的专属强度子项分别收入默认折叠的设置分组，组内标题省略重复模式名。
- `AGENTS.md`：明确后续效果／模式专属子项统一使用默认折叠的 `SettingsExpander`。

## 2026-10-20 辞曲新境设置分组与背景命名

- `SettingWindow.xaml`：商籁的七个开关和推镜裂光强度收进默认折叠的“商籁设置”分组。
- `Strings/zh-CN`、`Strings/en-us`：`common` 背景显示名改为“通用 / Common”，移除封面滤镜卡片的 Nomand 描述，分组内标题去除重复模式前缀；持久化值保持不变。

## 2026-10-20 商籁几何、转场与实时动感

- `FoliaSonnet`、`FoliaSonnetGraphics`、`External/Folia/sonnet-graphics.js`：接入原版 100 种主几何、8 种 HUD、8 种固定几何和 6 种漂浮装饰布局，保留逐笔展开、固定层反向旋转、装饰视差与入场；后台准备原生路径，逐帧复用。
- `FoliaGpuEffect.Sonnet`、`Sonnet.hlsl`：商籁使用独立透明场景进行镜头与段落转场，窗口画框与底部译文独立叠加；新增文字引导线和可选胶片／镜头后处理。
- `FoliaRealtimeMotion`、`FoliaDiorama`、`FoliaPendolo`：使用现有频谱瞬态驱动商籁推镜裂光、镜台涟漪与当前词跳动、时计震动与亮起；暂停、跳转和场景切换清空瞬态状态，不引入整曲模型分析。
- `AppSettings`、`SaveSetting`、`DataJsonService`、`SettingViewModel.Folia`、设置页与双语资源：增加商籁分层／转场开关、后处理开关、实时动感总开关和三个模式的独立强度；实时动感和后处理默认关闭，缺失字段沿用该默认值。

## 2026-10-18 许可证切换为 AGPL-3.0 与 README 同步

- `LICENSE.txt`：由 MIT 替换为 GNU AGPL-3.0（辞曲新境移植自 chthollyphile/folia-major，同许可证）；此后分发或通过网络提供修改版须按 AGPL-3.0 提供对应完整源码。
- `README.md`、`README.en.md`：许可证标识改为 AGPL-3.0；特性说明同步辞曲新境（13 种歌词模式 / 5 种动态背景 / 5 种封面滤镜）、歌词来源与 GPU 字形、着色器管理；致谢表新增 folia-major、Paper Shaders、Lyricify-Lyrics-Helper、msdfgen、Jint / Acornima / Earcut，并把 music_player 更正为 OriginalSound（original-sound-hq-player）仓库链接。

## 2026-10-18 全屏边缘缝隙修复

- `MainWindow.xaml.cs`：进入全屏时清零可视化容器外边距并关闭标题栏扩展，退出时恢复原布局，消除四周留白与顶部保留区。
- `MainWindow.Wallpaper.cs`：壁纸切换复用全屏入口，在退出全屏后保存小组件布局，避免往返切换或壁纸挂载失败后恢复错误边距。

## 2026-10-18 印象封面比例、清晰度与切歌连续播放

- `FoliaMonet`：宽窗口的封面、标题和艺术家整体宽度缩至左半区的一半，并在左半区居中；歌词继续使用右半区，紧凑布局保持原有上下结构。
- `FoliaPlaybackService`：封面解码最长边上限由 512 提高到 1536 像素，小图不放大，避免高清 SMTC 封面先缩小再在高 DPI 下被放大。
- `FoliaGpuEffect`：新场景准备期间保留上一首的独立播放时钟，播放中的歌词与动画继续推进，暂停状态保持暂停；场景就绪后整组切换到新曲及其当前时钟。

## 2026-10-18 GPU 字形生成与跨歌曲增量图集

- `FoliaGlyphOutline`、`GlyphGenerator`、`GlyphCorrection`：后台提取原始贝塞尔轮廓与着色信息，D3D12 计算距离场、边缘纠错和高斯阴影；设备不支持、初始化失败或复杂轮廓时保留后台 CPU 生成。
- `FoliaGlyphStore`、`FoliaGpuGlyphAtlas`：按字体、字重、斜体和字素复用分页图集，只生成缺失字形并上传其轮廓；CPU 回退仅上传缺失图块。扩容由 GPU 保留已有页，LRU 仅淘汰未引用字形，超过同时驻留上限时使用独立 CPU 图集。
- `FoliaScenePreparation`、`FoliaSceneUpload`、`FoliaGpuEffect`：准备和显示中的场景固定引用字形，提交完成后才交接场景与回收资源；取消中的已完成字形仍可复用。图集在效果实例生命周期内保留，不增加逐帧 I/O。
- `ShaderCatalog`、`GpuPipelineCache`：正式预编译新增计算入口，计算管线缓存随源码、资源契约及驱动标识失效。

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
