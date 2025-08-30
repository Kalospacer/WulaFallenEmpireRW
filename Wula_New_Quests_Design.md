# 乌拉族新任务设计文档

本文档旨在详细阐述为乌拉族Mod添加三个新任务的设计与实现方案。这三个任务分别为：**回收物品**、**安插信标**和**运送建材**。

## 1. 总体设计思路

我们将遵循RimWorld原版的任务（Quest）框架，为每个任务创建对应的`IncidentDef`（事件定义）作为触发器，以及`QuestScriptDef`（任务脚本定义）来描述任务的具体流程。

- **XML驱动**：尽可能利用原版的QuestNode来构建任务逻辑，减少C#代码的编写量。
- **C#扩展**：对于原版QuestNode无法实现的功能（如检查穿梭机内容、特定地点交互），我们将编写自定义的C#类（QuestNode、WorldObjectComp或GameComponent）。
- **模块化**：每个任务都将是独立的，有自己的`IncidentDef`和`QuestScriptDef`，便于管理和未来的扩展。
- **本地化**：所有面向玩家的文本（任务名、描述、信件内容等）都将使用Keyed-Value形式，并提供中英双语支持。

## 2. 任务详解

### 2.1. 回收物品 (Recover Item)

**任务描述**: 玩家接到任务，需要前往一个由佣兵或强盗看守的地图，取回一个特定的无价值物品（任务物品），并成功带回殖民地。一旦物品带回，乌拉族会派穿梭机前来回收，任务完成。

**实现方案**:
- **触发**: 创建一个新的`IncidentDef`，`defName: Wula_Incident_RecoverItem`。
- **任务脚本**: 创建`QuestScriptDef`，`defName: Wula_Quest_RecoverItem`。该脚本将参照原版的`OpportunitySite_ItemStash`。
- **流程图 (Mermaid)**:
  ```mermaid
  graph TD
      A[任务触发: Wula_Incident_RecoverItem] --> B{生成任务地点和看守};
      B --> C[在任务地点生成任务物品];
      C --> D[玩家前往并击败看守];
      D --> E[玩家拾取任务物品并带回基地];
      E --> F{监听物品进入玩家基地地图};
      F --> G[生成乌拉族回收穿梭机];
      G --> H[玩家将物品交给穿梭机];
      H --> I[任务完成，给予奖励];
  ```
- **关键QuestNode**:
  - `QuestNode_GetSiteTile`: 生成任务地点。
  - `QuestNode_GetSitePartDefsByTagsAndFaction`: 定义地点的敌人（佣兵/强盗）。
  - `QuestNode_GenerateThing`: 在任务地点生成任务物品。
  - `QuestNode_SignalListen`:
    - 监听`item.Map.IsPlayerHome`来检测物品是否被带回基地。
    - 监听`item.Transferable.Things`来检测物品是否被装入穿梭机。
  - `QuestNode_DropPods`: 用于生成乌拉族回收穿梭机。
  - `QuestNode_End`: 结束任务并给予奖励。
- **新定义**:
  - `ThingDef`: 一个新的任务物品，例如`Wula_QuestItem_AncientDataDevice`，它没有市场价值，不可交易，但可以被携带。

### 2.2. 安插信标 (Place Beacon)

**任务描述**: 乌拉族空投一个可打包的信标建筑。玩家需要将信标带到另一个由机械族看守的地图，并在指定区域进行“安装”（放置）。安装成功后任务完成。

**实现方案**:
- **触发**: 创建`IncidentDef`，`defName: Wula_Incident_PlaceBeacon`。
- **任务脚本**: 创建`QuestScriptDef`，`defName: Wula_Quest_PlaceBeacon`。
- **流程图 (Mermaid)**:
  ```mermaid
  graph TD
      A[任务触发: Wula_Incident_PlaceBeacon] --> B[在玩家基地空投信标物品];
      B --> C{生成任务地点和机械族看守};
      C --> D[玩家携带信标前往任务地点];
      D --> E[击败机械族];
      E --> F{在指定区域安装信标};
      F --> G[任务完成，给予奖励];
  ```
