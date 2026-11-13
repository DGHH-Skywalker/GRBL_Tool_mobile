# 0008：统一应用内交互与有界图像处理

## 交互

移除应用自己的 AlertDialog 与 Spinner。格式和算法选择、操作确认、日志详情统一进入应用内内容面板，使用同一背景、字号、按钮、返回方式与底部导航。打开面板时隐藏主页面内容，返回时恢复原页面。重要操作保留人工确认及原有世代、坐标和执行 ID 校验。

普通操作错误使用顶部就地提示。单页内容不显示上一页/下一页；长日志才按内容分页。系统文件选择器、USB 授权和通知授权保持系统所有。

## 尺寸

分别配置最大加工宽高（mm）与最大处理宽高（px）。按裁剪、EXIF 方向和旋转后的比例计算，只使用同一比例缩小；加工范围同时受用户工作区域限制。像素按两个边长及 100 万采样预算统一缩小，最多产生一像素取整差。源图不变，处理后的尺寸与参数写入任务快照，可修改上限重新生成。

源图解码保留独立的 4800 万像素预算，压缩文件仍受 16 MB 限制。使用 Skia 的 GetScaledDimensions 选择原生缩小解码，再等比例采样。解码后的大位图在边缘处理前释放。渲染直接组合 EXIF/裁剪/旋转矩阵，避免额外的全尺寸旋转副本。

## 实验性轮廓

选择“实验：轮廓提取”显式启用。流程为 Gamma/反色、5×5 可分离高斯去噪、Sobel 梯度、非极大值抑制、自适应双阈值连通、像素图路径跟踪及 Clipper2 几何简化。处理梯度结构，直接阈值无法保留的浅色边缘也可进入路径。保留灰度、黑白阈值、抖动和 SVG 路径模式。

轮廓灵敏度、最短路径长度可配置。邻接图每条连接只访问一次，去掉正交拐角的对角捷径；先跟踪端点/分叉，再跟踪闭环。每条路径先 M5、G0 到起点、M4/G1 雕刻，再 M5。限制 5000 条路径、10 万精简点，空结果明确提示。所有最终 G-code 继续经过现有预检和任务执行层。

## 优先复用评估

- SkiaSharp 3.119.2：复用完整解码、缩放、采样、EXIF 矩阵与 SVG 渲染。
- Clipper2 2.0.0：复用路径简化。继续沿用现有 GRBL 生成预检与 SHA256/任务链路。
- 查阅 OpenCvSharp 官方 Android preview 文档，其声明支持 net10.0-android、core/imgproc/imgcodecs。2026-11-13 实际查询 NuGet flat-container：OpenCvSharp5 托管包可用，OpenCvSharp5.runtime.android 及 OpenCvSharp4.runtime.android 均返回 404。文档中的 Android CI 使用本地打包产物。为避免引入自建 NDK/原生包供应链，本轮有限 Canny/图跟踪适配采用独立、可测试的托管实现，继续复用 Skia 与 Clipper2；未复制上游源代码，未增加依赖。

参考：

- https://learn.microsoft.com/en-us/dotnet/api/skiasharp.skcodec.getscaleddimensions
- https://shimat.github.io/opencvsharp/articles/getting-started/android.html
- https://docs.opencv.org/4.13.0/da/d22/tutorial_py_canny.html
- https://www.angusj.com/clipper2/Docs/Units/Clipper/Functions/SimplifyPath.htm

## 验证

GitHub Actions 验证单比例尺寸计算、像素预算、裁剪旋转、超过旧 1200 万像素限制的 JPEG、低对比结构与直接阈值的区别、轮廓路径之间关光、无边缘/取消/参数错误及算法切换。Android 模拟器验证应用内确认与返回、就地提示、无原生应用弹窗/下拉选择、限制调整、轮廓预览和切回光栅，继续检查小屏、大字体和键盘。
