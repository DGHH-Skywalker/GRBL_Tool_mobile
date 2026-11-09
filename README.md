# GRBL Tool Mobile

C# / .NET for Android 原生激光雕刻控制工具。支持 USB OTG 连接 GRBL 1.1，独立前台执行、G-code 工作流、位图与 SVG 转换。

## 0.2 功能

- USB 多端口扫描、系统权限、串口连接、故障检测和主动断开。
- 服务持有串口与状态；页面退出后保持执行，进程重启保守恢复历史。
- GRBL 身份、$30/$32/$22、G54/G17/G94 模态读取。
- 有限点动、工件原点、确认后的硬件回零和报警解锁。
- G-code 完整预检、毫米/英寸、绝对/相对 XY、模态、速度/功率/区域检查。
- 任务准备、哈希校验、持久化启动去重、暂停/继续、保持加复位取消、完成屏障。
- PNG/JPG 等位图的缩放、EXIF 方向校正、比例裁剪、90° 旋转、灰度、Gamma、阈值、反色、Floyd–Steinberg 抖动和双向扫描。
- SVG 安全导入、光栅转换、基础图形与路径轮廓；使用 Svg.Skia、SkiaSharp 和 Clipper2。
- 可缩放/平移预览、范围预跑、估时、任务库、执行历史、G-code 与日志导出。
- 声明式设备 JSON 导入；独立星光扩展保留待确认的硬件信息。

## 使用流程

1. 设备页扫描并选择端口，授权连接。
2. 等待状态，再点击“读取固件与配置”。
3. 设置与实机一致的工作区域、进给上限和 $30 功率范围。
4. 定位并设置 G54 工件零点；当前 WPos 应位于工作区域内。
5. 作品页导入文件；位图/SVG 填写参数后生成并保存。
6. 查看路径，按需要先进行无光范围预跑。
7. 加工页确认设备、坐标和现场条件后开始。
8. 可从通知返回应用；历史页管理文件、记录和导出。

G-code 支持 G0/G1、G20/G21、G90/G91、G17/G94、M3/M4/M5、X/Y/F/S。移动前必须声明单位、距离模式和 M5 初始化。暂未支持的圆弧/宏/配置指令整体拒绝。长任务当前采用逐行应答流控。

SVG 轮廓支持 path、rect、circle、ellipse、line，曲线通过 Skia 展平并使用 Clipper2 简化。带复杂样式或变换的 SVG 使用光栅模式，或在导入前展开路径。SVG 外部资源、脚本和文本需先转换为普通图形。

## 构建与测试

构建、测试和 APK 生成统一由 [GitHub Actions](https://github.com/DGHH-Skywalker/GRBL_Tool_mobile/actions) 执行。push 与手动 workflow_dispatch 均触发。运行成功后在 Artifacts 下载 android-apk。

开发者可使用 global.json 指定的 SDK：

```sh
dotnet restore GRBL.Tool.Mobile.Core.slnx --locked-mode
dotnet test GRBL.Tool.Mobile.Core.slnx -c Release --no-restore
dotnet run --project tools/LaserMobile.GrblSimulator -c Release
```

Android SDK 平台 36、build-tools 36.0.0、JDK 21：

```sh
dotnet workload restore GRBL.Tool.Mobile.slnx
dotnet restore GRBL.Tool.Mobile.slnx --locked-mode
dotnet build src/LaserMobile.Android -c Release -p:AndroidSdkDirectory=/path/to/android-sdk -p:JavaSdkDirectory=/path/to/jdk
```

## 工程资料

- [完整方案](docs/ANDROID_IMPLEMENTATION_PLAN.md)
- [开源复用矩阵](docs/reuse-matrix.md)
- [路线图与待验收项](docs/ROADMAP.md)
- [验证记录](docs/VALIDATION.md)
- [架构决定](docs/adr/README.md)
- [贡献规范](CONTRIBUTING.md)
- [第三方声明](THIRD_PARTY_NOTICES.md)

源码 GPL-3.0-or-later。当前 APK 为工程验证包，正式渠道签名和硬件验收按路线图执行。
