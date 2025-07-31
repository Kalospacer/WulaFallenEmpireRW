# Wula Fallen Empire - 事件系统文档

这是一个用于在RimWorld中创建复杂、带选项的事件和对话框的强大系统。它由两个主要部分组成：**任务事件** 和 **EventDef事件**。

## 核心概念

- **Effect（效果）**: 一个原子操作，例如生成一个Pawn、给予一个物品、改变派系关系或打开另一个UI。
- **Condition（条件）**: 一个用于决定一个选项是否可用的逻辑检查（例如，检查一个变量的值）。
- **EventContext（事件上下文）**: 一个全局的静态类，用于存储和检索变量，允许在不同的事件和UI之间传递数据。

---

## 1. 任务事件 (`QuestNode_Root_EventLetter`)

这是通过RimWorld的原版任务系统触发的事件。它会生成一个带有选项的信件。

### 如何使用

1.  在你的 `QuestScriptDef` 中，使用 `WulaFallenEmpire.QuestNode_Root_EventLetter` 作为根节点。
2.  在XML中定义 `letterLabel`, `letterTitle`, `letterText`。
3.  在 `<options>` 列表中定义多个选项。每个选项都有一个 `label` 和一个或多个 `effects`。

### 示例 (`QuestScriptDef`)

```xml
<?xml version="1.0" encoding="utf-8" ?>
<Defs>
  <QuestScriptDef>
    <defName>Wula_ExampleQuestEvent</defName>
    <root Class="WulaFallenEmpire.QuestNode_Root_EventLetter">
      <letterLabel>一个抉择</letterLabel>
      <letterTitle>远方的呼唤</letterTitle>
      <letterText>一个来自遥远星系的信号抵达了你们的通讯站。他们似乎想和你们谈谈。</letterText>
      <options>
        <li>
          <label>接受通讯</label>
          <optionEffects>
            <li Class="WulaFallenEmpire.Effect_OpenCustomUI">
              <defName>Wula_ExampleEvent</defName>
            </li>
          </optionEffects>
        </li>
        <li>
          <label>忽略他们</label>
          <optionEffects>
            <li Class="WulaFallenEmpire.Effect_ShowMessage">
              <message>你决定无视这个信号。宇宙的寂静再次笼罩着你。</message>
              <messageTypeDef>NeutralEvent</messageTypeDef>
            </li>
          </optionEffects>
        </li>
      </options>
    </root>
  </QuestScriptDef>
</Defs>
```

---

## 2. EventDef事件 (`Dialog_CustomDisplay`)

这是一个高度可定制的对话框窗口，可以显示角色肖像、背景、文本和多个带条件的选项。

### 如何使用

1.  创建一个 `EventDef`。
2.  定义 `label`, `characterName`, `portraitPath`, `descriptions` 等。
3.  在 `<options>` 列表中定义选项。每个选项可以有关联的 `effects` 和 `conditions`。
4.  你可以通过 `Effect_OpenCustomUI` 效果来打开这个UI（从任务事件或其他EventDef）。
5.  你也可以通过将 `CompOpenCustomUI` 附加到一个建筑上来从游戏中直接打开它。

### `EventDef` 参数

- **label**: (string) 窗口的标题。
- **characterName**: (string) 显示在肖像下方的角色名称。
- **portraitPath**: (string) 角色肖像的纹理路径。
- **descriptions**: (List<string>) 一个描述文本列表。可以通过 `descriptionMode` 控制是随机选择一个还是按顺序显示。
- **options**: (List<EventOption>) 对话框中显示的选项列表。
- **immediateEffects**: (List<ConditionalEffects>) 当对话框打开时立即执行的效果列表。
- **dismissEffects**: (List<ConditionalEffects>) 当对话框关闭时（通过关闭按钮或`Effect_CloseDialog`）执行的效果列表。
- **backgroundImagePath**: (string) (可选) 对话框的背景图片路径。
- **windowSize**: (Vector2) (可选) 自定义窗口大小。
- **hiddenWindow**: (bool) (可选) 如果为 `true`，则不会显示窗口。在这种模式下，`immediateEffects` 的内容会在加载时自动合并到 `dismissEffects` 中，然后在事件触发时作为单个效果链统一执行。这对于创建纯粹的后台“效果链”事件非常有用。默认为 `false`。

