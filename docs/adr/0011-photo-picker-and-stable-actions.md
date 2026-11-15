# 0011：系统相册导入与稳定的按钮交互

2026-11-15。位图走 Android 系统照片选择器，SVG/G-code/设备 JSON 使用文档接口。

## 平台接口

采用 Android 自带 Intent 和 ContentResolver，保持现有 C# / 原生 Views 技术栈。优先解析 `android.provider.action.PICK_IMAGES`，覆盖 Android 13 及有系统回移模块的设备；旧 ROM 使用 `ACTION_PICK` 与 `MediaStore.Images.Media.EXTERNAL_CONTENT_URI`。位图路径不回退到文档选择器。清单声明对应可见性 queries；入口不可用或启动异常时恢复按钮并提示。

参考：[Android Photo Picker](https://developer.android.com/training/data-storage/shared/photopicker)、[MediaStore.ACTION_PICK_IMAGES](https://developer.android.com/reference/android/provider/MediaStore#ACTION_PICK_IMAGES)、[ACTION_PICK](https://developer.android.com/reference/android/content/Intent#ACTION_PICK)。不引入照片全库读取权限。系统照片选择器的 image/* 展示范围由平台提供，应用返回后继续核验 PNG/JPG/WebP/BMP 白名单、真实编码、完整解码、动态帧、体积与像素预算。

部分相册 URI 没有文件名或扩展名；只在扩展名缺失时按受支持 MIME 补全名称，原有扩展名矛盾、GIF/HEIC、伪装图片等继续拒绝。支持单项 ClipData 返回；取消和错误保留当前作品。

## 全局交互修复

- 通用选项页预建稳定按钮，布局重排只改变可见范围；移除触摸过程中重新创建控件的逻辑。关闭页面的延迟回调通过页面版本失效。
- 异步操作使用统一忙碌集合。250 ms 状态刷新遵守同一集合，阻止重复开始、确认、导出等操作；完成后按实际设备和任务状态恢复按钮。
- “生成任务”明确允许再次点击取消，保持取消入口可用。
- 关闭应用内面板时无条件清理覆盖层与触摸拦截；恢复到前台时复位动画属性。
- 每次导入使用独立请求编号并保存到实例状态。显式重新导入会结束上次尚未返回的选择器，旧结果不覆盖新请求。
- 启动恢复任务使用版本及参数修订检查，防止异步读取旧任务覆盖刚导入的作品。
- 导出在点击时固定任务/日志内容，使用临时文件跨系统页面及 Activity 重建保存；返回后写入该快照。增加成功/取消反馈和错误清理，避免导出文件取到后续选择。
- 演示初始化等待协议给出的 CanArm 快照；移除固定 450 ms 后直接就绪的假设。CanArm 来自已有配置、应答后新 Idle、坐标、队列及故障判定，不改变任务启动和完成屏障。

## 验证

核心新增相册元数据及真实内容验证；CanArm 由已有配置/新 Idle 场景覆盖。Actions 模拟器走真实 Photo Picker，验证相册取消后再次导入、GIF 拒绝保留旧作品、文档 SVG 拒绝、导出取消/成功、操作确认返回、菜单反复开关、照片与 QR 生成、紧凑屏幕和键盘。照片夹具通过系统 MediaStore 索引，按每次一张选择；断言位图入口没有 DocumentsUI。每个截图检查点输出进度，保留最终图片和 UI XML。

实际通过的提交与运行记录于 VALIDATION.md；旧 ROM 相册及各厂商真机适配继续记入设备验收矩阵。
