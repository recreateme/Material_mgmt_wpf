# Syncfusion WPF UI Builder（Cursor）

本仓库已安装 [Syncfusion WPF UI Builder](https://help.syncfusion.com/wpf/agentic-ui-builder) 的 Cursor 目标技能，用于通过自然语言生成 Syncfusion WPF 界面。

## 已部署内容

| 路径 | 说明 |
|------|------|
| `.cursor/agents/syncfusion-wpf-ui-builder.md` | Cursor Agent 配置 |
| `.agents/skills/syncfusion-wpf-ui-builder/` | 8 阶段 UI 编排主技能 |
| `.agents/skills/syncfusion-wpf-*/` | 各 Syncfusion 控件技能（约 96+） |
| `.vendor/apm-packages/` | 离线安装用的上游包副本 |

## 使用方式

1. 在 Cursor 聊天面板选择 Agent：**syncfusion-wpf-ui-builder**
2. 用自然语言描述要生成的界面（登录窗、DataGrid、仪表盘等）
3. 按 Agent 分阶段确认主题、代码生成与插入

## 环境要求（已核对）

- APM CLI：已安装（`apm --version`）
- .NET SDK：≥ 6（本项目为 .NET 8 WPF）
- Node.js：Stage 3 控件映射脚本需要（建议 14+）
- Git：APM 在线安装依赖克隆仓库时需要
- **Syncfusion 许可证**：商用 / 社区 / 试用（生成代码并引用 NuGet 后需注册密钥）

## 在线重装（网络正常时）

```powershell
apm install syncfusion/wpf-ui-builder -t cursor
```

若 `git clone` 访问 GitHub 失败，可使用 `.vendor/apm-packages/` 中的离线包，或将技能目录同步到 `.agents/skills/`。

## 参考

- [官方文档](https://help.syncfusion.com/wpf/agentic-ui-builder)
- [APM 安装说明](https://microsoft.github.io/apm/getting-started/installation/)