### `EventDef` 示例

```xml
<?xml version="1.0" encoding="utf-8" ?>
<Defs>
  <WulaFallenEmpire.EventDef>
    <defName>Wula_ExampleEvent</defName>
    <label>神秘的通讯</label>
    <characterName>特使</characterName>
    <portraitPath>Textures/Wula/Events/Portraits/Envoy</portraitPath>
    <descriptions>
      <li>“你好，来自边缘世界的陌生人。我们观察你很久了。你的挣扎……很有趣。”</li>
    </descriptions>
    <immediateEffects>
      <!-- 这是一个无条件的ConditionalEffects块 -->
      <li>
        <!-- 没有<conditions>，所以总是执行 -->
        <effects>
          <li Class="WulaFallenEmpire.Effect_SetVariable">
            <name>MetTheEnvoy</name>
            <value>true</value>
          </li>
        </effects>
      </li>
    </immediateEffects>
    <dismissEffects>
      <!-- 这是一个有条件的ConditionalEffects块 -->
      <li>
        <conditions>
          <li Class="WulaFallenEmpire.Condition_VariableEquals">
            <name>PlayerMadeChoice</name>
            <value>false</value>
          </li>
        </conditions>
        <effects>
          <li Class="WulaFallenEmpire.Effect_ShowMessage">
            <message>你没有做出选择就关闭了通讯。</message>
          </li>
        </effects>
      </li>
    </dismissEffects>
    <options>
      <li>
        <label>“你是谁？”</label>
        <optionEffects>
          <li>
            <effects>
              <li Class="WulaFallenEmpire.Effect_ShowMessage">
                <message>“我们是观察者。我们是见证者。现在，我们是你的未来。”</message>
              </li>
              <li Class="WulaFallenEmpire.Effect_CloseDialog" />
            </effects>
          </li>
        </optionEffects>
      </li>
      <li>
        <label>“给我们一些东西来证明你的诚意。”</label>
        <disabledReason>他们似乎对你不够信任。</disabledReason>
        <!-- 这个conditions块现在只用于决定选项是否可点击 -->
        <conditions>
          <li Class="WulaFallenEmpire.Condition_VariableGreaterThanOrEqual">
            <name>EmpireGoodwill</name>
            <value>50</value>
          </li>
        </conditions>
        <optionEffects>
          <li>
            <!-- 你甚至可以在选项的效果内部再次添加条件 -->
            <conditions>
                <li Class="WulaFallenEmpire.Condition_VariableEquals">
                    <name>IsGenerous</name>
                    <value>true</value>
                </li>
            </conditions>
            <effects>
              <li Class="WulaFallenEmpire.Effect_GiveThing">
                <thingDef>Gold</thingDef>
                <count>200</count> <!-- 如果IsGenerous为true，则给予更多 -->
              </li>
              <li Class="WulaFallenEmpire.Effect_CloseDialog" />
            </effects>
          </li>
          <li>
            <effects>
              <li Class="WulaFallenEmpire.Effect_GiveThing">
                <thingDef>Gold</thingDef>
                <count>100</count>
              </li>
              <li Class="WulaFallenEmpire.Effect_CloseDialog" />
            </effects>
          </li>
        </optionEffects>
      </li>
    </options>
  </WulaFallenEmpire.EventDef>
</Defs>
```

### UI 布局配置 (`EventUIConfigDef`)

你可以在 `1.6/Defs/WulaMiscSettingDefs/EventUIConfig.xml` 中调整所有EventDef窗口的默认外观和布局。

---

## 3. 核心结构: 条件化效果 (`ConditionalEffects`)

所有执行效果的地方 (`immediateEffects`, `dismissEffects`, 以及每个选项的 `optionEffects`) 都是一个 `ConditionalEffects` 块的列表。

这允许你将一组效果与一组条件绑定在一起。

### `ConditionalEffects` 结构

每个 `<li>` 代表一个 `ConditionalEffects` 块。它包含两个可选部分：
- **`<conditions>`**: 一个条件列表。只有当这里的所有条件都满足时，对应的效果才会执行。如果省略这个部分，效果将总是执行。
- **`<effects>`**: 一个效果列表。当条件满足时，这些效果会被执行。

