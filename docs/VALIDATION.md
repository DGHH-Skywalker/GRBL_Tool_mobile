# 验证记录

执行环境：Linux x64，.NET SDK 10.0.403，Android workload 36.1.2，JDK 21。

本文件在提交前补齐实际命令结果。当前测试覆盖拆粘包、响应溢出、控制字节、输入方言、提前 ACK、过期 Idle、独立屏障、丢应答不重发、报警/重启、Hold 确认、重复 start、未知硬件和 SQLite 重启恢复。

以下按版本记录实际结果。0.3 演示通过与 USB 相同的 DeviceSession 异步协议链路运行，模拟器使用虚拟坐标和加速时钟。真实硬件验收另行记录。

## 2026-11-08 实际结果

- `dotnet restore GRBL.Tool.Mobile.Core.slnx --locked-mode`：通过。
- `dotnet test GRBL.Tool.Mobile.Core.slnx -c Release --no-restore`：18 项通过，0 失败。
- `dotnet restore GRBL.Tool.Mobile.slnx --locked-mode`：通过。
- `dotnet run --project tools/LaserMobile.GrblSimulator -c Release --no-restore`：Completed，发送和确认均为 6 行，控制器 Idle。
- Android Release APK 构建通过：0 errors，1 个本地 JDK 版本格式警告。
- SQLite 依赖升级到 10.0.14 后，恢复时的高危间接依赖报告已消除。
- 本地 JDK 为 Microsoft OpenJDK 21.0.13.0.2，Android 构建工具对其多段版本号发出 XA0033 解析警告；编译使用实际 JDK。

本轮没有连接真实 USB 控制板，也没有执行电机或激光动作。

## 0.2 软件功能与 CI

- 第一轮扩展运行包含 40 项测试，发现暂停确认条件过宽并完成修复。
- 提交 3c2ef763 的 GitHub Actions core 与 android 全通过：40 项核心测试及 Release APK。
- 后续加入图像编辑、持久化启动去重、设备预设及会话端到端测试；结果按对应 Actions 记录核验。
- 本轮按用户要求使用 GitHub Actions 编译与测试，本地只进行源码编辑、依赖恢复和静态检查。
- 硬件接口已实现；尚无真实控制板、实际激光输出或手机后台验收记录。
- 待验证：不同芯片的连接复位行为、暂停的实际停光、复位后位置、标准 GRBL 屏障在具体 fork 的行为、长任务吞吐。

- 提交 85ea3b3 的 core：42 项测试通过，Android Release 构建通过，包含真实会话端到端模拟和持久化启动事务。
- 后续补充暗色主题、图像 EXIF 方向与任务元数据校验，继续由 Actions 验证。

- 提交 197ba7d：GitHub Actions 全通过；48 项测试，包含真实 Skia Linux PNG/SVG 转换和 Clipper2 轮廓；Android Release APK 已生成。
- 最终收尾增加标准 G54 头部兼容与显式工作模式/取消点动入口，构建和测试仍由 Actions 完成。

## 0.3 验证范围（2026-11-13）

所有编译与测试均交由 GitHub Actions。本地进行源码编辑与静态 diff 检查。新增用例覆盖实际起点、首个运动模态、旧转换失效、字符窗口容量、暂停与继续、模拟设备端到端完成、复位重新就绪和连接故障历史。

Actions 同时生成签名 APK 和 SHA256，Android API 35 模拟器检查安装与完整交互流程，截图及日志作为独立 artifact。最终交付的功能提交：`2029bb38b87b05fa051fca51717aaae074fd81f2`。

[完整通过的 GitHub Actions 运行](https://github.com/DGHH-Skywalker/GRBL_Tool_mobile/actions/runs/43389716471)。

- 核心测试：59 项通过，0 失败、0 跳过；包含真实 Skia PNG/SVG 转换及 SQLite 会话工作流。
- CLI 模拟器：Completed，6 行已发送/已确认，控制器 Idle。
- Android Release：构建成功，签名校验成功。APK 包含 arm64-v8a 与 x86_64。
- Android API 35 / Pixel 2 模拟器：安装、完整演示、预览、开始、暂停、继续、按 Home 后后台完成、返回应用、执行历史、修改参数后禁用旧任务，全部通过。最终这一轮未遇到系统桌面 ANR。
- [签名 APK 与校验文件](https://github.com/DGHH-Skywalker/GRBL_Tool_mobile/actions/runs/43389716471/artifacts/14020267490)。
- [模拟器截图、UI XML、PASS 结果与 logcat](https://github.com/DGHH-Skywalker/GRBL_Tool_mobile/actions/runs/43389716471/artifacts/14019873821)。

APK SHA256：

```text
e80db22c46f7093867752ac2556ac081e0adb17a27f7d79f07a55d6bcae135e9
```

已下载 APK 并独立核对该校验值，已查看预览与后台完成页面的模拟器截图。SDK 与 workload set 均固定为 10.0.403，locked restore 保持启用。此前 CI 暴露的复位后误保持、Android 类型冲突、浮动 workload 依赖锁不一致与模拟器系统桌面启动问题均已处理，并由最终完整流水线验证。

实机验收范围仍包括 USB 芯片插拔/授权、具体控制板的屏障语义、实际运动、锁屏与长时耗电、现场激光加工；本轮未连接实体设备。
