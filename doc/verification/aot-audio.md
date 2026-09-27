# NativeAOT 音频采样回归

## 根因与修复

在同一个 96 kHz 输出设备上，普通构建可正常采样；NativeAOT 构建读出 44.1 kHz，`StartRecording` 抛出 `AudioFormatNotSupportedException (0x88890008)`。原先异常仅写到 Debug 输出，发布版表现为没有音频响应。

NAudio 3.0.1 的 `WaveFormatExtensible : WaveFormat` 使用继承式顺序布局类进行封送。本机 .NET 10 NativeAOT 下，已知 96 kHz 扩展格式往返封送失败，原生 header 的采样率偏移处得到 196608，格式标签也不正确。`AudioProbe --naudio-format` 保留该依赖库问题的独立复现（AOT 下预期失败）。

`LoopbackWaveFormat` 使用源码生成的 COM 接口，从同一设备 ID 的 `IAudioClient.GetMixFormat` 原生头读取采样率和声道数，不封送继承类；随后使用非继承的 IEEE float `WaveFormat` 请求 32 位浮点采样，与分析器解码路径一致。初始化和设备重建共用此路径，保留 20 ms 缓冲与设备事件驱动。启动失败和首次处理异常改为调用应用错误日志。

## 验证

NativeAOT 探针直接编译生产 `SpectrumAnalyzer.cs`、`LoopbackWaveFormat.cs`，以低音量调幅测试音采集 8 秒：

| 指标 | 修复前 AOT | 修复后 AOT |
| --- | ---: | ---: |
| 设备采样率 | 错误的 44100 | 96000 |
| 回调次数 | 0 | 800 |
| FFT 发布次数 | 0 | 801 |
| 频带峰值 | 0 | 0.4657 |
| 平均间隔 | 无回调 | 10.00 ms |
| 最大间隔 | 无回调 | 10.65 ms |
| 大于 25 ms 的间隔 | 无回调 | 0 |

`--format` 验证 44.1/48/96/192 kHz × 1/2/6/8 声道共 16 种原生头的读取和浮点请求格式编码。多声道检查是格式级测试，不代表已测试对应硬件。

在本目录运行：

```powershell
dotnet publish AudioProbe.csproj -c Release -r win-x64 -p:PublishAot=true -p:BuiltInComInteropSupport=true -o bin/aot-audio-fixed
./bin/aot-audio-fixed/AudioProbe.exe --format
./bin/aot-audio-fixed/AudioProbe.exe --tone
dotnet run --project AudioProbe.csproj -c Release -- --tone
```

完整 WinUI Release NativeAOT 发布成功，产物位于 `bin/x64/Release/net10.0-windows10.0.26100.0/win-x64/publish/Spectrum.exe`。没有覆盖用户已有的包签名配置；本次产物为非打包发布，未安装或验证 MSIX 包。
