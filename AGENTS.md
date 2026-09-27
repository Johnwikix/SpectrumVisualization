# 项目约定

SpectrumVisualization（WinExSpectrumTest）：WinUI 3 音频频谱可视化应用。WASAPI 环回采集（NAudio）→ `SpectrumAnalyzer` → 极光之环的 Win2D 渲染线程或音域回响的独立 D3D12 渲染线程；含 HDR、壁纸模式、SMTC 封面/标题读取、托盘集成。net10.0-windows10.0.26100.0，仅 x64，MSIX 打包，发布走 NativeAOT。

## 渲染线程与音频管线（热路径零分配）

- `IVisualizerEffect.Update/Draw` 每帧在 `CanvasAnimatedControl` 渲染线程调用，**禁止堆分配**：频谱数据、顶点、颜色等缓冲区必须在 `Initialize`/`OnResize` 预分配并逐帧复用。
- 与 UI 线程交互只能经 `RunOnGameLoopThreadAsync` 或 `DispatcherQueue`。`CanvasPanel` 串行切换两个宿主并等待旧帧退出；暂停不等于在途帧结束。释放必须在 Win2D 回调屏障 / D3D12 工作线程停止后进行，UI 不得同步等待 GPU 或 Join 渲染线程。
- 音频数据只从 `SpectrumAnalyzer` 发布的预分配 float 缓冲区读取（512 线性频段 + `FeatureIndex` 特征向量），不要在效果里另起捕获或每帧复制大数组。
- 新增可视化效果：选择宿主并在 `EffectRegistry` 注册名称；`IVisualizerEffect` 仅用于 Win2D。`Id` 即 `SaveSetting.VisualEffect` 的持久化值，发布后不可更改；显示名称走本地化资源。
- 音域回响的 ComputeSharp 计算着色器位于 `Effects/Sonic/Shaders`；`Rendering/SonicGraphics` 独占 GPU 资源，FP16 线性场景最终编码为 RGB10 的 SDR 或 HDR10。工作线程帧循环同样禁止堆分配。`SonicPanel` 将 DIP 换算为物理像素并施加逆 DPI 缩放；改窗口尺寸逻辑时同时核对着色器、粒子和 XAML 媒体卡片坐标。

## NativeAOT 发布约束

- 项目以 `PublishAot` + `PublishTrimmed` + SelfContained 发布，仅 Release publish 生效，Debug 构建不覆盖这些路径。改动涉及反射、序列化、COM/WinRT 封送时，必须跑一次 `dotnet publish` 验证，普通 build 通过不算数。
- JSON 序列化必须走 `Manager/SettingsJsonContext` 源生成；禁止使用 `JsonSerializer` 的反射重载。
- 原生互操作用 Vanara.PInvoke 或 `unsafe`，注意委托与原生指针的封送和生命周期（此前出现过 AOT 下封送错误）。
- `doc/verification` 下的探针工程（AudioProbe、AuroraRenderProbe 等）已从主工程排除，不影响主构建，可作最小复现与测量场景。

## 设置体系（一处改动要同步多处）

- 三层结构：`Model/AppSettings`（static 运行时状态 + `Changed` 事件，渲染侧消费）、`Model/SaveSetting`（持久化 DTO）、`Manager/SettingManager`（读写 `ApplicationData.LocalFolder` 下的 appsettings.json，源生成序列化）；`Service/DataJsonService` 负责双向映射。
- 新增一个设置项至少同步 4 处：`AppSettings`、`SaveSetting`、`DataJsonService.LoadSettingAsync`、`CreateSaveSetting`；要进设置页再加 `SettingViewModel`、`SettingWindow.xaml` 和两份 resw。默认值在 `AppSettings` 与 `SaveSetting` 各写一份，必须保持一致。
- `SaveSetting` 的属性名即 JSON 字段名：不要重命名（包括小写开头的 `elementTheme`），否则存量用户的 appsettings.json 字段会静默失效。

## 本地化

- `x:Uid` 使用属性后缀资源名，如 `x:Uid="WallpaperMode"` 对应 `WallpaperMode.Text`；代码取词用 `new ResourceLoader().GetString("WallpaperErrorTitle")`，参数是无属性后缀的独立资源键。`WallpaperMode.Text` 这类属性资源名不能直接作为 `GetString` 的参数——此前反复出现界面显示原始键名的问题，修改 UI 时务必检查这一点。
- `Strings/en-us` 与 `Strings/zh-CN` 的 `Resources.resw` 必须同步新增或修改；提交前静态核对新增键与两份资源是否匹配。构建成功不能证明运行时资源解析正确。

