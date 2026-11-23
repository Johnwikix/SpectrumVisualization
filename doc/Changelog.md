# Changelog

功能变更记录，最新在前；格式约定见根目录 AGENTS.md。

## 2026-11-23 重设计场景快捷切换

- `MainWindow` 用显示当前名称的场景选择器替换上一/下一按钮，可直接选择主效果和辞曲新境样式，保留 Ctrl+←/→ 快捷键，并补齐窗口按钮的双语提示。
- `NotifyIconControl` 将极光之环、音域回响提升到托盘首层，辞曲新境子菜单显示当前样式；`SettingViewModel` 统一维护两处菜单的命令、当前名称与选中状态。
- `SettingViewModel` 在町景下加入按歌曲自动选择、便利店街角、加油站停车场和九龙城寨酒吧四个快捷布景项，选择后直接进入町景，顶部名称同步显示当前布景。

## 2026-11-22 修复町景长焦运镜顿挫和积雪方格纹

- `FoliaMachi.Assets` 平滑构图边界之间的交接，减轻九龙等场景的长焦运镜顿挫并保留原有静止构图。
- `FoliaMachi.Assets`、`MachiDxrBackend` 和町景着色器根据镜头朝向构造射线，提高运镜时场景、薄雾和蒸汽的定位精度。
- `MachiTrace.hlsl` 用固定在世界坐标中的细小雪粒表现积雪和融雪，根据像素覆盖范围过滤远处雪纹，去除规则方格边。

## 2026-11-22 改善町景积雪过渡、外围夜景和雨雪渲染效率

- `MachiTrace.hlsl` 让积雪从薄层逐步连成边缘柔和的雪斑，融化时露出原有路面和积水反光。远处按像素覆盖范围过滤细纹。
- `neighborhood.py` 和三份町景资产为四排外围建筑的正面和侧面补上室内窗光，为近处对街店屋和仓库补上侧窗、背窗和装卸口照明。
- `FoliaGpuEffect.NativeStages` 和 `Machi.hlsl` 仅提交当前天气里可见的粒子层。纯雨跳过雪片，纯雪跳过雨线和屋檐滴水，雨雪交替时保留两层。
- `FoliaGpuEffect.NativeStages` 在镜头静止时复用取景和投影矩阵，切换布景、焦距、画幅或开始运镜时重新计算。

## 2026-11-22 町景镜头焦距扩大到 28–105 mm，默认 55 mm

- `AppSettings` 将町景焦距范围由 28–65 mm 调整为 28–105 mm，默认值由 40 mm 改为 55 mm；设置页滑块的上下限和 `SaveSetting` 的默认值都引用这些常量，无需另行修改。
- `FoliaMachi.Assets` 以默认焦距作为取景基准，默认镜头下按构图点、字幕预留和缩放取景；广角端的相机抬高补偿量随新基准增大，长焦端按默认与当前焦距的几何插值推近。
- 兼容性：已有 `appsettings.json` 中保存的 40 mm 不会被改写，继续按 40 mm 运行；新配置取 55 mm。`README.md`、`README.en.md` 同步镜头范围说明。

## 2026-11-22 町景新增镜头焦距，调整三景灯光并修复手推车

- `FoliaMachi.Assets`、`SettingWindow` 和设置读写新增 28–65 mm 等效焦距，默认 40 mm，缓慢运镜、景深与时域重建共用当前镜头参数。
- `AppSettings` 和 `SaveSetting` 默认启用町景独立画面设置，渲染比例为 67%。已有配置保留已保存的开关与比例。
- `Build/FoliaMachi` 与三份场景资产为便利店增设薄荷绿门框灯与琥珀壁灯，为加油站增设蓝色檐灯与双色泵岛灯，为九龙增设酒吧门柱与摊下灯，并调整立面、招牌和街具的配色。
- `neighborhood.py` 为三景的临街商铺、旅馆与唐楼分别搭配彩色雨篷、发光招牌和楼上窗光。
- `export-scene.mjs` 为九龙手推车补齐贯通轮轴、轮毂、车斗支架和停车脚，两轮与停车脚均贴合巷道铺面。

## 2026-11-22 町景改用自然透视，扩建街区并重做蒸汽与植被

