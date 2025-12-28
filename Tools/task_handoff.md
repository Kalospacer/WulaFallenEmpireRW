# WulaLink 任务交接文档

## 当前状态：需要创建 AIIntelligenceCore.cs

### 背景
WulaLink 是一个 RimWorld Mod 中的 AI 对话系统，包含两个 UI：
1. **大 UI** (`Dialog_AIConversation`) - 全屏对话窗口
2. **小 UI** (`Overlay_WulaLink`) - 悬浮对话窗口

### 已完成的操作
1. ✅ 恢复了 `Dialog_AIConversation.cs` 为旧版自包含版本（从备份文件 `Tools/using System;.cs` 复制）
2. ✅ 删除了损坏的 `AIIntelligenceCore.cs`
3. ✅ 重写了 `WulaLinkStyles.cs`（颜色主题配置）

### 当前编译错误
```
error CS0246: 未能找到类型或命名空间名"AIIntelligenceCore"
```

以下文件引用了 `AIIntelligenceCore`：
- `Overlay_WulaLink.cs` (第13行, 第94行)
- `Overlay_WulaLink_Notification.cs` (第89行)
- `Tool_ChangeExpression.cs` (第24行)
- `Tool_GetRecentNotifications.cs` (第113行)

---

## 需要完成的任务

### 任务：创建 AIIntelligenceCore.cs

**路径**: `Source/WulaFallenEmpire/EventSystem/AI/AIIntelligenceCore.cs`

**要求**：
1. 必须是 `WorldComponent`，类名 `AIIntelligenceCore`
2. 提供 `static Instance` 属性供外部访问
3. 从 `Dialog_AIConversation`（备份文件 `Tools/using System;.cs`）提取 AI 核心逻辑
4. 暴露事件/回调供 UI 使用

**必须包含的公共接口**（根据现有代码引用）：
```csharp
public class AIIntelligenceCore : WorldComponent
{
    // 静态实例
    public static AIIntelligenceCore Instance { get; private set; }
    
    // 事件回调
    public event Action<string> OnMessageReceived;
    public event Action<bool> OnThinkingStateChanged;
    public event Action<int> OnExpressionChanged;
    
    // 公共属性
    public int ExpressionId { get; }
    public bool IsThinking { get; }
    
    // 公共方法
    public void InitializeConversation(string eventDefName);
    public List<(string role, string message)> GetHistorySnapshot();
    public void SetExpression(int id);  // 供 Tool_ChangeExpression 调用
    public void SendMessage(string text);  // 供小 UI 调用
}
```

**参考代码**：
- 备份文件 `Tools/using System;.cs` 包含完整的 AI 逻辑（1549行）
- 核心方法包括：
  - `RunPhasedRequestAsync()` - 3阶段请求处理
  - `ExecuteXmlToolsForPhase()` - 工具执行
  - `BuildToolContext()` / `BuildReplyHistory()` - 上下文构建
  - `ParseResponse()` - 响应解析
  - `GetSystemInstruction()` / `GetToolSystemInstruction()` - 提示词生成

---

## 关键文件路径

```
C:\Steam\steamapps\common\RimWorld\Mods\3516260226\
├── Tools\
│   └── using System;.cs          # 旧版 Dialog_AIConversation 备份（包含完整 AI 逻辑）
└── Source\WulaFallenEmpire\EventSystem\AI\
    ├── AIIntelligenceCore.cs     # 【需要创建】
    ├── AIHistoryManager.cs       # 历史记录管理
    ├── AIMemoryManager.cs        # 记忆管理
    ├── SimpleAIClient.cs         # API 客户端
    ├── Tools\                    # AI 工具目录
    │   ├── Tool_SpawnResources.cs
    │   ├── Tool_SendReinforcement.cs
    │   └── ... (其他工具)
    └── UI\
        ├── Dialog_AIConversation.cs   # 大 UI（已恢复）
        ├── Overlay_WulaLink.cs        # 小 UI（需要修复引用）
        ├── Overlay_WulaLink_Notification.cs
        └── WulaLinkStyles.cs          # 样式配置（已重写）
```

---

## 编译命令

```powershell
dotnet build C:\Steam\steamapps\common\RimWorld\Mods\3516260226\Source\WulaFallenEmpire\WulaFallenEmpire.csproj
```

---

## 架构说明

### 目标架构
```
┌─────────────────────────────────────┐
│         AIIntelligenceCore          │  ← WorldComponent (核心逻辑)
│  - 历史记录管理                      │
│  - AI 请求处理 (3阶段)               │
│  - 工具执行                          │
│  - 表情/状态管理                     │
└──────────────┬──────────────────────┘
               │ 事件回调
    ┌──────────┴──────────┐
    ▼                     ▼
┌─────────────┐    ┌──────────────┐
│ Dialog_AI   │    │ Overlay_     │
│ Conversation│    │ WulaLink     │
│ (大 UI)     │    │ (小 UI)      │
└─────────────┘    └──────────────┘
```

### 关键点
1. `Dialog_AIConversation` 目前是**自包含**的（既有 UI 也有 AI 逻辑）
2. `Overlay_WulaLink` 需要通过 `AIIntelligenceCore` 获取数据
3. 两个 UI 可以共享同一个 `AIIntelligenceCore` 实例

---

## 注意事项

1. **不要使用 PowerShell Get-Content 读取文件** - 会显示乱码，请使用 `view_file` 工具
2. **备份文件编码正常** - `Tools/using System;.cs` 可以正常读取
3. **命名空间**：`WulaFallenEmpire.EventSystem.AI`
4. **依赖项**：需要引用 `SimpleAIClient`、`AIHistoryManager`、`AITool` 等现有类
