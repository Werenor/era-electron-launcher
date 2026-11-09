# 声明格式 v1

此格式将在下一版启动器提供。已发布的 v0.6.0 不支持新插件声明，需先升级启动器，再分发新声明；不能仅替换游戏中的 JSON 后要求旧启动器继续使用。新启动器继续读取无 schemaVersion 的旧格式，不自动改写玩家文件。

游戏文件名固定为 `ere.launcher.json`，插件包文件名固定为 `plugin.json`。编码 UTF-8，标准 JSON，不使用注释、尾逗号或字符串形式的布尔值。两个格式的 JSON Schema 位于 `schemas/`；编辑器可通过本地 `$schema` 字段引用。启动器内置字段校验，不联网下载 Schema，不依赖玩家安装额外校验库。

```json
{
  "schemaVersion": 1,
  "game": {
    "repository": "https://gitgud.io/ereal/game",
    "version": "0.8.1",
    "branch": "release"
  },
  "plugins": [
    {
      "id": "ere-keystate",
      "repository": "https://github.com/Werenor/ere-keystate",
      "fallback": true
    }
  ]
}
```

- schemaVersion：必填，整数1。它是声明格式版本，不是游戏、插件或引擎版本。未知格式版本明确报错，不按旧格式猜测。
- game：可选；提供时 repository、version 必填。version 是本地安装的游戏版本，不是想升级到的版本。branch 省略默认main。
- repository：完整HTTPS仓库根地址。允许末尾斜线或.git，内部统一规范化；不能填写Release页、分支页、下载链接、令牌、查询参数或片段。
- plugins：可选数组。每项id、repository必填；同一仓库与id不得重复。fallback 必须是布尔值，省略默认false；false表示未加载插件时阻止启动，true表示提醒后允许兼容运行。
- 插件请求不填写插件版本或引擎版本要求，也不声明能力。当前插件下载仅支持GitHub Releases；游戏更新支持GitHub和GitGud发布分支。
- v1拒绝未知字段，字段类型和必填项错误会指出字段位置。不认识的字段不会被悄悄忽略。旧格式继续按兼容路径读取。

插件包沿用既有补丁字段，增加schemaVersion并统一repository：

```json
{
  "schemaVersion": 1,
  "id": "example-plugin",
  "name": "示例插件",
  "repository": "https://github.com/author/example-plugin",
  "version": "1.0.0",
  "engineVersions": ["5.1.0"],
  "main": ".webpack/main/index.js",
  "prepend": "",
  "replacements": [],
  "files": []
}
```

插件包version是插件自身版本；name、engineVersions可省略。engineVersions仅显示适用版本说明，不作版本门禁；运行时仍检查文件摘要和补丁锚点。main、prepend、replacements、files均必填，具体文件项与替换项见plugin.schema.json。

迁移时旧的source=github、repository=作者/仓库会转换为与新地址相同的内部身份，继续使用原缓存和每游戏停用记录。新游戏请求可以加载旧插件包，旧游戏请求也可以加载新插件包。插件不能通过改名或省略身份校验冒充另一仓库的插件。

检查更新仍是只读操作，升级声明格式不改游戏存档、安装目录或引擎文件。

迁移期间，游戏开发分支可先提交v1声明；发行分支暂保留旧格式。启动器新版Release发布后，再切换游戏和插件的正式发行包。仅推送源码不会改变已经发布的启动器和插件Release。
