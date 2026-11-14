# 全局开源复用与替换审查 · 0.7

审查日期：2026-11-14。按实际源码、Android 兼容性、许可证、依赖大小及现有回归逐项决定；版本与来源记录在 third-party/manifest.json。

| 模块 | 当前采用的成熟实现 | 本轮处理及证据 |
|---|---|---|
| 设备 USB 枚举、授权 | Android UsbManager / 系统授权 | 原始设备列表独立于驱动匹配，展示 VID/PID、接口及端点；权限仍交给系统 |
| 串口芯片协议 | Anotherlab UsbSerialForAndroid 2.0.1 | 内置 ProbeTable、CDC/FTDI/CP21xx/CH34x/PL2303 驱动；新增自定义枚举映射，不实现厂商控制协议 |
| 未知 CDC | 复用 CdcAcmSerialDriver | 按 ACM 控制接口和 bulk 数据接口识别；HID、存储、CDC 网络设备不按串口处理 |
| DTR/RTS | 驱动库 SetDTR/SetRTS | 保持/低/高可配置；打开、设串口参数后一次应用；失败关闭端口并明确返回错误 |
| 图片解码、EXIF、缩放、采样 | SkiaSharp | 沿用完整内容验证与有界处理，避免自行实现编解码器 |
| 照片轮廓 | OpenCV / OpenCvSharp | 用成熟 CLAHE、双边/高斯、Canny 和连通域替换主轮廓的自研预处理与全局阈值链路 |
| 精细图文区域 | OpenCV 阈值 + Clipper2 布尔并集 | 替换曲线拟合和粗简化；整数像素区域精确合并，保留单像素模块和孔洞 |
| 光栅与抖动 | LaserGRBL / Cyotek | 沿用已经导入的八套扩散核、灰度、阈值和白区优化，来源和许可证保留 |
| SVG | Svg.Skia + Skia | 复用解析、渲染和基础路径处理；SVG 资源/指令白名单属于应用输入规则 |
| 曲线/多边形 | Skia + Clipper2 | 沿用成熟几何库，保真区域不再经过贝塞尔平滑 |
| 照片边缘到 GRBL 笔画 | 项目适配层 | 连接 OpenCV 像素边缘与逐路径关光语义；实际导出边缘覆盖率回归。已评估 skeleton-tracing，其端点容差拼接和稀疏拟合会改变笔画，当前采用有严格误差界的适配 |
| 持久化 | Microsoft.Data.Sqlite + SQLitePCLRaw | 沿用事务、进度和执行恢复；任务文件使用 .NET 原子写入及 SHA256 验证 |
| JSON/XML | System.Text.Json / XmlReader | 配置复用现有序列化框架；新增 USB 类型与值校验，用户配置不提供任意 .NET 类型名称 |
| 哈希 | System.Security.Cryptography | SHA256 继续使用框架实现 |
| 详情排版 | Android StaticLayout / TextView | 替换固定字数、固定七行分页；按真实字体、宽度、高度处理溢出 |
| 页面动画 | Android ViewPropertyAnimator / RippleDrawable | 替换瞬时切换；160 ms 合成属性动画，遵循系统动画开关 |
| 历史列表 | 缓存视图 + 后台 .NET Task / 现有 SQLite | 布局事件合并到布局完成后，仅数据/页码/容量变化时重建卡片；文件校验及数据库查询离开 UI 线程 |
| GRBL 解析与串行发送 | 项目协议适配层 + .NET 同步原语 | 保留经过测试的受限方言、字符流控、世代、显式屏障与新 Idle 完成语义；上游桌面发送器与 UI 生命周期耦合，协议策略继续由本项目集成验证 |
| 设备扩展 | 独立契约与 JSON 配置 | 保留星光和通用设备的分离，不从 USB 芯片推断主控型号或运动能力 |
| 质量回归 | xUnit + ZXing.Net + 原生 Skia/OpenCV | 完整密集二维码还原/解码、真实照片边缘、USB 描述符与控制线顺序、协议和 Android 工作流 |

## 已研究的候选

OpenCV、OpenCvSharp、sdcb/opencvsharp-mini-runtime、visioncortex/vtracer、LingDong-/linedraw、LingDong-/skeleton-tracing、LaserGRBL、anotherlab/UsbSerialForAndroid；细节见 ADR 0010 和复用矩阵。每次替换首先满足该模块的现有行为和质量门槛，再扩大部署范围。

## USB 工作流

设备页扫描后区分 Android 未枚举设备、设备已枚举而驱动未匹配、接口匹配和内置 VID/PID 匹配。打开“USB 设备诊断”查看原始信息；具备候选接口的设备可选择固定白名单驱动并持久化 VID/PID 映射，恢复内置匹配可移除覆盖。

设备参数中的 DTR/RTS 应用在下次连接；默认保持。可通过设备 JSON 的可选 usb 字段导入，示例见 samples/custom-usb-profile.json。当前界面和前台执行服务使用同一匹配逻辑，服务再次核对驱动及端口编号。

接口形状仅证明驱动候选条件，具体芯片通信由相应库驱动在连接时执行。本轮 USB 策略使用模拟端口和描述符测试，实体控制板验证单独记录。
