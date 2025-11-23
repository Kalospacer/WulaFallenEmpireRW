# 自主机械体系统改进计划

基于对 `AncotLibrary` 的分析，我们将对现有的 `WULA_AutonomousMech` 系统进行全面升级，旨在提供更灵活的配置、更友好的 UI 交互以及更智能的 AI 行为。

## 1. 核心架构重构

### 1.1 工作模式数据驱动化
*   **目标**: 废弃硬编码的 `AutonomousWorkMode` 枚举，转为使用 XML 定义的 `DroneWorkModeDef`。
*   **实现**:
    *   创建 `DroneWorkModeDef` 类，包含 `iconPath` (图标路径), `uiOrder` (排序), `label` (名称), `description` (描述) 等字段。
    *   在 `CompAutonomousMech` 中使用 `DroneWorkModeDef` 类型的字段替代原有的枚举。
    *   预定义基础模式：`Work` (工作), `Recharge` (充电), `Shutdown` (休眠), `AutoFight` (自动战斗)。

### 1.2 自动战斗系统 (`AutoFight`)
*   **目标**: 允许机械体在非征召状态下自动寻找并攻击敌人。
*   **实现**:
    *   引入 `CompMechAutoFight` 组件（或集成到 `CompAutonomousMech` 中）。
    *   添加 `ThinkNode_ConditionalAutoFight` 行为树节点。
    *   实现自动索敌和攻击的 AI 逻辑（参考 `JobGiver_AIFightEnemies`）。
    *   **威胁判定**: 确保开启自动战斗的机械体能被敌人正确识别为威胁（已部分实现，需完善）。

## 2. UI 交互增强

### 2.1 高级 Gizmo (`DroneGizmo`)
*   **目标**: 提供更直观的控制面板。
*   **实现**:
    *   **能量条**: 在 Gizmo 上直接显示当前能量百分比和剩余工作时间。
    *   **拖动设置**: 允许玩家通过拖动条设置“自动充电阈值”（例如：低于 30% 去充电）。
    *   **模式切换**: 点击图标弹出 `FloatMenu` 选择工作模式。
    *   **批量操作**: 当选中多个同类机械体时，Gizmo 操作应同步应用到所有选中的单位。

### 2.2 列表视图增强 (`PawnColumnWorker`)
*   **目标**: 在“动物/机械体”概览面板中提供关键信息。
*   **实现**:
    *   `PawnColumnWorker_DroneEnergy`: 显示能量条。
    *   `PawnColumnWorker_DroneWorkMode`: 显示当前工作模式图标，点击可快速切换。

## 3. AI 行为优化

### 3.1 智能充电与休眠
*   **目标**: 防止机械体在工作途中突然断电倒地。
*   **实现**:
    *   **低电量保护**: 当能量低于临界值（如 5%）且无法到达充电站时，自动寻找最近的安全地点（如室内、屋顶下）进入休眠状态 (`JobDriver_DroneSelfShutdown`)。
    *   **智能充电**: 优化 `JobGiver_GetDroneEnergy`，根据距离和当前工作优先级动态决定何时去充电。

### 3.2 永远可控 (`EverControllable`)
*   **目标**: 确保无论发生什么（如断网、无监管者），玩家始终能控制机械体。
*   **实现**:
    *   参考 `AncotPatch_MechanitorUtility_EverControllable`，通过 Harmony 补丁强制 `MechanitorUtility.EverControllable` 返回 true。

## 4. 实施步骤

1.  **定义 Defs**: 创建 `DroneWorkModeDef` 及相关 XML 配置。
2.  **重构 Comp**: 修改 `CompAutonomousMech` 以支持新的 Def 和逻辑。
3.  **UI 开发**: 实现 `DroneGizmo` 和 `PawnColumnWorker`。
4.  **AI 移植**: 移植并适配 `JobDriver_DroneSelfShutdown` 和相关 ThinkNodes。
5.  **补丁完善**: 添加 `EverControllable` 等缺失的 Harmony 补丁。
6.  **测试与验证**: 确保新旧系统平滑过渡，无红字报错。
