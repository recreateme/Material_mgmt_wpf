# 功能对照检查表（WPF v4.1.5）

对照源项目 PyQt6 v3.0。勾选表示已在 WPF 侧落地可演示。

快速上手：[quickstart.md](quickstart.md) · WPF 教程：[wpf-tutorial.md](wpf-tutorial.md)

## 壳与认证

- [x] 登录（管理员/操作员入口，root 例外）
- [x] 主窗口左侧导航 + 角色过滤
- [x] 登录后重建面板缓存
- [x] 修改密码 / 退出登录
- [x] 窗口几何记忆（settings.ini）
- [x] 状态栏设备指示
- [x] 登录后自动识别并连接电子秤

## 主数据

- [x] 原材料 CRUD / 筛选 / 编码 / 分页 / 行勾选与全选
- [x] 配方列表 / 启停 / 删除 / 筛选分页 / 勾选全选
- [x] 配方步骤编辑（增删改排序）
- [x] 用户管理（admin 仅 operator；root 可管 admin+operator）/ 勾选全选

## 称量

- [x] 选配方 / 扫 FML-
- [x] 工艺参数对话框
- [x] 称量状态机（扫码提示材料名、错误提示、稳定后判定、空格确认）
- [x] 工单恢复 Resume
- [x] 取消工单
- [x] 天平 ScaleWorker（协议对齐）+ 自动探测 / 重连
- [x] 扫码 BarcodeInputService（HID）+ BarcodeSerialService（USB COM）

## 历史与导出

- [x] 我的工单（继续 / 取消进行中 / 删除已完成 / 导出 / 分页 / 勾选）
- [x] 工单历史（详情异常说明 / 导出 / 删除 / 分页 / 勾选）
- [x] Excel 导出（ClosedXML，统一宋体）

## 其它

- [x] 首页统计与快捷入口
- [x] 系统设置（秤/扫码 COM、模式、硬件自检、模拟扫码、改密）
- [x] 审计日志 audit.log
- [x] 种子账号仅空库
- [x] formula_erp_bindings 表保留、UI 隐藏
- [x] 发布自包含脚本 `tools/publish.ps1`（保留库与配置）+ `DEPLOY.md`
- [x] 表格分页条（PaginationBar）
- [x] UI 主题（AppTheme + UiTheme 按钮工厂）
- [x] 文档：README / Quickstart / PROJECT / TECH / WPF 教程
- [ ] 现场硬件联调签字 → 见 [hardware-checklist.md](hardware-checklist.md)
