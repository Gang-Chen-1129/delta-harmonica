# 发布记录

## v0.1.0 · 2026-09-28

首个公开便携版。包含可调整宽度的曲谱库、分类与搜索、MIDI 导入、曲目删除、自定义全局热键、倒计时播放和屏幕提示。公开压缩包只含原创音阶示例，不含个人导入的歌曲。

- 文件：`dist/DeltaHarmonica-v0.1.0-win-x64-portable.zip`
- SHA-256：`fa5551421a771abcbcbc7640b9bb67c78f29542568744e22f1ec14b4625ee5bd`
- 构建：Windows x64，自包含 .NET 10；无需安装。
- 验证：同一源码的上一份构建已在 Win11 虚拟机检查界面、分类弹窗和 720p 布局；v0.1.0 的源码烟雾测试及压缩包完整性检查通过。公开压缩包保留原有程序二进制，仅更新说明、许可证并移除个人曲谱；已检查 ZIP 完整性，但公开压缩包尚未在 Win11 中启动。

### English

First public portable release with a searchable, categorized score library, MIDI import, editable notes, a configurable global hotkey, countdown playback, and on-screen status messages. The public archive contains only the original scale example. It includes the .NET runtime and runs on Windows x64 without a separate installation. The app itself has a Chinese interface and is unsigned. The archive passed ZIP integrity checks; this repackaged public archive has not yet been launched on Windows 11.

后续发布时更新项目文件的 `Version`，运行 `build-windows.ps1`，验证后更新本文件并创建同名 Git 标签。`dist` 只保留最新版发布包。