```xml
<!-- 示例: 一个ConditionalEffects块 -->
<li>
  <conditions>
    <li Class="WulaFallenEmpire.Condition_VariableEquals">
      <name>PlayerChoice</name>
      <value>AcceptedOffer</value>
    </li>
  </conditions>
  <effects>
    <li Class="WulaFallenEmpire.Effect_ShowMessage">
      <message>你接受了提议！</message>
    </li>
    <li Class="WulaFallenEmpire.Effect_GiveThing">
      <thingDef>Silver</thingDef>
      <count>500</count>
    </li>
  </effects>
</li>
```

---

## 4. 可用的效果 (`Effect`)

这些是可以在 `ConditionalEffects` 块的 `<effects>` 列表中使用的类。

### `Effect_OpenCustomUI`
打开一个指定的 `EventDef`。
- **defName**: (string) 要打开的 `EventDef` 的 `defName`。
```xml
<li Class="WulaFallenEmpire.Effect_OpenCustomUI">
  <defName>Wula_AnotherEvent</defName>
</li>
```

### `Effect_CloseDialog`
关闭当前的EventDef窗口。没有参数。
```xml
<li Class="WulaFallenEmpire.Effect_CloseDialog" />
```

### `Effect_ShowMessage`
在屏幕上显示一条消息。
- **message**: (string) 要显示的消息文本。
- **messageTypeDef**: (MessageTypeDef) 消息的类型 (例如 `PositiveEvent`, `NegativeEvent`, `NeutralEvent`)。默认为 `PositiveEvent`。
```xml
<li Class="WulaFallenEmpire.Effect_ShowMessage">
  <message>你获得了一个新的盟友。</message>
  <messageTypeDef>PositiveEvent</messageTypeDef>
</li>
```

### `Effect_FireIncident`
触发一个指定的事件。
- **incident**: (IncidentDef) 要触发的事件的 `defName`。
```xml
<li Class="WulaFallenEmpire.Effect_FireIncident">
  <incident>RaidEnemy</incident>
</li>
```

### `Effect_ChangeFactionRelation`
改变玩家与某个派系的关系。
- **faction**: (FactionDef) 目标派系的 `defName`。
- **goodwillChange**: (int) 关系值的变化量（可以是负数）。
```xml
<li Class="WulaFallenEmpire.Effect_ChangeFactionRelation">
  <faction>WulaFallenEmpire_Player</faction>
  <goodwillChange>15</goodwillChange>
</li>
```

### `Effect_ChangeFactionRelation_FromVariable`
根据一个变量的值改变派系关系。
- **faction**: (FactionDef) 目标派系的 `defName`。
- **goodwillVariableName**: (string) 存储关系变化值的变量名。
```xml
<li Class="WulaFallenEmpire.Effect_ChangeFactionRelation_FromVariable">
  <faction>WulaFallenEmpire_Player</faction>
  <goodwillVariableName>ReputationChange</goodwillVariableName>
</li>
```

### `Effect_GiveThing`
给玩家一些物品（通过空投）。
- **thingDef**: (ThingDef) 要给予的物品的 `defName`。
- **count**: (int) 给予的数量。默认为 1。
```xml
<li Class="WulaFallenEmpire.Effect_GiveThing">
  <thingDef>Plasteel</thingDef>
  <count>150</count>
</li>
```

### `Effect_SpawnPawn`
生成一个Pawn。
- **kindDef**: (PawnKindDef) 要生成的Pawn的 `defName`。
- **count**: (int) 生成的数量。默认为 1。
- **joinPlayerFaction**: (bool) 是否加入玩家派系。默认为 `true`。
- **letterLabel**: (string) 可选，生成时附带的信件标题。
- **letterText**: (string) 可选，生成时附带的信件内容。
- **letterDef**: (LetterDef) 可选，信件的类型。
```xml
<li Class="WulaFallenEmpire.Effect_SpawnPawn">
  <kindDef>Colonist</kindDef>
  <count>1</count>
  <joinPlayerFaction>true</joinPlayerFaction>
  <letterLabel>一个新人加入了！</letterLabel>
  <letterText>一个流浪者被你们的善举所吸引，决定加入你们的殖民地。</letterText>
</li>
```

