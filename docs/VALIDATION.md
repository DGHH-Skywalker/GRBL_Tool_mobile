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

## 镜像分发验证（2026-11-13）

新增原始已验收 APK 分发工作流，固定源运行、源提交及 SHA256，发布免登录的 APK 文件供镜像转发。分发运行 `43396404186` 成功。主镜像及备用镜像均已完整下载 56,527,391 字节并复核 SHA256。镜像地址见 [DOWNLOADS.md](DOWNLOADS.md)。本次分发保持原始安装包字节及签名。

## 0.4 单屏界面与导入验证（2026-11-13）

功能及模拟器脚本提交：`c20b0b33ac8369ce4b86acc3c739167f28fde32a`。

[完整通过的 Actions 运行](https://github.com/DGHH-Skywalker/GRBL_Tool_mobile/actions/runs/43401528060)。

- 74 项核心测试通过，包含 Skia 原生解码、伪装后缀、截断图片、MIME 类别、G-code 文本与指令内容、SVG 脚本及配置结构检查。
- Android Release 构建与签名校验通过。
- Android API 35 模拟器全部通过：正常尺寸下的固定屏幕、底部导航、暂停继续、后台完成、分页历史；系统文件选择器隐藏无关 PDF、选择伪装 PNG 后拒绝并保留原任务、有效 PNG 导入及生成；参数变化阻止启动；360×640 等效屏幕、1.3 字体、所有主页面与参数分步控件可见；软键盘与输入框、下一步保持可见。
- 验收脚本未使用寻找应用按钮的滑屏手势，应用主页面没有 ScrollView。
- 已人工查看正常屏幕加工控制、小屏加工页及键盘表单截图。

[APK 构建产物](https://github.com/DGHH-Skywalker/GRBL_Tool_mobile/actions/runs/43401528060/artifacts/14025037637)；[界面截图、UI XML、PASS 结果及日志](https://github.com/DGHH-Skywalker/GRBL_Tool_mobile/actions/runs/43401528060/artifacts/14026235734)。

APK SHA256：

```text
0b7e0b922674bca3ae9d76e1f82986977b6713ad67b745685e2d3e255ec3dfed
```

本轮全部构建和测试由 GitHub Actions 执行，本地进行源码修改和产物检查。控制器协议、实体设备验收范围保持原有记录。

0.4 分发运行 `43403321027` 成功。镜像主/备用入口均已完整下载 56,551,902 字节并复核相同 SHA256，下载地址见 DOWNLOADS.md。发行标签位于分发修订，流程比较该修订与已验收构建，确认应用源码保持一致。APK 签名证书 SHA256 为 `5d184a50ed70cdabc294ce5ddf9fc29308d4550de15a74891798490a52eee999`，与 0.3 演示签名相同。

## 0.5 统一交互、尺寸限制与实验性轮廓（2026-11-14）

最终功能提交：`ecd7ed9a6f4c2705744d6c05a1d9d3ea27c85125`。

[完整通过的 Actions 运行](https://github.com/DGHH-Skywalker/GRBL_Tool_mobile/actions/runs/43530400589)。

- 核心测试 88 项全部通过，0 失败、0 跳过。覆盖像素/加工区域双上限、统一比例、裁剪旋转、超过旧 1200 万像素限制的 JPEG 缩小解码、SVG 尺寸及水平线段、浅色结构边缘、平坦图像、灵敏度、取消、路径间 M5 及算法切换。
- CLI 模拟器 Completed，6 行已发送/已确认，控制器 Idle。
- Android Release 构建和签名校验通过，已独立下载并核对 APK SHA256。签名证书继续为 `5d184a50ed70cdabc294ce5ddf9fc29308d4550de15a74891798490a52eee999`。
- Android API 35 模拟器完整通过：应用内操作确认与返回、顶部错误提示、应用自己的窗口没有 AlertDialog/Spinner、格式与内容检查、轮廓选择与生成、切回阈值光栅、后台完成、暂停继续、历史、参数变化失效，以及 360×640 等效窗口、1.3 字体和软键盘。
- 缩放样例：1600×800 源图，配置 128×128 px 和 10×10 mm 上限，生成 128×64 px / 10×5 mm，轮廓任务 34 行，包含外部与内部轮廓。
- 已查看最终轮廓预览及紧凑窗口键盘截图；此前也查看同一界面实现的顶部提示、应用内确认和算法选择截图。

[原始签名 APK](https://github.com/DGHH-Skywalker/GRBL_Tool_mobile/actions/runs/43530400589/artifacts/14087686041)；[截图、UI XML、PASS 结果及日志](https://github.com/DGHH-Skywalker/GRBL_Tool_mobile/actions/runs/43530400589/artifacts/14086783808)。

APK：56,576,478 字节；SHA256：

```text
5b14365c88162b9640b9964fb24db0b0bd85d714cf2b16a52acc389a082f0003
```

全部构建与测试由 GitHub Actions 执行，本地进行源码编辑、静态检查和产物核验。本轮使用模拟器，实体设备验收范围沿用前述记录。

0.5 分发运行 `43531648213` 成功，标签 `v0.5.0-demo` 已发布。2026-11-14 从主镜像及备用镜像各完整下载 56,576,478 字节，SHA256 均与上述原始签名 APK 一致。当前执行环境测得主镜像 2.81 秒、备用 7.30 秒，作为本次下载记录。两个下载地址见 DOWNLOADS.md。

## 0.6 桌面算法复用与集中设置页（2026-11-14）

最终功能提交：`f7ab16ba56fb6d81689a782266046bc4409c90e6`。

[完整通过的 Actions 运行](https://github.com/DGHH-Skywalker/GRBL_Tool_mobile/actions/runs/43535391773)。

- 核心测试 105 项全部通过，0 失败、0 跳过。新增八套上游抖动及首行扩散黄金数组、OpticalCorrect 灰度/透明/整数功率、固定阈值、白区跳过及关光快移、CsPotrace 外环/孔洞/二维码三定位图案九条封闭边界/孤立噪点、低对比自动阈值、并发隔离和取消。
- 真实 Skia 像素测试确认默认雕刻预览的跨轮廓空移位置保持背景色，显式打开后显示灰色虚线。
- CLI 模拟器 Completed，6 行发送/确认，控制器 Idle。
- Android Release 构建与签名通过。签名证书继续为 `5d184a50ed70cdabc294ce5ddf9fc29308d4550de15a74891798490a52eee999`，支持覆盖安装。
- Android API 35 完整交互通过：12 个加工数字字段同屏、裁剪四边/旋转/还原同页、两页没有上一步/下一步；桌面 CsPotrace 选择/生成、固定阈值光栅、Atkinson 切换、默认隐藏空移及显示开关；保持原有导入白名单、伪装文件拒绝、确认返回、暂停继续、后台完成、历史与参数失效。
- 360×640 等效窗口、1.3 字体、各主页面、集中表单和软键盘均通过可见性和触控高度检查；没有滑屏寻找控件。
- 1600×800 浅色内外矩形样图，128×64 px、10×5 mm，CsPotrace 生成 18 行封闭内外路径；已查看最终参数网格、无空移细线的预览、紧凑键盘及裁剪页截图。

[原始签名 APK](https://github.com/DGHH-Skywalker/GRBL_Tool_mobile/actions/runs/43535391773/artifacts/14088884335)；[截图、UI XML、PASS 结果和日志](https://github.com/DGHH-Skywalker/GRBL_Tool_mobile/actions/runs/43535391773/artifacts/14089911424)。

APK：56,605,150 字节，已独立下载核对 SHA256：

```text
a2abf280913f17d67df13d0391fde93299b4eb295934bc11c07fca8fc7fffd0b
```

全部构建与测试交由 GitHub Actions。桌面源码许可证、提交、路径及适配差异见 third-party/manifest.json 和 ADR 0009。本轮使用模拟器和合成图案，实体设备验收范围沿用前述记录。

0.6 分发工作流通过手动触发运行 `43536905828` 成功，发布标签 `v0.6.0-demo`。发布检查确认分发修订仅含文档及分发流程变化，原始已验收 APK 保持签名与字节。2026-11-14 从主镜像和备用镜像分别完整下载 56,605,150 字节，SHA256 均与上述原始 APK 一致。当前执行环境下载耗时分别为 2.18 秒和 7.41 秒。


## 0.7 保真轮廓、开源复用与全局交互（2026-11-14）

最终功能提交：`9719a5fdde6fb15f43e6a146def8d99c02a6b8c9`。完整 Actions 运行：`43551159299`。

- 核心测试 120 项通过，0 失败、0 跳过；覆盖完整密集二维码的 2/3/5 像素模块、轮廓填充还原 IoU≥0.995、ZXing 解码载荷一致、直角/阶梯/孔洞/单像素岛，以及渐变照明下的细笔画。NASA 公有领域真实照片固定 URL/SHA256，导出轮廓覆盖全部 26,434 个参考边缘像素；旧/新轮廓及指标作为测试产物保留。
- USB 描述符匹配、自定义驱动候选、非法配置、DTR/RTS 保持/顺序/错误传播及设备 JSON 回归通过。配置等待测试使用实际最终 ACK 与后续 Idle，替换固定延时；生产协议屏障保持原有语义。
- OpenCV 4.13 的两个 Android ABI 从固定源码在 Actions 构建，复用 Sdcb mini 补丁。原生 ELF 架构/16 KB LOAD 对齐、安装包中的实际库、签名和锁定恢复均通过。APK 包含新增依赖原始许可证。
- Android API 35 完整流程通过：原始 USB 诊断、DTR/RTS 选择；12 个算法同屏；照片和密集二维码实际导入、OpenCV 生成与预览；短/图像执行详情同屏且没有翻页；历史容量与列表；暂停/继续、后台完成、格式白名单和伪装文件拒绝；360×640、1.3 字体、键盘及设备集中设置。应用界面没有滑屏寻找控件。
- 页面动画开启进行验收，保留过渡视频与 gfxinfo 帧统计。SwiftShader 模拟器记录 595 帧，P50 34 ms / P95 57 ms，janky 366 帧（61.51%）；此为当前模拟器测量，真机帧率验收列入后续记录。照片、二维码、详情、设备参数及紧凑屏幕的截图和 UI XML 保留在模拟器产物。
- 已人工查看 Android 上的完整二维码直角边界、照片人脸/头发细节、图像执行详情同屏截图。原始签名 APK artifact `14096371950`；模拟器证据 `14096778574`；核心图像回归 `14096855781`。独立检查 APK 内全部 28 个 64 位 ELF，LOAD 对齐均达到 16 KB。

APK：65,387,887 字节；SHA256：`6292e3aff42057f08701394da577b1af3d86686781190164b3aa7b79381c7d25`。签名证书 SHA256 延续 `5d184a50ed70cdabc294ce5ddf9fc29308d4550de15a74891798490a52eee999`，支持覆盖安装。

所有构建和测试由 GitHub Actions 执行，本地进行源码编辑、依赖恢复和产物核验。自动化图像使用上述公共照片与完整测试二维码；会话附图未作为可读源文件进入工作区。USB 实物插拔、具体芯片通信/控制线电平、长时功耗与现场加工待真机验收。全局模块替换清单见 OPEN_SOURCE_AUDIT.md。

0.7 分发运行 `43552655463` 成功，标签 `v0.7.0-demo`。主镜像和备用镜像分别完整下载 65,387,887 字节，SHA256 均与原始签名 APK 一致；当前执行环境耗时分别为 3.72 秒与 12.34 秒。分发修订 `bdb7283cf53ea5b30216f174370062a72a39862b` 仅含文档和分发流程变化，由发布检查验证。
