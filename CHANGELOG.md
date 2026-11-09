# Changelog

## 0.7.0 — 2026-11-09

- 过渡顺序：先升级启动器，再更新游戏；旧游戏声明仍可使用，无需手工修改JSON。
- 统一游戏和插件声明格式：schemaVersion 1、完整 HTTPS 仓库地址，增加 JSON Schema 与字段校验。
- 继续读取旧声明，保留插件缓存、每游戏启停记录；格式错误给出字段位置。
- 游戏开发与发行分支统一使用新声明格式，不保留旧格式发行；需使用支持格式v1的启动器。

## 0.6.0 — 2026-11-07

- 游戏更新改为读取声明指定的发布分支，不再依赖游戏 Release。
- 先锁定分支提交 SHA，再读取该提交的版本声明并生成同一 SHA 的源码 ZIP 下载链接。
- 分支默认 main，支持显式 branch；校验远程声明的仓库身份。
- 检查只读，确认后仅打开下载链接，不覆盖游戏、存档或玩家设置。
- 启动器和独立插件继续通过 Release 更新。
- 插件页显示插件名称、适用版本号及未安装／未启用／已启用状态，勾选后立即更新状态。


## 0.5.0 — 2026-11-07

- 独立插件声明、自报发布仓库，支持按游戏安装与启停，移除输入模式选择。
- 首次进入插件页和启动前检查声明，未加载插件时提醒；fallback 明确控制兼容运行。
- 游戏列表按最近启动排序，显示启动时间与游戏版本。
- 游戏声明仓库和当前版本，支持 GitHub / GitGud 正式 Release 只读更新检查。
- 更新检查不覆盖游戏、存档和设置，确认后仅打开发布页。
- 提供整包部署脚本，避免启动器与组件混装。


## 0.4.1 — 2026-11-01

- 引擎管理新增“从目录中移除”，默认保留文件，可选择移入回收站。
- 新增“清理失效目录”，仅移除路径已不存在的引擎登记。
- 移除后清空相关游戏的引擎绑定，保留游戏、存档与输入模式。
- 删除文件前检查运行状态、目录重叠、游戏文件和链接目录。


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