- `FoliaMachi.Assets`、`MachiDxrBackend` 和町景着色器让三个场景共用 38° 透视镜头，按构图点的实际深度取景，并同步调整景深、路面纹理和时域重建。
- `Build/FoliaMachi` 和三份场景资产补入外围街坊及对街的低层建筑，细化种植槽灌木和九龙窗台盆栽。每景 60 万个三角形的上限包含全部动态道具。
- `export-scene.mjs` 和 `fit-vehicles.mjs` 重建便利店四台空调的扇叶与护栅，将九龙两辆踏板车分开停放。
- `FoliaMachi.Environment`、`FoliaGpuEffect.NativeStages` 和 `Machi.hlsl` 沿视线积分井盖、排风口和锅灶的体积蒸汽。九龙锅烟沿棚下向前檐扩散，由场景深度遮挡。

## 2026-11-22 缩短 DLSS 启动等待

- `App.xaml.cs`、`GpuShaderWarmup` 和 `GpuRenderer` 根据启动效果选择预热路径。以 GPU 效果启动时，后台仅探测同一设备支持的重建能力，渲染线程提交首帧后再初始化已选模式。从 Win2D 切到 GPU 时，后台预热在当前工作和 SDK 清理结束后交接。
- `Native/Reconstruction/Reconstruction.cpp` 优先查询同一显卡的 DLSS 超分和光线重建支持要求，查询不可用时回退到各自原有的 NGX 初始化探测。实际创建效果时仍完整初始化 SDK。

## 2026-11-22 修复町景便利店的架空电线，优化路径追踪的光源数据读取

- `Build/FoliaMachi` 和 `konbini.machi.br` 将便利店两侧的五线跨距接到电杆瓷瓶，给中性线增加独立支架，并把变压器引线接至店外入户管。
- `MachiDxrBackend.Geometry` 和 `MachiDxrBackend.Pipelines` 为内联光追将静态光源采样表和三角形概率密度复制到 GPU 默认堆，首帧提交完成后释放上传缓冲。旧版 TraceRay 沿用原有资源路径。
- RTX 5070 Ti、1080p NRD 离屏对照中，使用修线后的便利店，两版末帧逐位一致，雨夜与白昼的整帧中位耗时分别从 6.900 降至 6.718 ms、从 6.637 降至 6.554 ms。加油站和九龙的昼夜交叉对照中，追踪阶段从 3.554、3.268 ms 降至 3.506、3.236 ms，整帧中位数分别为 6.907 → 6.890、6.541 → 6.632 ms。

## 2026-11-22 增强町景的霓虹打光，降低路径追踪帧耗

- `Build/FoliaMachi`、`External/Folia/Machi` 和 `FoliaMachi.Environment` 为便利店增加珊瑚红与青蓝雨棚灯，为加油站加强冷色棚灯与玫红旅馆招牌，为九龙增加粉青巷灯及紫色夜光。静态招牌和部分漆面重新调色，动态歌词仍使用原来的配色。
- `MachiTrace.hlsl`、`MachiDxrBackend.Pipelines` 和 `ShaderCatalog` 在支持 DXR 1.1 和 Shader Model 6.5 的设备上使用内联射线查询，复用全息歌词透明度测试，并为旧设备保留原追踪管线。
- `MachiDxrBackend` 和 `MachiTrace.hlsl` 按实际降噪器写入辅助纹理。DLSS 光线重建执行失败时，用相同采样、运动和抖动在该帧补齐 NRD 输入。`Native/Reconstruction/Nrd.cpp` 复用纹理描述符，合并资源屏障并跟踪反射、透射共用纹理的状态。
- RTX 5070 Ti、1080p 原生分辨率、夜雨、每像素 1 次采样、最多 8 次弹射和 NRD 下，便利店、加油站、九龙离屏场景（含后期与歌词）的 GPU 帧耗分别从 14.91、16.59、17.89 毫秒降至 7.14、7.12、6.93 毫秒。每场景每版本测量两轮，每轮 84 个稳态帧，取两轮中位数的平均值。

## 2026-11-21 修复在播启动的歌词和双语字幕，加快歌词模式切换

- `MediaInfoService`、`MediaPlaybackClock` 和 `FoliaPlaybackService` 按 SMTC 样本时间、播放状态和倍速定位首帧，时长暂缺时继续走时，同曲补齐时长后可重新匹配此前未找到的歌词。
- `LyricsTextFile` 和 `FoliaSong` 保留 QRC 文件的原始换行，并按行头和逐字时间恢复旧缓存的歌词分行。`Build/FoliaNative` 让雾笺的超长文字折行，保留逐字时序。
- `FoliaLyricLayout`、`FoliaSong` 和 `FoliaGpuEffect.NativeStages` 用当前行的原文、译文绘制底部字幕，按字形边界适配小窗口，并在雾笺玻璃层合成后叠加字幕。
- `FoliaSourcePlanner` 复用同一字图的分词、字形宽度与逐字时序。`FoliaSonnet` 和 `FoliaTempera` 通过弱引用复用最近一次匹配的镜头布局，切回时重新建立本次动画状态。
- `FoliaGpuEffect.MachiPipeline`、`MachiDxrBackend.Pipelines` 和 `FoliaScenePreparation` 在后台准备町景追踪管线，快速返回时复用同设备管线及尚可用的街区模型，等待期间继续绘制当前模式。

