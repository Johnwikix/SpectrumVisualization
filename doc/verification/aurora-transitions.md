# Aurora Ring 动画与频谱衔接验证

2026-09-27

## 行为

- 封面固定使用参考 `ImageSwitcher.ScaleInOut` 的 350 ms 过渡：旧图缩至 0.8 并淡出，新图从 0.85 放大至 1 并淡入。保留旋转、律动、透明度和圆形裁剪。
- `设置 → Aurora Ring → SMTC 文字切换动画` 可选择参考 AnimatedTextBlock 的八种效果。默认“关闭（原有淡入）”；标题和艺术家参与动画，播放时间继续按秒更新。
- 动画前景和阴影源使用同一字形状态；原有阴影强度、自适应文字颜色和文字透明度继续生效。
- 封面解码带版本检查，完成结果交给绘制线程应用；快速切歌或效果释放后，过期图片不能覆盖当前图片。

## 频谱根因与修复

原实现从左声道直接切换到右声道，首尾未参与三点平滑，镜像两侧的颜色/幅度补偿使用条带边界而非中心。固定输入左声道 0.8、右声道 0.2 时，32 条下低频接缝差为 0.412500，回归测试稳定失败。

修复将两个接缝附近逐渐混合到声道均值，其余位置保留独立声道；使用条带中心构造对称频率位置，并在独立缓冲区完成首尾相连的平滑。峰值帽在空间平滑后更新，避免落入条体。奇数条数向下取偶数以保持两个半圆对称。

`AuroraProbe` 覆盖 8/12/44.1/48/96 kHz、32/128/255/512 条、左右声道差异、单声道镜像、静音、峰值帽和采样率重建。全部通过；512 条低频接缝差为 0.000813。

## 实际绘制与设置

独立 WinUI 宿主 `AuroraRenderProbe` 调用生产 `AuroraRingEffect`，生成测试封面与元数据，不修改播放器会话。实际 Win2D 绘制验证通过：

- 两张封面的缩放交叠、完成后的旧位图释放。
- 八种文字动画及原有淡入，测试字符串包括中文、组合字符、emoji、RTL 与长标题省略。
- 动画期间阴影层非空、两层阴影存在、透明度改变后阴影源同步更新。
- 动画完成、重复元数据不重启、快速切歌只应用最新封面、清空媒体、缩放窗口及解码未结束时释放效果。

结果：[aurora-render-results.txt](aurora-render-results.txt)。帧截图：[封面过渡](aurora-cover-transition.png)、[文字过渡](aurora-text-transition.png)。

lvt 实际操作设置页，选择“逐字缩放”，关闭窗口检查 JSON 为 `default`，重开设置检查该项仍选中；测试后恢复原 Debug 配置。截图：[设置页](aurora-settings.png)。

Debug 编译和 Release NativeAOT 发布通过。既有 nullable、旧 API、版本信息读取及缺少 `win-x64.pubxml` 的警告仍存在。`git diff --check` 通过。

## 重跑

在本目录运行（由 `global.json` 选择 .NET 10 SDK）：

```powershell
dotnet run --project AuroraProbe/AuroraProbe.csproj
dotnet build AuroraRenderProbe/AuroraRenderProbe.csproj -c Debug -p:Platform=x64 -p:WindowsPackageType=None -p:GenerateAppxPackageOnBuild=false -p:AppxPackage=false -p:EnableMsixTooling=false -p:PublishAot=false
& ./AuroraRenderProbe/bin/x64/Debug/net10.0-windows10.0.26100.0/win-x64/AuroraRenderProbe.exe
dotnet publish ../../WinExSpectrumTest.csproj -c Release -r win-x64 -p:Platform=x64 -p:WindowsPackageType=None -p:GenerateAppxPackageOnBuild=false -p:AppxPackage=false -p:EnableMsixTooling=false -p:PublishReadyToRun=false
```
