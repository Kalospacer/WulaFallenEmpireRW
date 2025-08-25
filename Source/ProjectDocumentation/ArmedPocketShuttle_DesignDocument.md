# 武装口袋穿梭机 (Armed Pocket Shuttle) - 设计文档

## 1. 项目概述

**目标**：重新设计并实现一个《边缘世界》(RimWorld) 的Mod，引入一种具备武装能力和内部“口袋空间”的特殊穿梭机。该穿梭机将解决现有实现中存在的顽固bug，并提供更稳定、更灵活的游戏体验。

**核心问题**：现有 `Building_ArmedShuttleWithPocket.cs` 的实现尝试过度模仿原版 `MapPortal` 的内部机制，导致代码复杂且难以调试和维护，最终陷入无法修复的bug。

**解决方案理念**：
*   **放弃现有问题代码**：彻底废弃当前 `Building_ArmedShuttleWithPocket.cs` 中导致bug的复杂逻辑。
*   **回归原版基础**：以 `Building_PassengerShuttle` 为基类，利用其成熟的运输和组件系统。
*   **组合与委托**：通过组合而非直接继承或深度模仿的方式，将 `MapPortal` 的概念融入到新穿梭机中，实现口袋空间功能。
*   **职责分离**：明确区分穿梭机本体（武装、飞行、外部运输）和口袋空间（内部地图、内部传送）的职责。

## 2. 核心设计思路

### 2.1 穿梭机本体 (`Building_ArmedShuttleWithPocket` - 新版)

*   **继承**: `Building_ArmedShuttleWithPocket` 将继承 `Building_ArmedShuttle`，从而自然继承了武装能力和 `Building_PassengerShuttle` 的所有基础功能，包括 `CompTransporter` 和 `CompShuttle` 组件。
*   **唯一容器**: 穿梭机自身的 `CompTransporter` 将是唯一且权威的物品和人员容器。所有装载操作都将首先将物品和人员放入这个 `CompTransporter` 的 `innerContainer`。
*   **武装能力**：通过继承 `Building_ArmedShuttle`，穿梭机将保留其炮塔和攻击逻辑。

### 2.2 口袋空间实现

口袋空间将是一个独立生成的 `Map` 实例，通过 `PocketMapUtility` 进行管理。

*   **内部地图 (`pocketMap`)**: `Building_ArmedShuttleWithPocket` 将持有一个 `Map` 类型的私有字段 `pocketMap`，代表内部空间。
*   **地图生成**:
    *   口袋地图的生成将通过 `CreatePocketMap()` 方法触发，该方法会调用 `PocketMapUtility.GeneratePocketMap()`。
    *   地图的尺寸 (`pocketMapSize`)、生成器 (`mapGenerator`) 和出口定义 (`exitDef`) 将通过 `PocketMapProperties` (`DefModExtension`) 从XML配置中获取。
    *   生成后，会在口袋地图的特定位置放置一个 `Building_PocketMapExit` 实例，作为进出内部空间的唯一通道。
*   **人员/物品进出**:
    *   **从主地图进入口袋空间**:
        *   人员：通过 `EnterPocketSpace(IEnumerable<Pawn> pawns)` 方法，将选定的小人从主地图传送到口袋地图的指定位置（例如出口附近）。
        *   物品：物品将首先通过穿梭机的 `CompTransporter` 装载。
    *   **从口袋空间返回主地图**: 通过 `Building_PocketMapExit` 来实现，它将负责将口袋空间内的物品和人员传送到主地图的穿梭机位置。
*   **内部物品管理**: 口袋地图内的物品将直接作为地图上的 `Thing` 存在，而不是由穿梭机本体的 `CompTransporter` 直接管理。当穿梭机被销毁时，口袋地图内的所有物品和人员将被安全地转移回主地图的穿梭机位置。

### 2.3 装载机制 (`GetGizmos` 重构)

`Building_ArmedShuttleWithPocket` 的 `GetGizmos()` 方法将被重写，以提供清晰且功能分离的装载选项：

1.  **“装载至货仓” (WULA.LoadIntoCargo)**：
    *   **功能**: 模拟原版穿梭机的装载行为。玩家选择人员和物品后，殖民者会将它们搬运到穿梭机，并存放到穿梭机自身的 `this.TransporterComp.innerContainer` 中。
    *   **实现**: 调用 `this.TransporterComp` 提供的标准装载对话框和逻辑。

