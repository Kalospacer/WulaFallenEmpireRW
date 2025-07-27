# 自定义UI事件系统文档

## 1. 简介

本事件系统旨在为RimWorld提供一个强大的、数据驱动的、类似视觉小说的事件和事件链创建框架。它的设计灵感来源于Stellaris等策略游戏，允许开发者在XML中定义复杂的UI窗口、交互选项、事件效果和触发条件。

系统的核心由四个部分组成：
- **`CustomUIDef`**: 定义一个独立事件（UI窗口）的所有内容。
- **`Effect`**: 定义一个选项被点击后执行的具体动作（例如，给予物品、改变关系、打开新窗口等）。
- **`Condition`**: 定义一个选项是否可选的前提条件（例如，需要某个变量达到特定值）。
- **`EventContext`**: 一个全局的静态变量存储系统，允许在不同事件和效果之间传递数据。
- **`EventUIConfigDef`**: 一个全局的外观和布局配置文件，用于统一管理所有事件窗口的视觉风格。

---

## 2. 全局UI配置 (`EventUIConfigDef`)

为了方便统一修改所有事件窗口的外观和布局，系统使用一个单例的 `EventUIConfigDef`。你应该在 `Defs` 文件夹下创建一个XML文件来定义它。

**文件示例 (`1.6/Defs/ConfigDefs/EventUIConfig.xml`):**
```xml
<Defs>
  <WulaFallenEmpire.EventUIConfigDef>
    <defName>Wula_EventUIConfig</defName>
    
    <!-- 通用风格 -->
    <labelFont>Small</labelFont>
    <drawBorders>true</drawBorders>
    <defaultBackgroundImagePath>UI/Backgrounds/DefaultBG</defaultBackgroundImagePath>
    
    <!-- 虚拟布局尺寸 -->
    <lihuiSize>(500, 800)</lihuiSize>
    <nameSize>(260, 130)</nameSize>
    <textSize>(650, 500)</textSize>
    <optionsWidth>610</optionsWidth>
    
    <!-- 虚拟布局间距 -->
    <textNameOffset>20</textNameOffset>
    <optionsTextOffset>20</optionsTextOffset>
    
  </WulaFallenEmpire.EventUIConfigDef>
</Defs>
```

**字段说明:**
- `labelFont`: 事件标题 (`label`) 的字体大小。可选值: `Tiny`, `Small`, `Medium`, `Large`。
- `drawBorders`: 是否为立绘、名称和描述区域绘制白色边框。
- `defaultBackgroundImagePath`: 所有事件窗口默认使用的背景图路径。
- `lihuiSize`, `nameSize`, `textSize`, `optionsWidth`: 定义了UI各部分的基础虚拟尺寸，代码会根据窗口大小按比例缩放它们。
- `textNameOffset`, `optionsTextOffset`: 定义了各部分之间的垂直间距。

---

## 3. 如何创建事件 (`CustomUIDef`)

每个事件都是一个 `CustomUIDef`。你需要在一个 `Defs` XML文件中定义它。

**基本结构:**
```xml
<Defs>
  <WulaFallenEmpire.CustomUIDef>
    <defName>MyEvent_UniqueName</defName>
    <label>窗口标题</label>
    <portraitPath>Textures/UI/MyCharacter</portraitPath>
    <characterName>角色名称</characterName>
    <description>这里是事件的描述文本。</description>
    <options>
      <!-- 选项列表 -->
    </options>
  </WulaFallenEmpire.CustomUIDef>
</Defs>
```

**字段说明:**
- `defName`: 事件的唯一ID，用于在代码或其他事件中引用它。
- `label`: 显示在窗口左上角的标题。
- `portraitPath`: 立绘的纹理路径（相对于`Resources`或`Textures`目录）。
- `characterName`: 显示在名称框中的文本。
- `backgroundImagePath`: (可选)为此特定事件指定的背景图路径，它会覆盖 `EventUIConfigDef` 中的默认背景。
- `description`: 显示在描述框中的主要文本。
- `options`: 一个 `<li>` 列表，定义了所有的交互选项。

---

## 4. 核心概念：选项 (`CustomUIOption`)

每个选项都在 `<options>` 列表中的一个 `<li>` 标签内定义。