## 2026-11-21 修复雾笺英俄歌词，降低歌词合成、路径追踪和频谱显示开销

- `FoliaNebbiaStrokes`、`FoliaNebbia` 和 `Build/FoliaNative` 按字体字宽、基线和连续笔画绘制雾笺的回退字形，保留字腔、附加符号和标点。`FoliaDistanceField` 复用 DirectWrite 接口读取 NativeAOT 所需的基线，纯俄文可提前结束汉字笔顺表的扫描。
- `FoliaGpuEffect` 和 `Print.hlsl` 让普通歌词直接在背景上混合，按模式分配第二张全屏 FP16 纹理，提交新图集后释放闲置字图和上传缓冲。
- `FoliaGpuEffect` 和 `FoliaLineTiming` 合并三块常量上传资源，用 CPU 缓存准备帧参数，复用当前行索引并跳过已退场的歌词。
- `MachiTrace.hlsl` 在区域光源的阴影射线找到首个有效遮挡后结束遍历，保留透明招牌的可见性判定。`MachiDxrBackend` 将追踪前后的纹理状态转换分别合并成一次提交。
- `SpectrumAnalyzer` 和 `RadialSpectrumState` 复用单声道混音和左右声道的 FFT 工作缓冲，按频段求幅值，极光之环的左右镜像频段共用一次峰值查找。
- `SonicGpuEffect` 和 `GpuGraphics.Pipeline` 为活跃帧槽合并常量和粒子上传缓冲，退出双帧渲染后释放附加队列和命令分配器。
- 离屏验证覆盖 16 个歌词模式的 114 组同帧 FP16 合成对照、町景 232 帧的 3,872 张输出与辅助纹理，以及含「霧」的中文手写蒙版，以上回读均与改动前一致。

## 2026-11-21 修复町景俄文等文字的词内扫光

- `FoliaMachi.Plan`、`FoliaWordSegmentation` 移除拉丁字母白名单，让俄文、希腊文和其他字母文字在原词时间内按实际字宽连续扫光。汉字、假名、韩文沿用原有音节效果。

## 2026-11-21 切歌时保留画面历史，町景英文歌词按词扫光，条屏定点轮播

- `MediaInfoService`、`FoliaPlaybackService` 合并重复的 SMTC 读取，按先后顺序接收曲目与时间线，封面解码串行执行并丢弃过时结果。
- `FoliaGpuEffect`、`FoliaScenePreparation`、`FoliaSceneUpload` 和 `Stage.hlsl` 在原舞台准备新歌，沿用已上传的图像进度，对变化的歌词像素单独更新时域历史；取消和旧字图清理在后台完成。
- `FoliaGpuGlyphAtlas` 按字形面积和轮廓复杂度分批生成新字，直接向映射缓冲区写入轮廓，减少渲染线程切歌时的大块分配。
- `FoliaGpuEffect.NativeStages`、`FoliaMachi.Actors` 和 `FoliaMachi.Plan` 让街区相机、车辆和风扇按场景时钟运行，同一布景中切歌、跳播和补载封面均保留追踪与降噪历史。
- `FoliaMachi.Plan` 和 `Stage.hlsl` 按英文单词的实际字宽从左至右扫光，三个布景的横条屏均改为静止显示、每三秒整页切换，长文本自动分页。

## 2026-11-21 切歌保留歌词舞台，町景随机积水并增强逐字扫光

- `FoliaGpuEffect`、`FoliaScenePreparation`、`FoliaLyricLayout` 和 `FoliaPlaybackService` 在切歌、等待歌词和无歌词时保留选中的舞台，收到新曲目后收起旧词，移除通用居中封面卡。
- `FoliaMachi.Plan` 和 `Stage.hlsl` 为正唱音节加入与歌曲封面配色形成对比的扫光，沿字形笔画移动，同一音节内的字共用原始时间戳。
- `FoliaMachi.Environment` 和 `FoliaGpuEffect.NativeStages` 在实际进入布景时随机生成水洼位置，同场景切歌和缩放保留水洼。昼夜循环从夜晚开始，低角度的朝阳和夕阳同时染暖直射光与环境光。
- `AppSettings`、`SaveSetting` 默认使用四分钟昼夜循环，沿用已保存的时段选择。`SettingWindow.xaml` 将町景与雾笺设置的下拉框统一为 220 DIP。

