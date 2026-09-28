# SMAA 与时域重建验证

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

厂商 SDK 不提交二进制，恢复来自固定官方提交，22 个文件逐一验证 SHA-256。新检出工作区需先在仓库根目录执行：

```powershell
./External/Upscalers/Restore.ps1
./External/Upscalers/Restore.ps1 -VerifyOnly
./Native/Reconstruction/Build.ps1
```

原生适配层需要 PowerShell 7、VS 2026 C++ x64 工具集 v145 和 Windows SDK。正常项目构建会验证 SDK 并构建适配层，不会隐式联网下载。复用已构建且 ABI 匹配的适配层时可加 `SkipReconstructionNativeBuild=true`。使用 `EnableVendorReconstruction=false` 可构建仅含空间 AA 的版本，必须使用干净输出目录，避免旧 DLL 影响能力探测。

以下命令工作目录为 `doc/verification`，使用该目录的 .NET 10 SDK 约束：

```powershell
dotnet publish HdrProbe/HdrProbe.csproj -c Release -r win-x64 -p:PublishAot=true -p:PublishTrimmed=true -p:SkipReconstructionNativeBuild=true -o HdrProbe/bin/reconstruction-aot
./HdrProbe/bin/reconstruction-aot/HdrProbe.exe --reconstruction
./HdrProbe/bin/reconstruction-aot/HdrProbe.exe --benchmark-reconstruction
```

本次主项目还执行了以下 NativeAOT 无包发布，只生成文件，未启动主应用：

```powershell
dotnet publish ../../WinExSpectrumTest.csproj -c Release -r win-x64 -p:Platform=x64 -p:WindowsPackageType=None -p:AppxPackage=false -p:GenerateAppxPackageOnBuild=false -p:EnableMsixTooling=false -p:AppxPackageSigningEnabled=false -p:PublishAot=true -p:PublishTrimmed=true -p:SkipReconstructionNativeBuild=true -o HdrProbe/bin/app-reconstruction
```

SDK 部署、固定版本、体积和发布许可要求见 `External/Upscalers/README.md`。可选 DLL 合计约 160 MiB；DLSS 的商标、署名、终端许可和商业发布通知仍需发布者按 SDK 许可完成。本次没有代为通知、申请许可审批或发布软件。

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

## 留给用户的实机验收

- Arc 140T 上真实 1440p / 120 FPS、持续运行功耗、帧时间稳定性和桌面合成负载。
- SMAA / XeSS / FSR 的运动细节、闪烁、重影、音频突变及粒子观感；离屏非空和有限值不等于感知画质合格。
- 设置页选择、可用性 / 回退提示、重启持久化、多 DPI、壁纸 / 小组件切换及 HDR 显示观感。
- RTX 设备上的 DLSS / DLAA 实际执行、模式切换、画质和生命周期。
- MSIX 打包 / 安装 / 发布验收及厂商发布许可要求；本次仅检查共享 Content 声明和无包发布输出，没有执行 MSIX 安装或认证。
