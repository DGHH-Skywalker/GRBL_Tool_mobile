# 架构决定

- 0001：Android-only，C# 与 .NET for Android。复用 C# 生态，直接集成 Android 服务和 USB。
- 0002：原生 Views/XML。当前页面完成设备发现与集成验证；Fragments/AndroidX 在页面扩展阶段加入。
- 0003：服务持有执行生命周期。真实设备采用 connectedDevice 前台服务；DeviceSession 统一串行化读事件、控制和写入，Activity 重建订阅快照。
- 0004：逐行流控、独立 G4 P0 屏障、新 Idle。真实作业要求标准 GRBL 1.1 身份、模态、$30/$32、有效 WPos 及现场确认；fork 屏障语义仍需硬件验收。
- 0005：扩展契约独立。星光 profile 未确认，不通过 USB 桥 VID/PID 推断机型。
- 0006：依赖优先复用。USB、Skia、SQLite 已接入；Svg.Skia/Clipper2 已接入；Cyotek/LaserGRBL 抽取记录位于第三方 manifest。

- [0007：0.3 执行可靠性与完整演示](0007-v03-execution-and-demo.md)。

- [0008：统一应用内交互与有界图像处理](0008-image-processing-and-in-app-flows.md)。

- [0009：桌面 LaserGRBL 算法复用与集中参数页](0009-desktop-lasergrbl-cam-and-single-page-forms.md)。

这些记录描述当前已实现边界，详细目标见完整方案。
