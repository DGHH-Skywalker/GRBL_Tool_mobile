# 0009：桌面 LaserGRBL 算法复用与集中参数页

2026-11-14。用户要求加工参数和图像调整各自集中一页，直接复用桌面 LaserGRBL 方法，并修复轮廓预览的细线。

## 集中页面

加工参数使用三列紧凑网格，包含尺寸、行距、速度、功率、Gamma、阈值、毫米/像素上限、轮廓平滑和最短长度，算法与反色/自动阈值紧随其后。裁剪四边、旋转与还原集中在图像调整页。两页没有上一步/下一步。键盘打开时聚焦当前输入行，键盘收起后恢复全部字段；底部与二级导航沿用输入法避让。主流程保持固定单屏，设备参数页和长历史的原有分页另行保留。

算法选择采用双列同屏列表，沿用应用内面板和返回方式。

## 白色细线

原 ToolpathPreview 同时绘制了每次轮廓间的 Power=0 移动，灰色线在深色背景上呈白色细丝。默认仅绘制非 Rapid 且 Power>0 的雕刻路径。“显示空移”显式打开灰色虚线，该显示设置不使加工任务失效。预览移入硬件无关的 Imaging.Skia，使用真实 Skia 像素回归验证。

## 固定上游与改动

https://github.com/arkypita/LaserGRBL/tree/1f9337b3af27133f8b1696e41cc110f2af74d04f

- 直接导入 CsPotrace/CsPotrace.cs，保留轮廓分解、最优多边形、角点平滑与贝塞尔曲线优化。输入换成灰度缓冲，静态状态改为实例，加入取消及资源预算，去掉 GDI+。原始 GPL-2.0-or-later，使用 GPLv3 选项分发。
- 直接导入 Cyotek 的 ArgbColor、IErrorDiffusion、ErrorDiffusionDithering 和八套确定性误差扩散类：Floyd–Steinberg、Atkinson、Burkes、Jarvis、Stucki、Sierra2/3/Lite。矩阵和除数保持上游值；保留既有首行首列边界修复，启用构造参数检查。MIT 原始头与全文声明保留。
- ImageTransform.GrayScale/Threshold/DitherImage 和 GrblFile.GetColor/GetSegments/ExtractSegment/OptimizeLine2Line/OptimizePaths 的适用逻辑抽取到适配层。使用 OpticalCorrect 的 0.299/0.587/0.114 系数、阈值比较、整数 S 缩放、等功率连续段合并、空白跳过和最近邻排序。移除 Windows、全局设置及复杂指令解析依赖。
- 像素采样继续使用现有 Skia；曲线按毫米误差界展开为 G1，继续经过项目预检。最近邻距离按需计算，避免 5000 路径的平方内存矩阵。逐路径 M5/G0/M4/G1/M5 保持任务语义。
- 自动 Otsu 阈值、Gamma 查找表和移动端预算属于适配扩展，明确与上游导入核心区分。取消“自动阈值”即可使用配置的固定阈值。
- 旧 Canny 边缘实现保留作实验对比与回归；界面中的轮廓模式已使用 CsPotrace。

原始来源、许可证、文件清单及修改记录：third-party/manifest.json、third-party/notices/LaserGRBL-LICENSE.md、源文件头。未增加 NuGet 或原生运行时依赖。

## 验证

Actions 执行原生 Skia 与 CAM 测试：二维码定位图案的内外封闭孔洞、噪点、低对比阈值、并发状态隔离、取消、八套扩散方法、阈值和功率、白区跳过及真实预览像素。模拟器验证同屏全部字段、紧凑窗口/大字体/键盘、原有业务链路、轮廓与光栅切换及空移开关。
