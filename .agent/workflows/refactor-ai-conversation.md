# AI 对话系统重构任务

## 目标
合并 `Dialog_AIConversation` 和 `AIIntelligenceCore`，简化 AI 对话系统架构。

## 当前架构问题

### 1. 两个类存在重复逻辑
- `Dialog_AIConversation.cs` - 大对话框 UI，包含自己的:
  - `_history` 历史记录
  - `RunPhasedRequestAsync()` 三阶段对话逻辑
  - `ExecuteXmlToolsForPhase()` 工具执行
  - `BuildSystemInstruction()` 系统指令构建
  - `BuildUserMessageWithContext()` 上下文附加

- `AIIntelligenceCore.cs` - WorldComponent，包含:
  - `_history` 历史记录 (重复!)
  - `RunPhasedRequestAsync()` 三阶段对话逻辑 (重复!)
  - `ExecuteXmlToolsForPhase()` 工具执行 (重复!)
  - `SendUserMessage()` 用户消息发送
  - 但 **没有** `BuildUserMessageWithContext()` 导致小窗口看不到选中对象上下文

### 2. 小窗口 `Overlay_WulaLink` 使用 `AIIntelligenceCore.SendUserMessage()`
- 这个方法没有附加选中对象的上下文
- 导致通过小窗口对话时，AI 看不到玩家选中了什么

### 3. 历史记录不同步
- `Dialog_AIConversation` 和 `AIIntelligenceCore` 各自维护历史记录
- 可能导致状态不一致

## 重构方案

### 方案 A: 让 AIIntelligenceCore 成为唯一的对话逻辑中心

1. **在 `AIIntelligenceCore.SendUserMessage()` 中添加上下文附加逻辑**:
```csharp
public void SendUserMessage(string text)
{
    string messageWithContext = BuildUserMessageWithContext(text);
    _history.Add(("user", messageWithContext));
    PersistHistory();
    _ = RunPhasedRequestAsync();
}

private string BuildUserMessageWithContext(string userText)
{
    StringBuilder sb = new StringBuilder();
    sb.Append(userText);

    if (Find.Selector != null)
    {
        if (Find.Selector.SingleSelectedThing != null)
        {
            var selected = Find.Selector.SingleSelectedThing;
            sb.AppendLine();
            sb.AppendLine();
            sb.Append($"[Context: Player has selected '{selected.LabelCap}'");
            if (selected is Pawn pawn)
            {
                sb.Append($" ({pawn.def.label}) at ({pawn.Position.x}, {pawn.Position.z})");
            }
            else
            {
                sb.Append($" at ({selected.Position.x}, {selected.Position.z})");
            }
            sb.Append("]");
        }
        else if (Find.Selector.SelectedObjects.Count > 1)
        {
            sb.AppendLine();
            sb.AppendLine();
            sb.Append($"[Context: Player has selected {Find.Selector.SelectedObjects.Count} objects: ");
            var selectedThings = Find.Selector.SelectedObjects.OfType<Thing>().Take(5).ToList();
            sb.Append(string.Join(", ", selectedThings.Select(t => t.LabelCap)));
            if (Find.Selector.SelectedObjects.Count > 5) sb.Append("...");
            sb.Append("]");
        }
    }

    return sb.ToString();
}
```

2. **让 `Dialog_AIConversation` 使用 `AIIntelligenceCore` 而不是自己的逻辑**:
   - 移除 `Dialog_AIConversation` 中的 `RunPhasedRequestAsync()`
   - 移除 `Dialog_AIConversation` 中的 `ExecuteXmlToolsForPhase()`
   - 让 `SelectOption()` 调用 `_core.SendUserMessage()` 而不是自己处理
   - 订阅 `_core.OnMessageReceived` 事件来更新 UI

3. **统一历史记录**:
   - 只使用 `AIIntelligenceCore._history`
   - `Dialog_AIConversation` 通过 `_core.GetHistorySnapshot()` 获取历史

### 方案 B: 完全删除 AIIntelligenceCore，合并到 Dialog_AIConversation

不推荐，因为 `AIIntelligenceCore` 作为 WorldComponent 可以在游戏暂停/存档时保持状态。

## 文件位置

- `c:\Steam\steamapps\common\RimWorld\Mods\3516260226\Source\WulaFallenEmpire\EventSystem\AI\AIIntelligenceCore.cs`
- `c:\Steam\steamapps\common\RimWorld\Mods\3516260226\Source\WulaFallenEmpire\EventSystem\AI\UI\Dialog_AIConversation.cs`
- `c:\Steam\steamapps\common\RimWorld\Mods\3516260226\Source\WulaFallenEmpire\EventSystem\AI\UI\Overlay_WulaLink.cs`

## 验证标准

1. 编译通过 (`dotnet build Source\WulaFallenEmpire\WulaFallenEmpire.csproj`)
2. 通过大窗口和小窗口对话时，AI 都能看到选中对象的上下文
3. 历史记录在两个窗口之间保持同步
4. 没有空行/空消息问题
5. 工具调用正常工作

## 最小修复 (如果不想大重构)

只在 `AIIntelligenceCore.SendUserMessage()` 中添加 `BuildUserMessageWithContext()` 逻辑即可解决小窗口看不到上下文的问题。
