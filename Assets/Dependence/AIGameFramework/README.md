# AI Architecture Documentation

用于 Unity 项目的纯编辑器架构文档包。它用 Node 树描述功能边界，用接口引用记录模块之间的依赖，供开发者和 AI agent 使用。

包本身不包含运行时逻辑、场景、渲染配置或 Unity 工程文件；导入宿主项目后直接使用。

## 包内容

- `Editor/`：IMGUI 架构编辑器、数据模型、校验和编辑逻辑。
- `package.json`：Unity Package Manager 包配置。
- `Documentation~/AI_WORKFLOW.md`：AI 修改功能时应遵循的架构工作规范。
- `Documentation~/EDITOR.md`：编辑器界面和操作说明。
- `Documentation~/architecture.example.json`：架构数据格式示例。
- `Documentation~/agent-skills/architecture-node-workflow/`：可复制到宿主项目的 agent skill。

架构数据属于宿主项目，不在包内。首次打开编辑器时会在宿主项目创建 `Assets/AIArchitecture/architecture.json`，应将它纳入版本控制。

## 安装

要求 Unity 6.0 或更高版本。

1. 将整个包目录复制到宿主项目的 `Packages/com.aigameframework.architecture`。
2. 或在宿主项目的 `Packages/manifest.json` 中添加本地路径依赖，例如：

   ```json
   "com.aigameframework.architecture": "file:../path/to/this/package"
   ```

3. 等待 Unity 编译完成。

如果希望像普通依赖目录一样使用，也可以把整个目录直接放到宿主项目的 `Assets/Dependence/com.aigameframework.architecture`；`Editor/`、assembly definition 和文档结构仍然有效，架构数据仍写入宿主项目的 `Assets/AIArchitecture/architecture.json`。推荐优先使用 `Packages/` 安装，便于后续更新。

## 使用

- 通过 Unity 菜单 `Tools/AI 架构编辑器` 打开，或点击顶部工具栏的 `Documentation` 按钮。
- 编辑器会自动创建并保存 `Assets/AIArchitecture/architecture.json`。
- 修改会自动保存；数据文件可随宿主项目提交和共享。

## 给 AI 使用

1. 阅读 `Documentation~/AI_WORKFLOW.md`。
2. 将 `Documentation~/agent-skills/architecture-node-workflow` 复制到宿主项目的 `.agents/skills/` 或所用 agent 的 skills 目录。
3. 在宿主项目的 `AGENTS.md`、`CLAUDE.md` 或其他 agent 指令中引用包内的 `Documentation~/AI_WORKFLOW.md`，并按实际安装路径调整。

## 兼容性

仅支持编辑器，不产生运行时依赖。包代码位于 `Editor/` 并通过 editor-only assembly definition 编译。
