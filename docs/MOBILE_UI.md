# 0.4 单屏移动界面与导入规范

## 设计资料与应用决策

2026-11-13 查阅以下 Android 官方资料，并结合本项目的单屏操作要求实现。

- [布局与导航模式](https://developer.android.com/design/ui/mobile/guides/layout-and-content/layout-and-nav-patterns)：手机采用底部导航，四个同级目的地；子功能用页内选项卡。
- [触控目标与无障碍](https://developer.android.com/guide/topics/ui/accessibility/apps)：交互目标至少 48dp，显示文字与可点击区域独立处理。
- [边到边窗口与系统栏](https://developer.android.com/develop/ui/views/layout/edge-to-edge)：按照系统栏、刘海及输入法实际 inset 保留操作空间。
- [系统文档选择器](https://developer.android.com/training/data-storage/shared/documents-files)及[多 MIME 类型](https://developer.android.com/guide/components/intents-common)：使用 SAF，并通过 EXTRA_MIME_TYPES 提供明确的格式提示。

本项目将“主流程无需上下滚动”作为界面验收要求。页面在 Android 原生 Views 上实现，继续复用 SkiaSharp 预览及现有 USB、协议、CAM 与存储模块。

## 页面结构

| 底部目的地 | 页内功能 | 固定内容 |
|---|---|---|
| 设备 | 连接 | 固件/坐标摘要、串口、扫描连接、读取配置、确认就绪、演示、断开 |
| 设备 | 定位 | 坐标、步长、X/Y 点动、工件零点与点动取消 |
| 设备 | 参数 | 分步填写工作区域、运行上限、串口与 JSON 预设 |
| 设备 | 维护 | 分步提供回零、解锁、工作模式、复位、故障演示和日志 |
| 作品 | 预览 | 文件摘要、可缩放路径、导入、适合屏幕、生成、去加工 |
| 作品 | 加工参数 | 尺寸与精度、速度与功率、图像算法分步填写 |
| 作品 | 图像调整 | 裁剪起点、裁剪终点、旋转与转换分步设置 |
| 加工 | 单屏控制 | 任务摘要、自适应缩略路径、进度、确认、开始/预跑、暂停/继续、取消 |
| 历史 | 任务库 / 执行记录 | 根据屏幕和字体每页 2–3 项，上/下一页切换 |

四个主页面、分步表单与详情日志均采用固定布局或显式分页。主要内容不使用 ScrollView。系统文件选择器负责用户目录浏览。

底部导航固定在屏幕底部，状态始终位于顶部。软键盘显示时收起底部与次级导航，保留当前表单标题、字段和上/下一步操作。预览按剩余空间伸缩。当前版本专注手机竖屏工作流。

详情与日志按内容分成短页，通过按钮翻页；开始确认对话框保留任务名、实际起点、路径范围、工作区、功率上限及哈希摘要。

## 文件导入白名单

| 类型 | 文件扩展名 | 文件大小上限 | 内容检查 |
|---|---|---|---|
| 位图 | .png / .jpg / .jpeg / .webp / .bmp | 16 MB | Skia 识别的真实编码匹配扩展名；完整解码；最多 1200 万像素；静态图片 |
| 矢量 | .svg | 2 MB | SVG 根元素；复用现有 XML/SVG 限制，拒绝 DTD、脚本、外部资源和未支持的动态结构 |
| 加工文件 | .nc / .gcode / .gc / .ngc / .tap / .cnc | 4 MB | ASCII 或带 UTF-8 BOM；完整 GRBL 方言解析，拒绝不支持的指令与二进制内容 |
| 设备配置 | .json | 64 KiB | JSON 对象与现有 DevicePreset schemaVersion、固件、字段和范围校验 |

选择器先选择“位图”“SVG”或“G-code”类别，再设置该类别 MIME 白名单。JSON 仅从设备参数页导入。

Domain/ImportFormats 统一维护扩展名、MIME 和大小。系统 MIME 是选择提示；导入事务以扩展名白名单及内容检查结果为权威。空文件、伪装后缀、类型不符、缺失文件名、无关扩展名以及超限文件在替换当前作品前拒绝。文件提供者未返回大小时，读取过程中依然执行同一上限。

读取与内容检查在后台运行，检查失败保留原作品和准备任务。通过后再提交文件状态；图像参数或文件变化仍使已生成任务失效。

## 实现位置

- MainActivity.Layout.cs：固定布局、底部导航、页内选项卡、分步表单、分页记录及 insets。
- MainActivity.Files.cs：SAF 选择器、类别状态恢复、有界读取与导入提交。
- MainActivity.cs：已有会话操作、任务准备、预览与状态绑定。
- Domain/ImportFormats.cs：支持格式元数据。
- Imaging.Skia/ImportValidator.cs：复用 Skia、SVG 与 GRBL 的内容验证。

## 验证要求

所有构建与测试在 GitHub Actions 执行。核心测试覆盖白名单、MIME 错配、伪装和截断图片、G-code 二进制与方言错误、大小预算、SVG 脚本及配置结构。

模拟器验收使用正常手机、360×640 等效紧凑窗口及 1.3 倍字体，检查主页面和表单没有滚动容器、控件保持可见且有足够触控高度、软键盘不遮挡当前操作。真实系统选择器测试伪装 PNG 拒绝、原任务保留，以及有效 PNG 导入生成。保留原有开始、暂停、继续、后台完成、历史及参数失效检查。

实际结果、对应提交和截图以 VALIDATION.md 的最终记录为准。
