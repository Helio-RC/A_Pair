# SeatFlow 示例插件

本目录包含多个示例插件，覆盖 SeatFlow 插件系统的四种形态：

| 目录 | 类型 | 说明 |
|---|---|---|
| `HeightSortPlugin` | C# 程序集独立策略 | 按身高降序填充空座（`kind: strategy`，assembly 加载） |
| `DeskPairPlugin` | C# 程序集依赖策略 | 同桌配对（`isIndependent: false`，验证 EvaluateAsync 通道） |
| `ScriptPlugins/lua` | Lua 脚本策略 | 前排优先（`scriptType: lua`） |
| `ScriptPlugins/csharp` | C# 脚本策略 | 空座顺序分配（`scriptType: csharp`） |
| `MultiStrategyPackage` | 多策略包 | 单包两个策略（验证双层 manifest 与多策略装配） |

## 构建与打包

```bash
cd examples/plugins
./build.sh        # 编译程序集插件并打包所有 .ap-plugin 到 dist/
```

打包产物为 `.ap-plugin`（ZIP 格式），可通过 SeatFlow 插件管理页的
"安装插件"按钮或直接拖放到插件管理页安装。

## 包格式（v2）

每个包根目录包含 `plugins-manifest.json`，声明包元数据与 `plugins[]` 数组：

```json
{
  "id": "height-sort",
  "name": "Height Sort Plugin",
  "version": "1.0.0",
  "type": "strategy",
  "plugins": [
    {
      "kind": "strategy",
      "path": "strategy",
      "manifest": "strategy/manifest.json",
      "assembly": "HeightSortPlugin.dll",
      "entryType": "HeightSortPlugin.HeightSortStrategy"
    }
  ]
}
```

- `kind`：插件类型（当前支持 `"strategy"`，预留 `"data-provider"` / `"exporter"`）
- 程序集插件：`assembly` + `entryType`；脚本插件：`scriptFile` + `scriptType`
- 策略级 `manifest.json` 遵循 `StrategyManifest` 格式（`displayName` / `defaultPriority` / `isIndependent` 等）

## 在测试中使用

`SeatFlow.Application.Tests` 的插件集成测试从 `dist/` 安装这些包进行端到端验证
（装配 → 执行 → 配置路由 → ALC 回收）。
