# GRBL Tool Mobile

C# / .NET for Android 原生激光雕刻控制工具。支持 USB OTG 连接 GRBL 1.1，独立前台执行、G-code 工作流、位图与 SVG 转换。

## 0.7 OpenCV 保真轮廓与全局交互优化

[APK 镜像直链](https://gh-proxy.com/https://github.com/DGHH-Skywalker/GRBL_Tool_mobile/releases/download/v0.7.0-demo/GRBL-Tool-Mobile-0.7.0.apk) · [下载与校验说明](docs/DOWNLOADS.md) · [120 项测试与 Android 交互验收](https://github.com/DGHH-Skywalker/GRBL_Tool_mobile/actions/runs/43551159299)

从上面的 APK 镜像直链下载并安装。Android 10 及以上、ARM64 或 x86_64，打开设备页点击“体验完整演示”。[完整体验与安装说明](docs/DEMO.md)。

照片轮廓采用 OpenCV 的局部对比增强、去噪、Canny 和连通域过滤；二维码与图文通过 Clipper2 精确像素区域并集保留直角、孔洞和单像素模块。提供智能识别、照片细节、图文保真和局部阈值四种策略，继续复用 LaserGRBL/Cyotek 灰度、阈值和八套抖动。

设备参数和维护集中一页，详情按实际宽高排版，历史同屏可容纳时隐藏翻页。全局加入 160 ms 原生过渡动画和按钮水波纹，历史加载移到后台。USB 诊断展示全部原始设备、VID/PID、接口及驱动匹配结果，支持兼容未知设备的驱动映射和可配置 DTR/RTS。

[全局开源复用审查](docs/OPEN_SOURCE_AUDIT.md) · [轮廓与交互设计](docs/adr/0010-opencv-fidelity-and-adaptive-layout.md) · [移动界面规范](docs/MOBILE_UI.md)。保留尺寸/像素上限、等比例缩小、底部导航和文件内容白名单。

保留实际工作坐标预检、参数修改失效、人工就绪、字符流控、进度节流、缓存预览、任务期唤醒锁与通知暂停。完整演示复用 USB 会话、任务和历史链路。

## 功能

- 原始 USB 枚举、VID/PID/接口诊断、多端口选择、自定义兼容驱动映射、DTR/RTS、系统授权和串口读写。
- 服务持有串口与状态；页面退出后保持执行，进程重启保守恢复历史。
- GRBL 身份、$30/$32/$22、G54/G17/G94 模态读取。
- 有限点动、工件原点、确认后的硬件回零和报警解锁。
- G-code 完整预检、毫米/英寸、绝对/相对 XY、模态、速度/功率/区域检查。
- 任务准备、哈希校验、持久化启动去重、暂停/继续、保持加复位取消、完成屏障。
- PNG/JPG 等位图的缩放、EXIF 方向校正、比例裁剪、90° 旋转、灰度、Gamma、阈值、反色、八套桌面抖动方法和双向扫描。
- 可配置最大加工宽高与处理像素，超限等比例缩小，摘要显示实际尺寸；OpenCV 分场景轮廓及 Clipper2 精确图文边界。
- SVG 安全导入、光栅转换、基础图形与路径轮廓；使用 Svg.Skia、SkiaSharp 和 Clipper2。
- 可缩放/平移预览、范围预跑、估时、任务库、执行历史、G-code 与日志导出。
- 声明式设备 JSON 导入；独立星光扩展保留待确认的硬件信息。

## 使用流程

1. 设备页扫描并选择端口，授权连接。
2. 等待状态，再点击“读取配置”。
3. 在设备“参数”分类集中设置工作区域、进给上限和 $30 功率范围。
4. 在“定位”分类设置工件零点，等待新 WPos 后点击“确认就绪”。
5. 作品页导入文件；位图/SVG 填写参数后生成并保存。
6. 查看路径，按需要先进行无光范围预跑。
7. 加工页确认设备、坐标和现场条件后开始。
8. 可从通知返回应用；历史页管理文件、记录和导出。

G-code 支持 G0/G1、G20/G21、G90/G91、G17/G94/G54、M3/M4/M5、X/Y/F/S。移动前必须声明单位、距离模式和 M5 初始化。暂未支持的圆弧/宏/配置指令整体拒绝。设备报告 RX 容量时启用字符窗口，未知容量使用逐行流控。

SVG 轮廓支持 path、rect、circle、ellipse、line，曲线通过 Skia 展平并使用 Clipper2 简化。带复杂样式或变换的 SVG 使用光栅模式，或在导入前展开路径。SVG 外部资源、脚本和文本需先转换为普通图形。

## 构建与测试

构建、测试和 APK 生成统一由 [GitHub Actions](https://github.com/DGHH-Skywalker/GRBL_Tool_mobile/actions) 执行。push 与手动 workflow_dispatch 均触发。运行成功后在 Artifacts 下载 android-apk。

开发者可使用 global.json 指定的 SDK：

```sh
dotnet restore GRBL.Tool.Mobile.Core.slnx --locked-mode
dotnet test GRBL.Tool.Mobile.Core.slnx -c Release --no-restore
dotnet run --project tools/LaserMobile.GrblSimulator -c Release
```

Android SDK 平台 36、build-tools 36.0.0、JDK 21。先从 Actions 的 opencv-native-* 产物恢复两个 ABI 到 artifacts/opencv-native，或用 NDK 28.2.13676358 在 Linux 运行 tools/native-opencv/build.sh；随后：

```sh
dotnet workload restore GRBL.Tool.Mobile.slnx
dotnet restore GRBL.Tool.Mobile.slnx --locked-mode
dotnet build src/LaserMobile.Android -c Release -p:AndroidSdkDirectory=/path/to/android-sdk -p:JavaSdkDirectory=/path/to/jdk
```

## 工程资料

- [完整方案](docs/ANDROID_IMPLEMENTATION_PLAN.md)
- [开源复用矩阵](docs/reuse-matrix.md)
- [全局开源替换审查](docs/OPEN_SOURCE_AUDIT.md)
- [路线图与待验收项](docs/ROADMAP.md)
- [验证记录](docs/VALIDATION.md)
- [架构决定](docs/adr/README.md)
- [贡献规范](CONTRIBUTING.md)
- [第三方声明](THIRD_PARTY_NOTICES.md)

源码 GPL-3.0-or-later。演示签名、覆盖安装与正式渠道发布步骤见 [演示版说明](docs/DEMO.md)。
