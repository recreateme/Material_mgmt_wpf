# 功能对照检查表（WPF v4.2.2）

对照源项目 PyQt6 v3.0，并纳入 v4.2 / ui phase 增量。勾选表示已在 WPF 侧落地可演示。

快速上手：[quickstart.md](quickstart.md) · WPF 教程：[wpf-tutorial.md](wpf-tutorial.md)

## 壳与认证

- [x] 登录（仅用户名+密码，无身份单选）
- [x] 两角色：`system_admin` / `business`；旧 `admin`/`operator`/`root` 启动时迁移
- [x] 出厂默认密码首次登录强制修改；失败 5 次锁定 60 秒
- [x] 占位显示名可补填真实姓名（欢迎语/工单操作员）
- [x] 登录页可选本地/在线背景图（`Assets/login-bg.jpg`）
- [x] 主窗口左侧导航 + 角色过滤
- [x] 登录后重建面板缓存
- [x] 修改密码 / 退出登录（退出时关闭称量窗）
- [x] 窗口几何记忆（settings.ini）
- [x] 状态栏设备指示
- [x] 登录后按配置自动连接电子秤（可关；已配置口不盲目扫全 COM）

## 主数据

- [x] 原材料 CRUD / 筛选 / 编码 / 分页 / 行勾选与全选（业务人员）
- [x] 配方列表 / 启停（可多选）/ 删除 / **复制配方** / 筛选分页 / 勾选全选（业务人员）
- [x] 配方步骤编辑（勾选多选、选中后插入/未选追加末尾、可滚动不分页、扫码选料、两阶段保存顺序）
- [x] 用户管理（系统管理员仅管业务人员；密码 ≥6 位）/ 勾选全选

## 双窗与称量

- [x] 业务人员「打开称量窗」（独立 `WeighHostWindow`；仅一个）
- [x] 选配方 / 扫 FML-
- [x] 工艺参数对话框（底栏固定 + 内容滚动）
- [x] 称量状态机（扫码提示材料名、错误提示、稳定后判定、空格确认）
- [x] 侧栏步骤进度（绿=已完成可回称、灰=待称、当前高亮；未来步不可点）
- [x] 回称：作废该步有效记录，其后已完成步保留；自动跳过仍有效的后续步
- [x] 读数过期/断线：撤销可确认态，防止对过期克重确认
- [x] 确认称重：PrepareConfirm + 落库 + CommitConfirm 两阶段
- [x] 量程条 RangeGauge（目标区间 + 实时指针着色）
- [x] 称量提示音（合格/超范围/错码/读数中断；设置可关）
- [x] 工单恢复 Resume
- [x] 取消工单
- [x] DeviceHub：空闲跟焦点窗；进行中称量独占；禁止第二称量窗；稳定 WeightUpdated 转发
- [x] 天平 ScaleWorker（协议对齐）+ 按需探测 / 重连
- [x] 扫码 BarcodeInputService（HID）+ BarcodeSerialService（USB COM）

## 历史与导出

- [x] 我的工单（称量窗：继续 / 取消进行中 / 删除已完成或已取消 / 导出 / 分页 / 勾选）
- [x] 工单历史（业务人员主窗：详情异常说明 / 导出 / 软删除可追溯 / 分页 / 勾选）
- [x] Excel 导出（ClosedXML，统一宋体；含快照字段）

## 其它

- [x] 首页统计与快捷入口（含打开称量窗）
- [x] 系统设置（两角色均可：秤/扫码 COM、模式、硬件自检、模拟扫码、提示音开关与试听、改密）
- [x] 启动自动备份 `data/backup/`（保留 14 份）+ 数据完整性兜底索引
- [x] 审计日志 audit.log（登录、用户、配方复制、回称、称量窗开闭等）
- [x] 种子账号仅空库（admin / operator；无 root）
- [x] formula_erp_bindings 表保留、UI 隐藏
- [x] 发布自包含脚本 `tools/publish.ps1`（保留库与配置）+ `DEPLOY.md`
- [x] 表格分页条（PaginationBar）
- [x] UI 主题（AppTheme + UiTheme 按钮工厂 / DialogBody）
- [x] 文档：README / Quickstart / PROJECT / TECH / WPF 教程
- [ ] 现场硬件联调签字 → 见 [hardware-checklist.md](hardware-checklist.md)
