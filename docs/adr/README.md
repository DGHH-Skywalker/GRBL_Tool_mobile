# 架构决定

- 0001：Android-only，C# 与 .NET for Android。复用 C# 生态，直接集成 Android 服务和 USB。
- 0002：原生 Views/XML。当前页面完成设备发现与集成验证；Fragments/AndroidX 在页面扩展阶段加入。
- 0003：服务持有执行生命周期。初版实现有限模拟任务的独立前台服务；真实设备服务待 USB 授权后采用 connectedDevice 类型。
- 0004：逐行流控、独立 G4 P0 屏障、新 Idle。仅模拟器 profile 验证屏障，真实 profile 默认禁用。
- 0005：扩展契约独立。星光 profile 未确认，不通过 USB 桥 VID/PID 推断机型。
- 0006：依赖优先复用。USB、Skia、SQLite 已接入；CAM 引入前记录源文件、提交和许可。

这些记录描述当前已实现边界，详细目标见完整方案。