## 2026-11-21 町景积水随雨势扩张，歌词屏跟随封面配色，新增昼夜循环并校准歌词时间轴

- `MachiTrace.hlsl`、`Stage.hlsl` 和 `MachiDxrBackend` 提高三块主歌词屏的发光强度，按实时字色从屏幕正面照亮街区，同步调整面积光源采样权重。
- `FoliaMachi.Plan`、`FoliaScenePreparation` 从传入封面提取广告牌字色和底色，同曲稍后收到封面时更新配色，无封面时使用暖色。
- `FoliaMachi.Environment`、`MachiTrace.hlsl` 分开计算路面湿润和积水深度；雨中低洼处扩张连片，停雨后逐渐退水，边缘露出湿润颗粒，屋檐下积水较少。`Machi.hlsl` 移除地面圆环叠图，涟漪只扰动明水的反射。
- `FoliaMachi.Environment` 和「町景设置 → 时段」新增四分钟昼夜循环，日月沿相对的轨道升落，雨、雾、雪夜继续遮蔽月光。
- `FoliaMachi.Plan` 和 `FoliaGpuEffect.NativeStages` 让广告牌和字幕共用早期模式的当前行，按原始音节时间高亮；纯行时间戳歌词整句点亮。

## 2026-11-21 调整町景日月照明，开放路径追踪采样和反弹设置

- `FoliaMachi.Environment` 降低晴夜月光和夜间天光，雨、雾、雪夜完全遮蔽月光；提高夜间灯具出光和晴昼直射日光。
- `MachiTrace.hlsl` 以 0.526° 角直径采样太阳和月亮，统一玻璃、湿地与不透明表面的反弹上限，多次采样在降噪前合并。
- `SettingWindow` 和设置读写链路新增每像素采样数（1–16）及最大反弹次数（1–32），旧配置默认使用 1 SPP 和 8 次反弹。

## 2026-11-21 町景扩建街区并重做车辆与夜间材质

- `Build/FoliaMachi`、`External/Folia/Machi` 用 Blender 重建街坊、旅馆、九龙后巷和车辆，补入灯具、加油枪、不锈钢水箱和街边设施。完整场景按 50 万三角形封顶。
- `MachiTrace.hlsl` 为柏油、铺装、墙砖、混凝土和金属屋面读取带粗糙度与凹凸信息的平铺贴图，车漆使用独立清漆反射层。
- `FoliaMachi.Actors`、`FoliaMachi.Environment` 将车道和降水延伸出原有地块，调整取景及夜间天光，让周边建筑进入画面。
- `FoliaGpuEffect.NativeStages`、`Machi.hlsl` 删除路灯的叠加光锥和地面补色，街灯由发光网格参与路径追踪，收敛灯牌泛光并保留暗部细节。

## 2026-11-20 修复町景 NRD 运镜重影，增加降噪和镜头选项

- `FoliaMachi.Assets`、`MachiDxrBackend` 向重建器传递精确的正交投影，并将字幕取景偏移计入视图平移，修复三个布景在运镜时误用透视重投影和丢失降噪历史的问题。
- `GpuGraphics`、`MachiDxrBackend` 新增「无降噪」，直接使用原始路径追踪辐亮度，可搭配町景现有的抗锯齿和渲染分辨率设置。该模式仅分配合成与所选超分需要的纹理，并跳过积水的降噪辅助射线。
- `SettingWindow`、`Machi.hlsl` 增加独立的色散、景深模糊和泛光开关，旧配置沿用 NRD 与三个默认开启的镜头效果。

## 2026-11-20 町景独立画质、真实灯具与积水反射

- `GpuGraphics`、`MachiDxrBackend` 按选定输入分辨率追踪，NRD 可接普通 DLSS、XeSS 或 FSR，DLSS RR 可直接重建到输出分辨率；景深、天气和歌词按输出尺寸合成。
- `SettingWindow`、`AppSettings` 增加町景专属画质覆盖开关，可单独保存抗锯齿和分辨率比例，默认继承现有画质设置。状态文本显示当前实际运行的重建算法。
- `Build/FoliaMachi`、`FoliaMachi.Assets` 保留 Blender 灯罩的线性 HDR 发光，在三处布景按实际灯具面积分配对应上游光通量，移除重复的解析补光。
- `export-scene.mjs`、`MachiTrace.hlsl` 让印刷旗面受光显示，站台走马灯按灯泡相位切换发光强度，直视和倒影使用同一时刻的亮度。
- `MachiTrace.hlsl` 以空气和水的折射率、表面粗糙度和真实可见光源计算雨地反射，用额外的距离查询稳定 NRD 湿面引导。玻璃界面拥有独立的追踪预算，灯面穿玻璃的贡献只按折射路径计入一次。
- `MachiTrace.hlsl` 独立采样玻璃后表面的漫反射和镜面反射，并对真实灯面分层采样，降低运镜过程中窗后的亮斑闪烁。两条光路复用同一段循环，缩短首次创建 DXR 管线的时间。
- `Machi.hlsl` 移除降噪后逐帧叠在玻璃和暗部上的全屏胶片颗粒。