**字段说明:**
- `label`: (必须) 按钮上显示的文本。
- `effects`: (可选) 一个 `<li>` 列表，定义了点击此按钮后按顺序执行的所有 `Effect`。
- `conditions`: (可选) 一个 `<li>` 列表，定义了此按钮可选所必须满足的所有 `Condition`。只有所有条件都满足，按钮才能被点击。
- `disabledReason`: (可选) 一个字符串。当按钮因不满足`conditions`而禁用时，鼠标悬停在按钮上会显示此文本。如果未提供，则会自动显示第一个未满足的条件的原因。

---

## 5. 核心概念：效果 (`Effect`)

效果定义了“做什么”。每个效果都在 `effects` 列表中的一个 `<li>` 标签内定义，并且必须有一个 `Class` 属性。

### 已实现的 `Effect` 列表

#### 5.1 `Effect_OpenCustomUI`
- **功能**: 打开另一个自定义UI事件窗口。
- **Class**: `WulaFallenEmpire.Effect_OpenCustomUI`
- **字段**:
  - `defName`: (必须) 要打开的 `CustomUIDef` 的 `defName`。
- **示例**:
  ```xml
  <li Class="WulaFallenEmpire.Effect_OpenCustomUI">
    <defName>MyEvent_Step2</defName>
  </li>
  ```

#### 5.2 `Effect_CloseDialog`
- **功能**: 关闭当前的事件窗口。
- **Class**: `WulaFallenEmpire.Effect_CloseDialog`
- **字段**: 无
- **示例**:
  ```xml
  <li Class="WulaFallenEmpire.Effect_CloseDialog" />
  ```

#### 5.3 `Effect_ShowMessage`
- **功能**: 在屏幕左上角显示一条游戏消息。
- **Class**: `WulaFallenEmpire.Effect_ShowMessage`
- **字段**:
  - `message`: (必须) 要显示的文本。
  - `messageTypeDef`: (可选) 消息类型 (例如 `PositiveEvent`, `NegativeEvent`, `NeutralEvent`)。默认为 `PositiveEvent`。
- **示例**:
  ```xml
  <li Class="WulaFallenEmpire.Effect_ShowMessage">
    <message>你获得了一个物品！</message>
    <messageTypeDef>PositiveEvent</messageTypeDef>
  </li>
  ```

#### 5.4 `Effect_FireIncident`
- **功能**: 触发一个原版或Mod添加的游戏内事件。
- **Class**: `WulaFallenEmpire.Effect_FireIncident`
- **字段**:
  - `incident`: (必须) 要触发的 `IncidentDef` 的 `defName`。
- **示例**:
  ```xml
  <li Class="WulaFallenEmpire.Effect_FireIncident">
    <incident>RaidEnemy</incident>
  </li>
  ```

#### 5.5 `Effect_ChangeFactionRelation`
- **功能**: 改变与指定派系的好感度。
- **Class**: `WulaFallenEmpire.Effect_ChangeFactionRelation`
- **字段**:
  - `faction`: (必须) 目标 `FactionDef` 的 `defName`。
  - `goodwillChange`: (必须) 好感度的改变量，可以是正数或负数。
- **示例**:
  ```xml
  <li Class="WulaFallenEmpire.Effect_ChangeFactionRelation">
    <faction>Empire</faction>
    <goodwillChange>15</goodwillChange>
  </li>
  ```

#### 5.6 `Effect_SetVariable`
- **功能**: 在 `EventContext` 中设置或修改一个变量的值。
- **Class**: `WulaFallenEmpire.Effect_SetVariable`
- **字段**:
  - `name`: (必须) 变量的名称。
  - `value`: (必须) 变量的值。系统会尝试将其解析为整数或浮点数，如果失败则存为字符串。
- **示例**:
  ```xml
  <li Class="WulaFallenEmpire.Effect_SetVariable">
    <name>my_quest_progress</name>
    <value>1</value>
  </li>
  ```

#### 5.7 `Effect_GiveThing`
- **功能**: 给予玩家一个或多个物品。
- **Class**: `WulaFallenEmpire.Effect_GiveThing`
- **字段**:
  - `thingDef`: (必须) 要给予物品的 `ThingDef` 的 `defName`。
  - `count`: (可选) 给予的数量，默认为 1。
- **示例**:
  ```xml
  <li Class="WulaFallenEmpire.Effect_GiveThing">
    <thingDef>Silver</thingDef>
    <count>100</count>
  </li>
  ```

