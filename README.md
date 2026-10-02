[**English**](README.en.md) | **中文**

<div align="center">
  <img src="Assets/spectrumLogo.png" alt="SpectrumVisualization Logo" width="160">

  <h1>SpectrumVisualization</h1>

  <h3>让每一次鼓点都看得见</h3>

  <p>
    基于 WinUI 3 / Win2D / D3D12 / ComputeSharp 的轻量级音频频谱可视化工具<br>
    WASAPI 环回 · 双效果页 · HDR10 · 抗锯齿 / 时域重建 · 壁纸模式
  </p>

  <p>
    <a href="https://github.com/Johnwikix/SpectrumVisualization"><img src="https://img.shields.io/badge/GitHub-Johnwikix%2FSpectrumVisualization-181717?logo=github" alt="GitHub"></a>
    <img src="https://img.shields.io/badge/C%23-WinUI_3-purple?logo=dotnet" alt="C# / WinUI 3">
    <img src="https://img.shields.io/badge/.NET-10-512BD4?logo=dotnet" alt=".NET 10">
    <a href="LICENSE.txt"><img src="https://img.shields.io/badge/License-MIT-blue" alt="MIT License"></a>
    <img src="https://img.shields.io/badge/Platform-Windows_10%2F11-0078D4?logo=windows" alt="Windows 10/11">
  </p>

</div>

---

## 目录