### `Effect_SpawnPawnAndStore`
生成一个Pawn并将其存储在一个变量中以备后用。
- **kindDef**: (PawnKindDef) 要生成的Pawn的 `defName`。
- **count**: (int) 生成的数量。默认为 1。
- **storeAs**: (string) 用于存储生成Pawn的变量名。如果 `count` 大于1，则存储一个Pawn列表。
```xml
<li Class="WulaFallenEmpire.Effect_SpawnPawnAndStore">
  <kindDef>Wula_Elite_Warrior</kindDef>
  <storeAs>spawnedWarrior</storeAs>
</li>
```

### `Effect_AddQuest`
触发一个新的任务。
- **quest**: (QuestScriptDef) 要开始的任务的 `defName`。
```xml
<li Class="WulaFallenEmpire.Effect_AddQuest">
  <quest>Wula_AnotherQuest</quest>
</li>
```

### `Effect_FinishResearch`
立即完成一个研究项目。
- **research**: (ResearchProjectDef) 要完成的研究的 `defName`。
```xml
<li Class="WulaFallenEmpire.Effect_FinishResearch">
  <research>MicroelectronicsBasics</research>
</li>
```

### `Effect_TriggerRaid`
触发一次袭击。这个效果有两种模式：
1.  **简单模式**: 使用派系默认的袭击队伍。
2.  **高级模式**: 使用动态定义的 `pawnGroupMakers` 来生成自定义的袭击队伍。

- **points**: (float) 袭击的点数。
- **faction**: (FactionDef) 袭击者的派系 `defName`。
- **raidStrategy**: (RaidStrategyDef) 袭击策略的 `defName` (例如 `ImmediateAttack`)。
- **raidArrivalMode**: (PawnsArrivalModeDef) 袭击者到达方式的 `defName` (例如 `EdgeWalkIn`)。
- **groupKind**: (PawnGroupKindDef) (高级模式) 定义队伍类型，例如 `Combat` 或 `Trader`。默认为 `Combat`。
- **pawnGroupMakers**: (List<PawnGroupMaker>) (高级模式) 一个 `PawnGroupMaker` 列表，用于动态定义袭击队伍的构成。
- **letterLabel**: (string) (可选) 自定义袭击信件的标题。如果提供，将覆盖默认的 "Raid" 标题。
- **letterText**: (string) (可选) 自定义袭击信件的内容。如果提供，将覆盖默认的袭击描述文本。

**简单模式示例:**
```xml
<li Class="WulaFallenEmpire.Effect_TriggerRaid">
  <points>500</points>
  <faction>Pirate</faction>
  <raidStrategy>ImmediateAttack</raidStrategy>
  <raidArrivalMode>EdgeWalkIn</raidArrivalMode>
  <letterLabel>侦测到威胁！</letterLabel>
  <letterText>我们的传感器侦测到一伙来自 {FACTION_name} 的袭击者！他们看起来充满敌意，正朝着我们的殖民地前进。</letterText>
</li>
```

**高级模式示例:**
```xml
<li Class="WulaFallenEmpire.Effect_TriggerRaid">
  <points>1000</points>
  <faction>WulaFallenEmpire_Player</faction>
  <raidStrategy>ImmediateAttack</raidStrategy>
  <raidArrivalMode>EdgeWalkIn</raidArrivalMode>
  <groupKind>Combat</groupKind>
  <pawnGroupMakers>
    <li>
      <kindDef>Combat</kindDef>
      <commonality>100</commonality>
      <options>
        <Mech_WULA_Cat_Constructor>20</Mech_WULA_Cat_Constructor>
        <Mech_WULA_Cat_Assault>20</Mech_WULA_Cat_Assault>
        <Wula_Broken_Personality_Pawn_7>2</Wula_Broken_Personality_Pawn_7>
        <Wula_Broken_Personality_Pawn_5>1</Wula_Broken_Personality_Pawn_5>
      </options>
    </li>
  </pawnGroupMakers>
</li>
```

