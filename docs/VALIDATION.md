# 验证记录

执行环境：Linux x64，.NET SDK 10.0.403，Android workload 36.1.2，JDK 21。

本文件在提交前补齐实际命令结果。当前测试覆盖拆粘包、响应溢出、控制字节、输入方言、提前 ACK、过期 Idle、独立屏障、丢应答不重发、报警/重启、Hold 确认、重复 start、未知硬件和 SQLite 重启恢复。

模拟器是有限协议夹具，不计算真实加速度和光功率。当前 Android 服务运行模拟任务，USB 入口仅发现受支持驱动。真实 USB、手机后台行为及实际设备仍在后续验收范围。

## 2026-11-08 实际结果

- `dotnet restore GRBL.Tool.Mobile.Core.slnx --locked-mode`：通过。
- `dotnet test GRBL.Tool.Mobile.Core.slnx -c Release --no-restore`：18 项通过，0 失败。
- `dotnet restore GRBL.Tool.Mobile.slnx --locked-mode`：通过。
- `dotnet run --project tools/LaserMobile.GrblSimulator -c Release --no-restore`：Completed，发送和确认均为 6 行，控制器 Idle。
- Android Release APK 构建通过：0 errors，1 个本地 JDK 版本格式警告。
- SQLite 依赖升级到 10.0.14 后，恢复时的高危间接依赖报告已消除。
- 本地 JDK 为 Microsoft OpenJDK 21.0.13.0.2，Android 构建工具对其多段版本号发出 XA0033 解析警告；编译使用实际 JDK。

本轮没有连接真实 USB 控制板，也没有执行电机或激光动作。