## 2026-11-19 修复町景镜头抖动、玻璃噪点与雨地倒影

- `MachiDxrBackend` 统一主射线、深度和降噪的像素采样中心，固定镜头下的招牌、窗框和灯杆轮廓保持稳定。
- `MachiTrace.hlsl` 解析闭合薄窗的反射与透射，保留玻璃后的灯条与招牌细节。RR 单独使用主导光路的深度和运动，景深与降雨继续使用第一层表面的深度。
- `MachiTrace.hlsl` 统一湿膜和水洼的反射与降噪，恢复三处布景雨地上的灯光和建筑倒影。
- `Machi.hlsl` 以中心和成对采样计算景深，修复细杆经过焦区边界时出现的横向断层。

## 2026-11-19 对齐四种歌词场景，町景接入硬件路径追踪

- `FoliaHanabi`、`FoliaMetaphor`、`FoliaNebbia` 使用上游编排，补全花火拼贴、双墨场显影、雾笺排版与玻璃组合。
- `FoliaMachi` 从上游烘焙便利店、加油站和九龙城寨，使用正交取景、场内歌词牌及动态道具，便利店加入精简后的 RainyCorner 陈设。
- `MachiDxrBackend` 与原生重建桥支持硬件 DXR、NRD 和可选 DLSS RR，设置与托盘显示硬件可用性。町景可选择布景、时段、天气和运镜。
- Release NativeAOT 离屏覆盖三种町景、两条降噪路径、模式切换和尺寸变化。上游截图对照覆盖花火 50 个预设和雾笺九种组合。

## 2026-11-19 辞曲新境新增四种歌词动效

- `FoliaHanabi`、`FoliaMetaphor`：增加花火的逐字色块、升空转场和副歌辉光，以及隐喻的墨场、半调显影、镜头硬切和反相。两种模式支持无歌词曲目。
- `FoliaNebbia`、`FoliaMachi`：增加雾笺雨夜车窗、笔顺擦痕与折射，并加入町景便利店、世界空间 LED 歌词和带运动矢量的慢运镜。
- 设置页、托盘及中英资源同步四种模式，隐喻反相接入原有设置持久化。
- 验证：Debug x64、Release NativeAOT、D3D12 离屏回读和中英设置资源检查通过。四种模式各抽测 24 个稳态帧，`PrepareFrame` 和 `RecordScene` 合计托管分配为 0 字节。

## 2026-10-26 修复逐字行末词后的显示空窗

- `FoliaSong`：当前行的 `End` 独立作为显示末界，延伸到下一不同入场时间或曲目末尾；显式行尾与下一入场取较晚值，最后一个 word 的真实结束时间保持不变，避免末词完成后、下一行开始前整行提前消失。处理方式对应参考播放器的 `HighlightEndMs`，不把长间奏伪装成末词发音。
- 验证：增强 LRC、QRC、TTML 的末词早退场景及末行保留通过，`RenderEnd` 和实际 `FindActiveLine` 均覆盖至下一入场；标准 LRC 分词、图集和 Planner／Classic 回归保持通过。

## 2026-10-26 修复标准 LRC 分词与逐字动画

- `FoliaSong`、`FoliaWordSegmentation`：行级歌词按 ICU 词边界拆分，严格保留原文和空白，六点间奏标记仍为单个词；按字素数量生成有界显示时间，长间奏不再被逐字动画铺满，逐字估算尊重显式结束时间且不修改真实逐字数据。
- `FoliaTextAtlas`、`FoliaFume`：字形与烟岚统一消费 word/grapheme 显示时间，不再把整行作为一个词或按完整行尾重算；行级歌词仍保留 `WordSynced=false`，估算时间不用于真实音节强调。
- 验证：Debug x64 构建、标准／增强 LRC、长间奏、空白／CJK／emoji、TTML 显式结束时间、Win2D 离屏图集及 Planner／Classic 映射回归通过；实机歌词动画观感由用户验收。

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
