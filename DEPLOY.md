# 部署与数据迁移说明（WPF v4.0）

上手操作请先看 [docs/quickstart.md](docs/quickstart.md)。

## 1. 发布产物结构

```
原料管理系统/
├── 原料管理系统.exe
├── *.dll / 运行时文件
├── data/
│   └── material_mgmt.db      ← 业务库（SQLite）
├── config/
│   └── settings.ini          ← 串口、窗口几何、自动连接
├── logs/
│   ├── app.log
│   └── audit.log
├── docs/                     ← README / Quickstart / 部署与联调
└── 使用说明.txt
```

## 2. 开发机发布

```powershell
cd D:\Develop\Material_Mgmt
powershell -ExecutionPolicy Bypass -File .\tools\publish.ps1
```

行为要点：

- 自包含 `win-x64`，目标机**无需安装 .NET**
- **自动备份并恢复**现有 `dist\...\data\material_mgmt.db`（含 wal/shm）
- **自动备份并恢复**现有 `config\settings.ini`
- 产物：`dist\原料管理系统\` 与 `dist\原料管理系统-v4.0.0-win-x64.zip`

## 3. 目标机部署

1. 复制**整个**「原料管理系统」文件夹（或解压 zip）
2. 双击 `原料管理系统.exe`
3. 空库会写种子账号；若已带 `data\material_mgmt.db` 则沿用原账号
4. 登录后会**自动识别并连接电子秤**；失败时可到「系统设置」手动选 COM

## 4. 从 PyQt v3.0 迁移数据

1. **先退出**旧版与新版程序  
2. 将旧版 `data\material_mgmt.db`（及 `-wal`/`-shm`）拷到新版同级 `data\`  
3. 启动 WPF 版；启动时会校验/补齐 schema  
4. `settings.ini` 可选拷贝；COM 口建议在新机确认一次  

> 不要多进程同时写同一 SQLite 文件。

## 5. 备份建议

- 退出程序后复制整个 `data\`（及需要时的 `config\`）
- 重要变更或重新打包前先备份

## 6. 默认种子账号（仅空库）

| 用户名 | 密码 | 角色 |
|--------|------|------|
| root | root666 | 开发人员 |
| admin | admin123 | 管理人员 |
| operator | operator123 | 实验人员 |

## 7. 常见问题

**Q：拷贝后数据是空的？**  
确认数据库位于 exe 同级的 `data\material_mgmt.db`，且分发的是整夹而非单个 exe。

**Q：电子秤未自动连接？**  
检查线缆与驱动；看状态栏提示；设置页「刷新端口」后保存。登录后会周期重试识别。

**Q：打包后数据/串口配置丢了？**  
使用仓库内最新 `tools\publish.ps1`（会保留 dist 内库与 ini）。打包前请先退出正在运行的程序。

**Q：能否只拷 exe？**  
不能。请整夹或 zip 分发。
