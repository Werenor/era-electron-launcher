# Era-Electron启动器

独立的 Windows 第三方启动器：集中管理本地 Era-Electron 游戏与多个引擎版本，选择游戏后启动，退出时清理临时引擎。

## 使用

下载 Windows x64 发布包，完整解压，双击 **StartGame.exe**。需要 Windows 10/11 与系统 .NET Framework 4.8；无需安装 Python，无需管理员权限。发行包不附带引擎或游戏。

1. 打开“引擎管理”，通过“引擎官方发布页”在浏览器访问官方站点，下载并解压需要的版本。
2. 点击“登记引擎目录”，可登记多个版本。
3. 导入包含 `ere/main.js` 或打包入口的游戏目录。
4. 为游戏选择引擎。游戏名称、输入模式与可用的窗口选项在编辑后自动保存。
5. 点击“启动游戏”。启动器保持打开，显示运行日志；游戏退出后清理临时引擎并返回就绪状态。

## 多版本与扩展

标准启动不按版本号设白名单，原版引擎不会被修改。启动器检测安装包中的版本并允许每个游戏独立选择。现有 Era-Electron 使用独立引擎用户配置的 `last` 路径打开选定游戏；如未来引擎改变这一启动约定，需增加对应适配。

**引擎版本支持与输入扩展适配是两回事。** keystate 1.1.0 和临时窗口参数目前为已验证的原版 5.1.0 提供。其他版本使用标准启动，窗口由游戏自己的配置决定，界面不会套用不匹配的补丁。后续可在保留其他版本的同时增加适配。

支持游戏列表搜索、导入、复制启动配置、移出列表、引擎校验、停止游戏、日志文件与目录入口。移出列表不删除游戏或存档。

## 数据与退出清理

- 配置：当前用户 LocalAppData/PortArcadeLauncher/launcher.json，保存时保留备份。该历史目录名用于兼容早期版本。
- 每个启动配置的引擎用户数据保存在 `instances/<ID>/profile`。
- 日志保存在 `logs`，包括启动记录和引擎日志快照。
- 引擎运行在 `sessions/<随机ID>` 的临时副本，关闭后清理。异常残留会在下次启动时检查。
- 存档仍在游戏目录。复制启动配置不会复制游戏或存档；同一游戏目录不允许并发启动。
- 有游戏运行时关闭启动器，会询问是否停止并清理。普通设置直接自动保存，没有“保存设置”按钮。临时空白的游戏名称等无效输入不会覆盖最后一次有效配置。

keystate 采用 Electron 输入事件与 Windows 原生松键校验，修复真实环境可能缺失的 keyup；只在游戏启用捕获且窗口有焦点时运行原生组件，退出或失焦停止。不安装服务或全局键盘钩子。

## 构建

在安装有 .NET Framework 4.x 编译器的 Windows 上运行：

```powershell
.uild.ps1
```

脚本编译启动器和原生组件，运行检查，生成 `dist/Era-Electron-Launcher-v0.3.0-win-x64.zip` 与 SHA256SUMS.txt。构建和运行均无需下载原版引擎，无 Python 依赖。

```powershell
.\dist\Era-Electron-Launcher-v0.3.0-win-x64\StartGame.exe --self-test C:\Temp\launcher-checks.txt
```

源码包括图形界面、配置存储、进程生命周期、ASAR 适配器、keystate 与原生组件。`build.ps1` 从源码生成组件校验值。

## 项目与许可

本项目不是官方 Era-Electron 发行版。官方引擎与作者：[UmaERA / Era-Electron](https://gitgud.io/umaera/engine/era-electron)，[官方发布页](https://gitgud.io/umaera/engine/era-electron/-/releases)。

GPL-2.0，参见 LICENSE 和 THIRD_PARTY_NOTICES.md。发布包不包含私人游戏目录、存档、令牌、开发机配置或原版引擎二进制。