2.  **“装载并传送入内” (WULA.LoadAndTeleport)**：
    *   **功能**: 玩家选择人员和物品，殖民者将其搬运到穿梭机并存放到 `this.TransporterComp.innerContainer`。**一旦装载完成**（即 `this.TransporterComp.leftToLoad` 为空），系统将自动触发一个内部传送过程，将 `this.TransporterComp.innerContainer` 中的所有物品和人员取出，并直接放置到口袋地图的指定位置。
    *   **实现**:
        *   通过一个布尔标志 (`doTeleportAfterLoading`) 来标记当前装载操作是否需要进行内部传送。
        *   在 `Tick()` 方法中监控 `this.TransporterComp.leftToLoad` 的状态。当其变为空且 `doTeleportAfterLoading` 为 `true` 时，调用 `TeleportContentsToPocketDimension()` 方法。
        *   `TeleportContentsToPocketDimension()` 方法将遍历 `this.TransporterComp.innerContainer` 中的所有物品和人员，使用 `Thing.DeSpawn()` 和 `GenPlace.TryPlaceThing()` 将它们移动到 `pocketMap` 的指定位置。
    *   **可见性**: 只有当口袋空间 (`pocketMap`) 已经生成 (`PocketMapExists == true`) 时，此按钮才会在UI中显示。

## 3. 关键组件/类 (`WulaFallenEmpire` 命名空间)

*   **`Building_ArmedShuttleWithPocket.cs` (主类)**:
    *   继承 `Building_ArmedShuttle`。
    *   私有字段 `pocketMap` (类型 `Map`)。
    *   布尔标志 `pocketMapGenerated`。
    *   `MapGeneratorDef mapGenerator` 和 `ThingDef exitDef` 用于XML配置。
    *   `public Building_PocketMapExit exit` 引用口袋出口。
    *   布尔标志 `doTeleportAfterLoading` 和 `wasLoading` 用于控制传送逻辑。
    *   属性 `PocketMap`, `PocketMapExists`, `PocketMapGenerated`。
    *   重写 `ExposeData()` 进行持久化。
    *   重写 `DeSpawn()` 清理口袋地图。
    *   重写 `Tick()` 监控装载状态并触发传送。
    *   重写 `GetInspectString()` 提供状态信息。
    *   重写 `GetGizmos()` 提供自定义Gizmo。
    *   方法 `CreateLoadGizmo(bool teleport)` 生成装载按钮。
    *   方法 `TeleportContentsToPocketDimension()` 执行内部传送。
    *   方法 `EnterPocketSpace(IEnumerable<Pawn> pawns)` 将人员传送到口袋空间。
    *   方法 `SwitchToPocketSpace()` 切换视角。
    *   方法 `CreatePocketMap()` 生成口袋地图。
    *   方法 `GeneratePocketMapInt()` (受保护虚方法，可重写)。
    *   方法 `GetExtraGenSteps()` (受保护虚方法，可重写)。
    *   方法 `PlaceExitInPocketMap()` 在口袋地图中放置出口。
    *   方法 `TransferPawnToPocketSpace(Pawn pawn)` 将单个小人传送到口袋空间。
    *   方法 `TransferAllFromPocketToMainMap()` 在销毁时将口袋内容传回主地图。
    *   实现 `IThingHolder` 接口：`GetChildHolders()` (将 `this.TransporterComp` 添加为子容器) 和 `GetDirectlyHeldThings()` (返回一个空的 `ThingOwner` 实例)。
    *   `UpdateExitPointTarget()` 更新出口目标位置。
    *   重写 `SpawnSetup()` 初始化组件和属性。

*   **`Building_PocketMapExit.cs` (现有)**:
    *   作为口袋空间的出口，负责将内部人员和物品传回主地图。

*   **`PocketMapProperties.cs` (现有)**:
    *   `DefModExtension` 类，用于在XML中配置口袋地图的尺寸 (`pocketMapSize`)、地图生成器 (`mapGenerator`) 和出口建筑定义 (`exitDef`)。

## 4. XML 定义 (`1.6/1.6/Defs/ThingDefs_Buildings/Building_WULA_ArmedShuttleWithPocket.xml`)

*   `ThingDef` 定义 `WULA_ArmedShuttleWithPocket`。
*   `modExtensions` 中包含 `PocketMapProperties`：
    ```xml
    <modExtensions>
      <li Class="WulaFallenEmpire.PocketMapProperties">
        <mapGenerator>WULA_PocketSpace_Small</mapGenerator> <!-- 使用正确的标签名 -->
        <exitDef>WULA_PocketMapExit</exitDef>
        <pocketMapSize>(13, 13)</pocketMapSize>
      </li>
    </modExtensions>
    ```

## 5. 预期结果

*   一个功能稳定、没有运行时崩溃的武装口袋穿梭机。
*   清晰的UI和交互流程，允许玩家选择不同的装载模式。
*   口袋空间能够正确生成、管理和销毁，内部物品和人员能够安全进出。
*   代码结构更清晰，易于理解和未来的维护。