- **关键QuestNode**:
  - `QuestNode_DropPods`: 在玩家基地空投信标。
  - `QuestNode_GetSiteTile`: 生成任务地点。
  - `QuestNode_GetSitePartDefsByTagsAndFaction`: 定义地点的敌人（机械族）。
  - `QuestNode_SignalListen`: 监听一个自定义信号，如`wula.beaconPlaced`。
- **C# 扩展**:
  - `Building_QuestBeacon`: 一个继承自`Building`的C#类。当这个建筑在任务地图上成功建造完成时，它会触发`wula.beaconPlaced`信号。
- **新定义**:
  - `ThingDef`: 一个可打包、可安装的信标建筑，例如`Wula_QuestBuilding_Beacon`，并关联到`Building_QuestBeacon`类。

### 2.3. 运送建材 (Deliver Materials)

**任务描述**: 乌拉族派遣一艘穿梭机降落在玩家基地，玩家需要在限定时间内将指定数量的材料（如钢铁、玻璃钢等）装入穿梭机。装满后穿梭机离开，任务完成。

**实现方案**:
- **触发**: 创建`IncidentDef`，`defName: Wula_Incident_DeliverMaterials`。
- **任务脚本**: 创建`QuestScriptDef`，`defName: Wula_Quest_DeliverMaterials`。
- **流程图 (Mermaid)**:
  ```mermaid
  graph TD
      A[任务触发: Wula_Incident_DeliverMaterials] --> B[在玩家基地生成穿梭机和任务参数<br>(所需材料/数量/时限)];
      B --> C{启动计时器和物品检查器};
      C --> D{玩家装载材料};
      D -- 未装满 --> E{检查是否超时};
      E -- 超时 --> F[任务失败，穿梭机离开];
      D -- 装满 --> G[任务成功，给予奖励];
      G --> H[穿梭机离开];
  ```
- **关键QuestNode**:
  - `QuestNode_Delay`: 用于设置任务时限。
  - `QuestNode_SignalListen`: 监听`wula.materialsDelivered`成功信号或`wula.deliveryFailed`失败信号。
- **C# 扩展**:
  - `QuestNode_WulaShuttleAndChecker`: 一个自定义的`QuestNode`。它的功能是：
    1.  在玩家基地生成一艘穿梭机。
    2.  初始化任务参数（需要的物品、数量）。
    3.  启动一个计时器。
    4.  持续检查穿梭机内的物品数量。
    5.  当数量满足或时间耗尽时，发送对应的成功/失败信号。
- **新定义**:
  - 无需新的`ThingDef`，将直接使用游戏内已有的材料。

## 3. 文件结构

- **XML**:
  - `1.6/1.6/Defs/IncidentDefs/Wula_ScheduledIncidents.xml`: 添加3个新的`IncidentDef`。
  - `1.6/1.6/Defs/QuestScriptDefs/Wula_ScheduledEvents.xml`: 添加3个新的`QuestScriptDef`。
  - `1.6/1.6/Defs/ThingDefs_Misc/Wula_QuestItems.xml`: 创建一个新的XML文件，用于存放任务相关的物品定义。
- **C#**:
  - `Source/WulaFallenEmpire/Quests/`: 在源码中创建一个`Quests`目录。
  - `Source/WulaFallenEmpire/Quests/Building_QuestBeacon.cs`: “安插信标”任务的建筑逻辑。
  - `Source/WulaFallenEmpire/Quests/QuestNode_WulaShuttleAndChecker.cs`: “运送建材”任务的核心逻辑节点。
- **语言 (Languages)**:
  - `1.6/1.6/Languages/ChineseSimplified/Keyed/Wula_Quest_Keys.xml`: 中文文本。
  - `1.6/1.6/Languages/English/Keyed/Wula_Quest_Keys.xml`: 英文文本。

---
这份文档概述了新任务的完整实现计划。请审阅。