# SpectrumVisualization

A WinUI3-Based Audio Visualization Tool

## 1. 项目简介 / Project Introduction

SpectrumVisualization是一款基于 **WinUI3 框架**开发的轻量级音频可视化应用，采用 C# 实现核心逻辑。该工具可实时捕获系统或麦克风音频信号，通过动态波形等效果直观展示音频频谱特征，同时集成系统托盘功能，兼顾操作便捷性与 Windows 系统设计风格一致性。

SpectrumVisualization is a lightweight audio visualization application developed based on the **WinUI3 framework**, with core logic implemented in C#. It can capture system or microphone audio signals in real-time, display audio spectrum characteristics intuitively through dynamic waveforms and other effects, and integrate system tray functionality to balance operational convenience with consistency in Windows system design style.

## 2. 核心功能 / Core Features

| 功能描述 / Feature Description | 技术细节 / Technical Details                                 |
| ------------------------------ | ------------------------------------------------------------ |
| 实时音频可视化                 | 支持波浪线等动态效果，实时反映音频频率、强度变化 / Supports dynamic effects like wave lines, reflecting audio frequency and intensity changes in real-time |
| 系统托盘集成                   | 可最小化至托盘，通过右键菜单快速执行 “显示窗口”“退出” 等操作 / Minimizable to system tray, with right-click menu for quick operations like "Show Window" and "Exit" |
| 轻量化架构                     | 基于 WinUI3 现代 UI 框架，界面流畅、资源占用低，适配 Windows 11/10 设计规范 / Built on WinUI3 modern UI framework, with smooth interface, low resource usage, and compatibility with Windows 11/10 design standards |

## 3. 技术栈 / Technology Stack

| 类别 / Category | 具体技术 / Specific Technology                               |
| --------------- | ------------------------------------------------------------ |
| 开发框架        | WinUI3                                                       |
| 编程语言        | C# (100% 代码占比 / Code Proportion)                         |
| 目标系统        | Windows 10 1809 (Build 17763) 及以上 / Windows 10 1809 (Build 17763) or higher |
| 依赖环境        | .NET 8 SDK 及以上 /.NET 8 SDK or higher                      |
| 开发工具        | Visual Studio 2022                                           |

## 4. 项目结构 / Project Structure

```
WinExSpectrumTest/
├─ Analyzer/          # 音频分析模块：处理音频捕获、频谱计算与数据转换 / Audio analysis: capture, spectrum calculation, data conversion
├─ Assets/            # 资源目录：存储图标、配置文件等可视化相关资源 / Resources: icons, config files for visualization
├─ Canvas/            # 画布模块：实现波浪线、频谱图等可视化效果渲染 / Canvas: rendering of wave lines, spectrum charts
├─ Control/           # 自定义控件模块：封装可视化交互组件（如频谱控件） / Custom controls: interactive components like spectrum controls
├─ Helper/            # 工具类模块：提供托盘管理、音频设备检测等辅助逻辑 / Helpers: tray management, audio device detection
├─ Model/             # 数据模型模块：定义托盘菜单、音频参数等数据结构 / Models: data structures for tray menu, audio parameters
├─ Properties/        # 项目属性：包含应用版本、资源字典等配置 / Project properties: app version, resource dictionary
├─ App.xaml(.cs)      # 应用入口：初始化托盘、加载主窗口 / App entry: tray initialization, main window loading
├─ MainWindow.xaml(.cs) # 主窗口：承载可视化界面与用户交互逻辑 / Main window: visualization interface and user interaction
├─ Package.appxmanifest # 打包配置：定义应用名称、权限、支持的系统版本 / Packaging config: app name, permissions, supported OS versions
├─ WinExSpectrumTest.csproj # 项目工程文件：管理依赖、编译配置 / Project file: dependency management, build config
└─ WinExSpectrumTest.sln # 解决方案文件：Visual Studio 项目入口 / Solution file: Visual Studio entry point
```

- 

## 6. 许可证 / License

本项目采用 **MIT 许可证** 开源，允许自由使用、修改和分发，详情见项目根目录下的 LICENSE.txt 文件。

This project is open-source under the **MIT License**, allowing free use, modification, and distribution. For details, see the LICENSE.txt file in the project root directory.