### `Effect_SetVariable`
设置一个 `EventContext` 变量的值。
- **name**: (string) 变量名。
- **value**: (string) 变量的值。系统会自动尝试将其解析为 `int` 或 `float`，如果失败则作为 `string` 存储。
```xml
<li Class="WulaFallenEmpire.Effect_SetVariable">
  <name>PlayerChoice</name>
  <value>AcceptedOffer</value>
</li>
```

### `Effect_ModifyVariable`
对一个数字变量进行加、减、乘、除操作。
- **name**: (string) 变量名。
- **value**: (float) 用于操作的数值。
- **operation**: (VariableOperation) 操作类型，可以是 `Add`, `Subtract`, `Multiply`, `Divide`。
```xml
<li Class="WulaFallenEmpire.Effect_ModifyVariable">
  <name>ResourceCount</name>
  <value>-10</value>
  <operation>Add</operation> <!-- This will subtract 10 -->
</li>
```

### `Effect_ClearVariable`
从 `EventContext` 中移除一个变量。
- **name**: (string) 要清除的变量名。
```xml
<li Class="WulaFallenEmpire.Effect_ClearVariable">
  <name>PlayerChoice</name>
</li>
```

---

## 4. 可用的条件 (`Condition`)

这些是可以在 `conditions` 列表中使用的类，用于控制选项的可用性。

### `Condition_VariableEquals`
检查一个变量是否等于一个特定值。
- **name**: (string) 要检查的变量名。
- **value**: (string) 要比较的字面值。
- **valueVariableName**: (string) (可选) 要比较的另一个变量的名称。如果提供此项，则忽略 `value`。
```xml
<li Class="WulaFallenEmpire.Condition_VariableEquals">
  <name>PlayerChoice</name>
  <value>AcceptedOffer</value>
</li>
```

### `Condition_CompareVariable` (基类)
这是一个抽象基类，不应直接使用。以下所有比较条件（大于、小于等）都继承自这个基类，并共享其参数。

**基类参数:**
- **name**: (string) 要检查的变量名。
- **value**: (float) 要比较的字面数值。
- **valueVariableName**: (string) (可选) 要比较的另一个变量的名称。如果提供此项，则会忽略 `value` 字段。

**工作原理:**
当你使用例如 `Condition_VariableGreaterThan` 时，你实际上是在使用一个 `Condition_CompareVariable` 的特定版本。你可以提供 `value` 来与一个固定的数字比较，或者提供 `valueVariableName` 来与另一个变量的值进行比较。

**变量与变量比较示例:**
下面的例子使用了 `Condition_VariableGreaterThanOrEqual`（它是 `Condition_CompareVariable` 的子类），来检查 `PlayerWealth` 变量是否大于或等于 `RequiredWealth` 变量。
```xml
<li Class="WulaFallenEmpire.Condition_VariableGreaterThanOrEqual">
  <name>PlayerWealth</name>
  <valueVariableName>RequiredWealth</valueVariableName>
</li>
```

### `Condition_VariableGreaterThan`
检查一个变量是否 **大于** 一个特定值。
```xml
<li Class="WulaFallenEmpire.Condition_VariableGreaterThan">
  <name>ColonistCount</name>
  <value>5</value>
</li>
```

### `Condition_VariableLessThan`
检查一个变量是否 **小于** 一个特定值。
```xml
<li Class="WulaFallenEmpire.Condition_VariableLessThan">
  <name>ThreatPoints</name>
  <value>1000</value>
</li>
```

### `Condition_VariableGreaterThanOrEqual`
检查一个变量是否 **大于或等于** 一个特定值。
```xml
<li Class="WulaFallenEmpire.Condition_VariableGreaterThanOrEqual">
  <name>EmpireGoodwill</name>
  <value>50</value>
</li>
```

### `Condition_VariableLessThanOrEqual`
检查一个变量是否 **小于或等于** 一个特定值。
```xml
<li Class="WulaFallenEmpire.Condition_VariableLessThanOrEqual">
  <name>YearsPassed</name>
  <value>2</value>
</li>
```

### `Condition_VariableNotEqual`
检查一个变量是否 **不等于** 一个特定值。
```xml
<li Class="WulaFallenEmpire.Condition_VariableNotEqual">
  <name>QuestStage</name>
  <value>3</value>
</li>
```

---