- [核心特性](#核心特性)
- [下载与安装](#下载与安装)
- [演示](#演示)
- [社区](#社区)
- [贡献与构建](#贡献与构建)
- [许可与致谢](#许可与致谢)

---

## 核心特性

### 双渲染管线，效果页互补
- **极光之环（Aurora Ring，默认）：** 基于 Win2D 的 SDR 内嵌小组件。封面主色 + 4 色调色板为频谱条着色，节拍触发涟漪圆环并让封面随低音做"呼吸"缩放，自动读取 SMTC 封面与标题。
- **音域回响（Sonic Topography）：** 基于 D3D12 + ComputeSharp 的独立渲染线程。FP16 线性场景，可输出 SDR 或 HDR10；地形高度场随 15 维音频特征实时起伏，节拍与流星在场景里留下涟漪与粒子。

### 抗锯齿与时域重建
- **关闭 / FXAA 3.11 / SMAA 1x HIGH** 三档空间 AA，主机壳无任何 DLL 依赖。
- **XeSS / FSR 3.1 / DLSS + DLAA** 时域重建，DLSS J / K / L / M 模型可独立选择；SDK 头与 DLL 固定 SHA-256，初始化失败自动回退 FXAA 但保留用户选择。
- **公共输出流水线** 统一处理 SDR / HDR10 亮度映射、PQ / sRGB 编码与抖动；切换 HDR 与 AA 不重写用户设置。

### HDR10 输出与硬件探测
- 白点 80–500 nit、峰值 80–4000 nit，按窗口所在显示器自动探测；非 HDR 显示器静默回退 SDR。
- 用户意图（开关）与实际输出状态（HdrStatus）分离，设置页可直接看到当前显示器在 HDR10 / SDR / Unavailable / Failed 之间的实际状态。

### 壁纸模式与自动暂停
- 托盘一键将当前效果铺满主屏桌面图标下方；退出恢复小组件原位置 / 锁定 / 全屏状态。
- 同屏最大化或全屏窗口完整覆盖壁纸时自动暂停渲染，恢复后立即继续；副屏变化、显示桌面、隐藏 / 透明窗口均不会误暂停。

### 自定义标题栏与锁定
- 自绘拖动区 + 最小化 / 最大化 / 关闭按钮，悬停显隐。
- 锁定态使用 `WS_EX_LAYERED` 点击穿透，按钮组通过慢轮询 + 快轮询两档定时器按需放行；不抢焦点、不进任务栏。

### 系统托盘 + SMTC
- 托盘图标右键菜单：切换壁纸模式、解锁、切换效果、设置、退出。
- SMTC 朗读支持：优先 HQPlayer，新会话空枚举双次去重，切回 Aurora 时从缓存恢复信息避免"切歌即丢字"。
- 调试覆盖层：FPS、提交 / 丢弃、CPU / Present / GPU 等待、输入输出尺寸、AA 模式、HDR、呈现方式，按 0.5 Hz 在 UI 线程采样。

### 本地化与设置
- 中 / 英双语 resw，提交前静态检查键集合一致；首启语言按 `Windows.System.UserProfile.GlobalizationPreferences` 自动切换。
- 三层设置：`AppSettings`（运行时 + `Changed` 事件）、`SaveSetting`（持久化 DTO，属性名即 JSON 字段名）、`SettingManager` + 源生成 `SettingsJsonContext`；新增设置项至少同步 4 处，默认值在 `AppSettings` 与 `SaveSetting` 各写一份并保持一致。

### NativeAOT 与裁剪
- Release 启用 `PublishAot=true` / `PublishTrimmed=true` / `SelfContained=true` / `BuiltInComInteropSupport=true` / `PublishReadyToRun=true`，仅 x64。
- 原生桥 (`Native/Reconstruction/`) 通过 C ABI 借用宿主 D3D12 设备、命令列表与纹理，不创建交换链 / 音频端点 / UI 线程 / 帧循环；启动期独立线程、低优先级离屏预热空间 AA 与可用 SR，HLSL 字节码与设备在渲染线程就绪后按需移交。

---

## 下载与安装

<div align="center">

| Microsoft Store (推荐) | 手动安装 |
| :---: | :---: |
| 暂未上架 | **从源码构建 Release NativeAOT**：见下方 [贡献与构建](#贡献与构建) |

**[变更记录](doc/Changelog.md) · [设计与验证](doc/design/) · [许可证](LICENSE.txt)**

</div>

---

## 演示

主窗口默认尺寸 1024×1024，支持小组件 / 全屏 / 最大化 / 锁定四种状态；托盘勾选"壁纸模式（主屏幕）"后效果铺满主屏桌面图标下方。

> 演示动图：[doc/pic/example.gif](doc/pic/example.gif)

### 模式截图

两个效果在窗口小组件、壁纸模式（主屏幕）与锁定模式下的实机截图（窗口居中于 4K 主屏）：

| 效果 | 窗口小组件 | 壁纸模式（主屏幕） | 锁定模式 |
| :--- | :---: | :---: | :---: |
| **极光之环** | <img src="doc/pic/aurora-window.png" width="100%" alt="极光之环 窗口小组件"> | <img src="doc/pic/aurora-wallpaper.png" width="100%" alt="极光之环 壁纸模式"> | <img src="doc/pic/aurora-lock.png" width="100%" alt="极光之环 锁定模式"> |
| **音域回响** | <img src="doc/pic/sonic-window.png" width="100%" alt="音域回响 窗口小组件"> | <img src="doc/pic/sonic-wallpaper.png" width="100%" alt="音域回响 壁纸模式"> | <img src="doc/pic/sonic-lock.png" width="100%" alt="音域回响 锁定模式"> |

效果示例（来自 `doc/verification/` 的离屏 / 实机回归截图）：

- [Aurora 封面过渡](doc/verification/aurora-cover-transition.png)
- [Aurora 文字过渡](doc/verification/aurora-text-transition.png)
- [设置页](doc/verification/aurora-settings.png)
- [音域回响 NativeAOT 离屏](doc/verification/native-sonic.png)
- [音域回响旋转 A](doc/verification/sonic-rotation-a.png)
- [音域回响旋转 B](doc/verification/sonic-rotation-b.png)
- [壁纸模式（主屏）](doc/verification/wallpaper-primary.png)
- [带图标的托盘菜单](doc/verification/wallpaper-tray.png)
- [壁纸关闭后的小组件（UIA 标注）](doc/verification/wallpaper-restored.png)

---

## 社区

按项目惯例，**实机显示与交互测试由用户自行完成**：除非用户当次明确授权，开发者 / 协作者不会启动或操作主应用 / 设置窗口，不执行 UI 自动化、桌面截图、实际呈现帧率采集、壁纸模式切换或显示器 HDR / 分辨率切换，也不通过真实音频播放或采集干预用户环境。验证以离屏探针为主，详见 [验证范围说明](doc/verification/)（[`doc/verification/README.md`](doc/verification/README.md) 索引）。

提交 Issue 或反馈：

- 仓库：[Johnwikix/SpectrumVisualization](https://github.com/Johnwikix/SpectrumVisualization)
- 镜像（[设置页](View/SettingWindow.xaml) 中提供）：[Gitee](https://gitee.com/people_1/win-ex-spectrum-test)

---

## 贡献与构建

### 开发环境

- Windows 10 1809 (10.0.17763.0) 及以上，**x64 only**
- .NET 10 SDK
- Visual Studio 2026 + Windows App SDK / C++ 工作负载（构建原生桥时需要 VS 2026 C++ x64 工具集 v145 和 Windows SDK）
- PowerShell 7

### Debug 构建

```powershell
dotnet build WinExSpectrumTest.csproj -c Debug -p:Platform=x64 -p:WindowsPackageType=None
```

### Release NativeAOT 发布

```powershell
dotnet publish WinExSpectrumTest.csproj -c Release -r win-x64 -p:Platform=x64 `
    -p:PublishAot=true -p:PublishTrimmed=true -p:BuiltInComInteropSupport=true `
    -p:WindowsPackageType=None -p:AppxPackage=false `
    -p:GenerateAppxPackageOnBuild=false -p:EnableMsixTooling=false `
    -p:AppxPackageSigningEnabled=false
```

输出：`bin/x64/Release/net10.0-windows10.0.26100.0/win-x64/publish/Spectrum.exe`。`ReconstructionAssets.targets` 会构建原生桥并把 XeSS / FSR / DLSS 的 release DLL 与许可复制到发布目录。

- 复用已构建桥：`-p:SkipReconstructionNativeBuild=true`
- 只构建空间 AA 版本：`-p:EnableVendorReconstruction=false`（先用干净输出目录）

### 校验厂商 SDK

```powershell
./External/Upscalers/Restore.ps1 -VerifyOnly
./Native/Reconstruction/Build.ps1
```

`Restore.ps1` 校验 SHA-256，缺失文件按官方提交补齐。

### 仓库约定（详见 `AGENTS.md`）

- 修改渲染线程、音频管线、设置体系或本地化后必须重跑对应离屏探针或 Release NativeAOT 发布。
- 任何功能改动在 `doc/Changelog.md` 顶部追加一条记录。
- 默认代码质量与审查标准见 `AGENTS.md` 末尾。

### 离屏探针（独立项目，从主工程排除）

```
doc/verification/
├─ AudioProbe.csproj            # NativeAOT WASAPI 采样率回退与采集节奏验证
├─ AuroraProbe/                 # 极光之环频谱镜像 / 接缝差验证
├─ AuroraRenderProbe/           # 极光之环封面 / 文字过渡实绘制
├─ HdrProbe/                    # HDR / SR / FXAA / SMAA / 时域重建离屏回归
├─ WallpaperOcclusionProbe/     # 全屏 / 最大化遮挡几何回归
└─ desktop.ps1, ui-test.ps1,
   wallpaper-*.ps1              # 桌面 / 状态 / 壁纸 / 托盘回归脚本
```

详情见 [`doc/verification/README.md`](doc/verification/README.md) 与 [`doc/design/gpu-effects-quality-aa.md`](doc/design/gpu-effects-quality-aa.md)。

---

## 许可与致谢

本项目采用 **[MIT 许可证](LICENSE.txt)**。

### 引用与灵感

| 项目 | 用途 |
| --- | --- |
| [BetterLyrics](https://github.com/jayfunc/BetterLyrics) | 早期的 SMTC 文本与封面读取灵感 |
| [music_player](https://) | 自定义标题栏、锁定态穿透、桌面歌词自适应文字色、`AnimatedTextBlock` 文字切换动画、桌面壁纸宿主 |
| [ComputeSharp](https://github.com/Sergio0694/ComputeSharp) | D3D12 计算着色器集成 |
| [Intel XeSS](https://github.com/intel/xess) | 时域重建（J / K / L / M preset） |
| [AMD FidelityFX SDK](https://github.com/GPUOpen-LibrariesAndSDKs/FidelityFX-SDK) | FSR 3.1 |
| [NVIDIA NGX DLSS](https://github.com/NVIDIA/DLSS) | DLSS / DLAA |
| [SMAA 1x](https://github.com/iryoku/smaa) | 空间抗锯齿 |
| [FXAA 3.11](https://download.nvidia.com/developer/tools/SDK/10.5/FXAA_WhitePaper.pdf) | 亮度映射后处理边缘 |

`External/` 下的厂商 SDK 与第三方着色器保留各自原始许可证；`Licenses/` 目录存放随发布产物分发的许可文本。`External/Upscalers/README.md` 列出 XeSS / FSR / DLSS 的固定上游 commit 与 SHA-256；发布者须按各 SDK 许可完成商标 / 署名 / 终端许可 / 商业发布通知等要求，仓库本身不代为提交这些申请。

---

<div align="center">

<sub>本项目以 MIT 许可证开源。所有第三方资源归各自所有者所有。</sub>
<br>
<sub>实机显示 / 交互测试由用户自行完成；构建与离屏验证见 `doc/verification/`。</sub>

</div>