#### 5.8 `Effect_SpawnPawn`
- **功能**: 在地图上生成一个或多个Pawn，并可选地发送一封信件通知玩家。
- **Class**: `WulaFallenEmpire.Effect_SpawnPawn`
- **字段**:
  - `kindDef`: (必须) 要生成Pawn的 `PawnKindDef` 的 `defName`。
  - `count`: (可选) 生成的数量，默认为 1。
  - `joinPlayerFaction`: (可选) Pawn是否加入玩家派系，默认为 `true`。
  - `letterLabel`: (可选) 通知信件的标题。
  - `letterText`: (可选) 通知信件的内容。
  - `letterDef`: (可选) 通知信件的类型 (例如 `PositiveEvent`, `NegativeEvent`)。默认为 `PositiveEvent`。
- **示例**:
  ```xml
  <li Class="WulaFallenEmpire.Effect_SpawnPawn">
    <kindDef>Colonist</kindDef>
    <count>1</count>
    <joinPlayerFaction>true</joinPlayerFaction>
    <letterLabel>A New Colonist</letterLabel>
    <letterText>{PAWN_nameDef} has decided to join your colony.</letterText>
  </li>
  ```

#### 5.9 `Effect_ModifyVariable`
- **功能**: 对一个数值类型的变量进行数学运算（加、减、乘、除）。
- **Class**: `WulaFallenEmpire.Effect_ModifyVariable`
- **字段**:
  - `name`: (必须) 要修改的变量的名称。
  - `value`: (必须) 用于运算的数值。
  - `operation`: (必须) 执行的运算类型。可选值: `Add`, `Subtract`, `Multiply`, `Divide`。
- **示例**:
  ```xml
  <!-- 将变量 'player_score' 的值增加 10 -->
  <li Class="WulaFallenEmpire.Effect_ModifyVariable">
    <name>player_score</name>
    <value>10</value>
    <operation>Add</operation>
  </li>
  ```

#### 5.10 `Effect_ClearVariable`
- **功能**: 从事件上下文中移除一个变量。
- **Class**: `WulaFallenEmpire.Effect_ClearVariable`
- **字段**:
  - `name`: (必须) 要移除的变量的名称。
- **示例**:
  ```xml
  <li Class="WulaFallenEmpire.Effect_ClearVariable">
    <name>quest_completed_flag</name>
  </li>
  ```

#### 5.11 `Effect_AddQuest`
- **功能**: 给予玩家一个由游戏核心任务系统生成的任务。
- **Class**: `WulaFallenEmpire.Effect_AddQuest`
- **字段**:
  - `quest`: (必须) 要给予的 `QuestScriptDef` 的 `defName`。
- **示例**:
  ```xml
  <li Class="WulaFallenEmpire.Effect_AddQuest">
    <quest>OpportunitySite_BanditCamp</quest>
  </li>
  ```

#### 5.12 `Effect_FinishResearch`
- **功能**: 立即完成一个指定的科技研究项目。
- **Class**: `WulaFallenEmpire.Effect_FinishResearch`
- **字段**:
  - `research`: (必须) 要完成的 `ResearchProjectDef` 的 `defName`。
- **示例**:
  ```xml
  <li Class="WulaFallenEmpire.Effect_FinishResearch">
    <research>MicroelectronicsBasics</research>
  </li>
  ```

---

## 6. 核心概念：条件 (`Condition`)

条件定义了选项是否可选的“前提”。每个条件都在 `conditions` 列表中的一个 `<li>` 标签内定义，并且必须有一个 `Class` 属性。

### 已实现的 `Condition` 列表

#### 6.1 `Condition_VariableEquals`
- **功能**: 检查一个变量是否等于指定的值。支持字符串和数字的比较。
- **Class**: `WulaFallenEmpire.Condition_VariableEquals`
- **字段**:
  - `name`: (必须) 要检查的变量的名称。
  - `value`: (可选) 要比较的固定值。
  - `valueVariableName`: (可选) 存储比较值的变量的名称。如果同时提供了 `value` 和 `valueVariableName`，则优先使用 `valueVariableName`。
- **示例 (与固定值比较)**:
  ```xml
  <li Class="WulaFallenEmpire.Condition_VariableEquals">
    <name>quest_status</name>
    <value>completed</value>
  </li>
  ```
- **示例 (与另一个变量比较)**:
  ```xml
  <li Class="WulaFallenEmpire.Condition_VariableEquals">
    <name>player_choice</name>
    <valueVariableName>correct_answer</valueVariableName>
  </li>
  ```

