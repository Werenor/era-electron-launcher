# Changelog

## 0.4.0 — 2026-10-30

- 默认沿用游戏窗口设置，可选择自定义尺寸、字号和最大化。
- 浏览官方引擎版本，下载直链ZIP或安装本地ZIP；保留已有引擎，检查解压路径及大小限制。
- 检查GitHub正式版本更新并打开发布页；游戏下载暂未接入。

- 新增“关于”页，内置完整GPL-2.0许可证和第三方声明，离线可读；用户发布包不再附带README、更新日志或独立法律文档。

- 保存引擎／游戏绝对路径及路径基准，迁移旧相对路径，避免换启动器目录后错误地寻找本地engine子目录。
- 引擎移位时在已知位置有限查找，仅自动恢复唯一的同SHA-256归档；不按文件夹名或版本号猜选，多个匹配或未知身份需要手动定位。
- 新增“重新定位引擎”和游戏失效路径选择提示，保留游戏绑定、目录和存档；缺失目录在启动前处理。
- 引擎原地更新后重新检查兼容性，不静默降级实时按键模式；启动组件缺失／混装时报告具体文件。
- 增加首次运行、跨文件夹更新、旧配置迁移、指纹唯一／歧义、原地升级与模式兼容的隔离自检。

## 0.3.0 — 2026-10-29

- Public release as **Era-Electron启动器**.
- Rename the display-name field to 游戏名称.
- Automatically save valid settings after editing; remove the save button and save-confirmation prompts.
- Register and select multiple engine versions. Standard launch no longer rejects engines based on the 5.1.0 allowlist.
- Display detected engine versions and expose the official engine release page in the default browser.
- Build the optional 5.1.0 adapter locally at launch; distribute no engine or private game binaries.
- Keep version-specific keystate extensions separate from unrestricted standard launch.
- Retain temporary engine isolation, game-directory locking, launch logs and exit cleanup.
