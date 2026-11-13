# 开源优先复用矩阵

| 能力 | 项目 | 当前决定 | 本轮证据 |
|---|---|---|---|
| USB | anotherlab/UsbSerialForAndroid 2.0.1 | 已接入真实读写、端口选择与系统授权 | 实际 NuGet restore 与 Android 编译检查 |
| 绘图 | SkiaSharp.Views 3.119.2 | 已引入，SKCanvasView 预览 | Android 依赖锁与编译检查 |
| 存储 | Microsoft.Data.Sqlite 10.0.14 | 已接入 | 临时 SQLite 重启恢复测试 |
| 测试 | xUnit 2.9.3 | 已接入 | 核心测试实际执行 |
| 光栅/CAM | LaserGRBL | 已抽取 Cyotek 灰度抖动适配，保留版权；其它算法继续审查 | 1f9337b3 的 FloydSteinberg/ErrorDiffusion，改为灰度缓冲并修复首行首列边界 |
| SVG 预览 | Svg.Skia | 已接入 Svg.Skia 4.9.1 | SVG 光栅转换；依赖锁与 Android CI |
| 几何 | Clipper2 C# | 已接入 Clipper2 2.0.0 | 轮廓简化使用公开 API，维护交接已记录 |
| GRBL 能力 | ioSender | 纯逻辑参考 | 桌面依赖需抽离 |
| 移动流程 | LaserGRBL Mobile bb9ed4b | 界面和测试参考 | 自建发送器验证状态语义 |
| 流控 | grblcontroller/GRBL 文档 | 字符计数阶段参考 | 0.3 加入按 [OPT] 报告的 RX 字符窗口，未知容量逐行；回归覆盖在途字节与屏障 |

发送器属于本产品的任务状态集成层，采用 GRBL 公开协议独立实现。采用独立实现的原因：需要统一的显式完成屏障、控制器确认暂停、无重放超时和服务所有权；不复制上游 UI 生命周期及 Windows 耦合。

禁止未评估就重写成熟 USB、图像渲染或几何库。新增替代库时提交接入结果与 ADR。

图像导入已整理为独立的 LaserMobile.Imaging.Skia 项目，核心 CI 使用真实 Skia Linux native 运行 PNG/SVG 与 Clipper2 转换用例。Android 继续使用对应 Android native assets。

0.4 沿用 Android 原生 Views、SkiaSharp、Svg.Skia、Clipper2 与现有 GRBL 解析器。文件类型检测通过 Skia 编码识别与完整解码、现有 SVG 验证和 G-code 预检完成。未新增运行时依赖。布局与文件选择规范来源及对应决策见 [MOBILE_UI.md](MOBILE_UI.md)。

0.5 继续复用 SkiaSharp 的 SKCodec 缩小解码和 Canvas 矩阵采样，使用 Clipper2 简化提取路径。未新增运行时依赖。已评估 OpenCvSharp Android 文档及 NuGet：上游 Android 运行包仍采用 CI 本地产物，公开 flat-container 的 opencvsharp5.runtime.android 在调查时返回 404。采用可替换的有界 C# Canny/路径适配层，避免引入自建 NDK 发布链；调查链接和决定见 [ADR 0008](adr/0008-image-processing-and-in-app-flows.md)。
