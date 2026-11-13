# 0.4.0 安装包下载

[主镜像：直接下载 APK](https://gh-proxy.com/https://github.com/DGHH-Skywalker/GRBL_Tool_mobile/releases/download/v0.4.0-demo/GRBL-Tool-Mobile-0.4.0.apk)

[备用镜像：直接下载 APK](https://ghfast.top/https://github.com/DGHH-Skywalker/GRBL_Tool_mobile/releases/download/v0.4.0-demo/GRBL-Tool-Mobile-0.4.0.apk)

文件名：`GRBL-Tool-Mobile-0.4.0.apk`。大小：56,551,902 字节，约 54 MiB。

支持 Android 10（API 29）及以上，ARM64 或 x86_64。下载后安装，在设备页点击“体验完整演示”。

## 文件校验

```text
SHA256 0b7e0b922674bca3ae9d76e1f82986977b6713ad67b745685e2d3e255ec3dfed
```

2026-11-13 已从以上两个镜像各完整下载一份 APK，大小及 SHA256 均与原始已验收安装包一致。镜像只转发已有签名文件，保持原签名和内容。

源安装包来自完整通过 74 项核心测试及 Android 模拟器交互测试的提交 `c20b0b33ac8369ce4b86acc3c739167f28fde32a`。分发工作流首先检查原始流水线成功状态、源码提交与文件哈希，再发布原始签名 APK；已存在的发行文件通过下载校验复核，重复运行保持相同文件。

发行标签保留分发说明与 GPL-3.0-or-later 对应源码；发布任务核对标签修订与已验收构建之间仅有文档和分发流程差异。仓库保留 `v0.4.0-demo` 标签及对应源码，第三方声明与操作说明随源码维护。分发结果由 `Distribute verified demo` 工作流记录。

0.4 保持与 0.3 相同的演示签名，版本号递增，支持覆盖安装。单屏设计与格式白名单见 [MOBILE_UI.md](MOBILE_UI.md)。
