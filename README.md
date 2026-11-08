# GRBL Tool Mobile

Android GRBL 控制工具。C# / .NET for Android / Android 原生 Views / SkiaSharp。

## 当前里程碑

- 完整技术方案和开源优先复用矩阵。
- 独立领域、GRBL、应用、存储、扩展项目。
- 有界响应分帧、逐行流控、故障中断、确认暂停和执行屏障。
- C# 模拟器、SQLite 重启恢复、自动化测试。
- Android USB 设备扫描、SkiaSharp 预览与独立前台模拟服务。
- 未确认的星光硬件 profile 保持未知。

当前版本的设备入口为只读 USB 发现；任务入口运行纯软件无光模拟。真实 USB 授权/会话/执行器、完整 CAM 和正式插件工作流见 [路线图](docs/ROADMAP.md)。

## 构建与测试

安装 global.json 指定的 .NET 10 SDK。核心方案可在无 Android workload 的机器上运行。

```sh
dotnet restore GRBL.Tool.Mobile.Core.slnx --locked-mode
dotnet test GRBL.Tool.Mobile.Core.slnx -c Release --no-restore
dotnet run --project tools/LaserMobile.GrblSimulator -c Release
```

Android：

```sh
dotnet workload restore GRBL.Tool.Mobile.slnx
dotnet restore GRBL.Tool.Mobile.slnx --locked-mode
dotnet build src/LaserMobile.Android -c Release -p:AndroidSdkDirectory=/path/to/android-sdk -p:JavaSdkDirectory=/path/to/jdk
```

APK 位于 Android 项目 bin/Release/net10.0-android。Debug 使用 `-c Debug`。

## 开发规则

优先复用 [UsbSerialForAndroid](https://github.com/anotherlab/UsbSerialForAndroid)、[SkiaSharp](https://github.com/mono/SkiaSharp)、适用的 LaserGRBL 算法与其它成熟库。

- [完整技术方案](docs/ANDROID_IMPLEMENTATION_PLAN.md)
- [开源复用矩阵](docs/reuse-matrix.md)
- [架构决定](docs/adr/README.md)
- [验证记录](docs/VALIDATION.md)
- [贡献说明](CONTRIBUTING.md)

源码采用 GPL-3.0-or-later；第三方依赖保留各自许可证，见 [声明](THIRD_PARTY_NOTICES.md)。