#### 6.2 数值比较条件
以下所有条件都用于数值比较，并共享相同的字段。

- **通用字段**:
  - `name`: (必须) 要检查的变量的名称。
  - `value`: (可选) 要比较的固定数值。
  - `valueVariableName`: (可选) 存储比较数值的变量的名称。如果同时提供了 `value` 和 `valueVariableName`，则优先使用 `valueVariableName`。

- **`Condition_VariableGreaterThan`**: 检查变量是否 **大于** 比较值。
- **`Condition_VariableLessThan`**: 检查变量是否 **小于** 比较值。
- **`Condition_VariableGreaterThanOrEqual`**: 检查变量是否 **大于或等于** 比较值。
- **`Condition_VariableLessThanOrEqual`**: 检查变量是否 **小于或等于** 比较值。

- **示例 (大于固定值)**:
  ```xml
  <li Class="WulaFallenEmpire.Condition_VariableGreaterThan">
    <name>player_reputation</name>
    <value>50</value>
  </li>
  ```
- **示例 (小于或等于另一个变量)**:
  ```xml
  <li Class="WulaFallenEmpire.Condition_VariableLessThanOrEqual">
    <name>current_threat_level</name>
    <valueVariableName>max_allowed_threat</valueVariableName>
  </li>
  ```

---

## 7. 核心概念：变量系统 (`EventContext`)

`EventContext` 是一个全局的静态字典，用于在事件链的不同部分之间传递信息。

- **设置变量**: 使用 `Effect_SetVariable` 在XML中设置变量。
- **检查变量**: 使用 `Condition_VariableEquals` 或其他条件类来检查变量的值，从而控制事件流程。
- **使用变量**: 一些特殊的 `Effect` (例如 `Effect_ChangeFactionRelation_FromVariable`) 可以被设计为从 `EventContext` 中读取值来执行操作。

**注意**: 当前 `EventContext` 是全局共享的。在一个事件链结束后，最好能有一个 `Effect` 来清理掉设置的变量，以避免对其他不相关的事件产生影响（此功能待实现）。

---

## 8. 完整示例

以下是一个演示了事件链、变量和条件的完整示例。

```xml
<Defs>

  <!-- Event 1: 开始 -->
  <WulaFallenEmpire.CustomUIDef>
    <defName>Wula_ExampleUI</defName>
    <label>事件链示例 - 1</label>
    <description>这是一个事件链的开端。</description>
    <options>
      <li>
        <label>继续事件</label>
        <effects>
          <!-- 设置一个变量来追踪进度 -->
          <li Class="WulaFallenEmpire.Effect_SetVariable">
            <name>wula_event_progress</name>
            <value>1</value>
          </li>
          <!-- 打开下一个事件 -->
          <li Class="WulaFallenEmpire.Effect_OpenCustomUI">
            <defName>Wula_ExampleUI_Next</defName>
          </li>
          <!-- 关闭当前窗口 -->
          <li Class="WulaFallenEmpire.Effect_CloseDialog" />
        </effects>
      </li>
    </options>
  </WulaFallenEmpire.CustomUIDef>

  <!-- Event 2: 中段 -->
  <WulaFallenEmpire.CustomUIDef>
    <defName>Wula_ExampleUI_Next</defName>
    <label>事件链示例 - 2</label>
    <description>这是事件链的第二部分。</description>
    <options>
      <li>
        <label>完成事件</label>
        <effects>
          <li Class="WulaFallenEmpire.Effect_ShowMessage">
            <message>事件链已完成！</message>
          </li>
          <li Class="WulaFallenEmpire.Effect_CloseDialog" />
        </effects>
      </li>
      <li>
        <label>特殊选项</label>
        <disabledReason>需要事件进度=1</disabledReason>
        <!-- 这个选项只有在变量 'wula_event_progress' 等于 1 时才可选 -->
        <conditions>
          <li Class="WulaFallenEmpire.Condition_VariableEquals">
            <name>wula_event_progress</name>
            <value>1</value>
          </li>
        </conditions>
        <effects>
          <li Class="WulaFallenEmpire.Effect_ShowMessage">
            <message>你触发了特殊选项！</message>
          </li>
           <li Class="WulaFallenEmpire.Effect_CloseDialog" />
        </effects>
      </li>
    </options>
  </WulaFallenEmpire.CustomUIDef>

</Defs>