## UI 风格

- 保持 MVVM：设置状态和命令放在 ViewModel（写 `AppSettings`），视图负责布局和交互转发。
- SettingsCard 中并列的开关与数值输入优先同一行、垂直居中；不要给横向使用的 ToggleSwitch 添加 Header，导致卡片增高或标题重叠。
- 主窗口有小组件/壁纸两态（`MainWindow.Wallpaper.cs` partial）：涉及窗口属性、DPI、焦点时，注意壁纸模式不抢焦点、不置顶、副屏不受影响的约束。

## 功能变更记录（doc/Changelog.md）

- 每次功能修改（新功能、行为变更、默认值调整、面向用户的修复）在 `doc/Changelog.md` 顶部追加一条记录，方便 review；全部变更共用这一个文件，不按主题或日期另开新篇。
- 条目尽量简洁：`## 日期 标题` + 每个改动一行（文件 + 做了什么），仅在对 review 有影响时补充兼容性/验证说明（如默认值变更是否影响存量用户）。
- 纯注释、格式化、构建脚本等不改变运行行为的修改不记录。

## 默认代码质量与审查标准

后续功能实现、修复、重构和 code review 默认执行以下高标准，无需用户重复提醒。审查必须覆盖功能正确性、重构方案设计、性能、内存分配和代码可读性；按风险投入验证，不以"能编译"或"已拆分类"作为完成标准。

- **功能正确性**：沿真实调用链核对行为与兼容性，覆盖正常流程、空状态、失败、取消、重试、并发及启动中退出。效果切换、壁纸开关、采集启停等多入口命令应统一可用条件和执行守卫；显式用户意图不能因去重或单飞机制被静默丢弃。
- **重构设计**：先明确职责、依赖方向、状态来源和资源所有权，再确定拆分边界。保持 MVVM 和单一状态源（运行时状态归 `AppSettings`，持久化归 `SaveSetting`）；避免只搬移代码却继续隐藏依赖或副作用。构造函数保持轻量，采集、渲染、壁纸宿主的启动与停止显式管理；优先构造注入，必要的延迟解析集中在明确边界并说明原因。
- **异步与生命周期**：UI 属性、绑定集合和命令通知必须在 UI 线程发布；渲染线程回调不得触碰 UI 对象。取消等待不等于底层工作已停止；退出需要处理在途帧、迟到的渲染回调、事件订阅解绑和依赖释放顺序。清理应幂等，单项失败不能跳过其他资源；后台任务必须有明确的异常观察与收尾责任。
- **性能**：检查 UI 线程阻塞、重复 I/O、事件风暴和每帧热路径的重复计算；关闭功能时核对其后台活动（采集、定时器、壁纸宿主）是否也停止。区分冷路径与热路径，优先解决可感知延迟和掉帧，不为微优化引入不必要的复杂度。
- **内存分配**：检查每帧热路径的闭包、装箱、LINQ/匿名对象、字符串和集合复制，以及 GPU 资源（CanvasBitmap、D2D effect、着色器缓冲）与原生句柄的生命周期。Span/Memory/ArrayPool 仅在收益明确且所有权、线程与异步边界安全时使用；池化缓冲区必须可靠归还。区分短期分配、长期保留与实际泄漏，没有测量不得宣称零分配或具体收益比例。
- **可读性**：命名准确、控制流清楚、方法职责集中；避免在一行压缩多个状态变更或清理操作。注释解释约束和原因，并随实现同步更新；移除迁移后无效的状态、重复守卫、过时注释和无用依赖。
- **验证证据**：运行与改动风险相称的构建、回归或最小复现；涉及时用 `doc/verification` 探针和 Release publish 覆盖 AOT/裁剪路径。测试桩应保留真实边界的异步时序、线程切换与失败行为；涉及 WinUI、WASAPI 采集、壁纸宿主或原生资源时，桩测试通过不能代替集成验证。性能结论注明场景与测量依据，未执行的设备/UI 验证明确列出。
- **审查输出**：按严重性报告可定位、可复现或有完整调用链证据的问题，给出文件位置、触发条件、影响和修复方向。区分本次引入的回归、既有问题与设计建议；不得为了覆盖维度而凑问题。说明已执行验证及剩余盲区。仅请求 review 时默认交付审查结果，生产代码修复按用户授权范围执行。
