# AI Letter Auto-Response System - 开发文档

## 概述

这个功能将使 P.I.A AI 能够自动监听游戏内的 Letter（信封通知），并根据内容智能决定是否向玩家发送评论、吐槽、警告或提供帮助建议。

---

## 功能需求

### 核心功能
1. **Mod 设置开关**: 在设置中添加 `启用 AI 自动评论` 开关
2. **Letter 监听**: 拦截所有发送给玩家的 Letter
3. **智能判断**: AI 分析 Letter 内容，决定是否需要回应
4. **自动回复**: 通过现有的 AI 对话系统发送回复

### AI 回应类型
| 类型 | 触发场景示例 | 回应风格 |
|------|-------------|---------|
| 警告 | 袭击通知、疫病爆发 | 紧急提醒，询问是否需要启动防御 |
| 吐槽 | 殖民者精神崩溃、愚蠢死亡 | 幽默/讽刺评论 |
| 建议 | 资源短缺、贸易商到来 | 实用建议 |
| 庆祝 | 任务完成、殖民者加入 | 积极反馈 |
| 沉默 | 常规事件、无关紧要的通知 | 不发送任何回复 |

---

## 技术架构

### 1. 文件结构
```
Source/WulaFallenEmpire/
├── Settings/
│   └── WulaModSettings.cs          # 添加新设置字段
├── EventSystem/
│   └── AI/
│       ├── LetterInterceptor/
│       │   ├── Patch_LetterStack.cs    # Harmony Patch 拦截 Letter
│       │   ├── LetterAnalyzer.cs       # Letter 分析和分类
│       │   └── LetterToPromptConverter.cs  # Letter 转提示词
│       └── AIAutoCommentary.cs         # AI 自动评论逻辑
```

### 2. 关键类设计

#### 2.1 WulaModSettings.cs (修改)
```csharp
public class WulaModSettings : ModSettings
{
    // 现有设置...
    
    // 新增
    public bool enableAIAutoCommentary = false;  // AI 自动评论开关
    public float aiCommentaryChance = 0.7f;      // AI 评论概率 (0-1)
    public bool commentOnNegativeOnly = false;   // 仅评论负面事件
    
    public override void ExposeData()
    {
        base.ExposeData();
        Scribe_Values.Look(ref enableAIAutoCommentary, "enableAIAutoCommentary", false);
        Scribe_Values.Look(ref aiCommentaryChance, "aiCommentaryChance", 0.7f);
        Scribe_Values.Look(ref commentOnNegativeOnly, "commentOnNegativeOnly", false);
    }
}
```

#### 2.2 Patch_LetterStack.cs (新建)
```csharp
using HarmonyLib;
using RimWorld;
using Verse;

namespace WulaFallenEmpire.EventSystem.AI.LetterInterceptor
{
    [HarmonyPatch(typeof(LetterStack), nameof(LetterStack.ReceiveLetter), 
        new Type[] { typeof(Letter), typeof(string) })]
    public static class Patch_LetterStack_ReceiveLetter
    {
        public static void Postfix(Letter let, string debugInfo)
        {
            // 检查设置开关
            if (!WulaModSettings.Instance.enableAIAutoCommentary) return;
            
            // 异步处理，避免阻塞游戏
            AIAutoCommentary.ProcessLetter(let);
        }
    }
}
```

#### 2.3 LetterAnalyzer.cs (新建)
```csharp
namespace WulaFallenEmpire.EventSystem.AI.LetterInterceptor
{
    public enum LetterCategory
    {
        Raid,           // 袭击
        Disease,        // 疾病
        MentalBreak,    // 精神崩溃
        Trade,          // 贸易
        Quest,          // 任务
        Death,          // 死亡
        Recruitment,    // 招募
        Resource,       // 资源
        Weather,        // 天气
        Positive,       // 正面事件
        Negative,       // 负面事件
        Neutral,        // 中性事件
        Unknown         // 未知
    }

    public static class LetterAnalyzer
    {
        public static LetterCategory Categorize(Letter letter)
        {
            // 根据 LetterDef 分类
            var def = letter.def;
            
            if (def == LetterDefOf.ThreatBig || def == LetterDefOf.ThreatSmall)
                return LetterCategory.Raid;
            if (def == LetterDefOf.Death)
                return LetterCategory.Death;
            if (def == LetterDefOf.PositiveEvent)
                return LetterCategory.Positive;
            if (def == LetterDefOf.NegativeEvent)
                return LetterCategory.Negative;
            if (def == LetterDefOf.NeutralEvent)
                return LetterCategory.Neutral;
            
            // 根据内容关键词进一步分类
            string text = letter.text?.ToLower() ?? "";
            if (text.Contains("raid") || text.Contains("袭击") || text.Contains("attack"))
                return LetterCategory.Raid;
            if (text.Contains("disease") || text.Contains("疫病") || text.Contains("plague"))
                return LetterCategory.Disease;
            if (text.Contains("mental") || text.Contains("精神") || text.Contains("break"))
                return LetterCategory.MentalBreak;
            if (text.Contains("trade") || text.Contains("贸易") || text.Contains("商队"))
                return LetterCategory.Trade;
            
            return LetterCategory.Unknown;
        }
        
        public static bool ShouldComment(Letter letter)
        {
            var category = Categorize(letter);
            
            // 始终评论的类型
            switch (category)
            {
                case LetterCategory.Raid:
                case LetterCategory.Death:
                case LetterCategory.MentalBreak:
                case LetterCategory.Disease:
                    return true;
                    
                case LetterCategory.Trade:
                case LetterCategory.Quest:
                case LetterCategory.Positive:
                    return Rand.Chance(WulaModSettings.Instance.aiCommentaryChance);
                    
                case LetterCategory.Neutral:
                case LetterCategory.Unknown:
                    return Rand.Chance(0.3f); // 低概率评论
                    
                default:
                    return false;
            }
        }
    }
}
```

#### 2.4 LetterToPromptConverter.cs (新建)
```csharp
namespace WulaFallenEmpire.EventSystem.AI.LetterInterceptor
{
    public static class LetterToPromptConverter
    {
        public static string Convert(Letter letter, LetterCategory category)
        {
            var sb = new StringBuilder();
            
            sb.AppendLine("[SYSTEM EVENT NOTIFICATION]");
            sb.AppendLine($"Event Type: {category}");
            sb.AppendLine($"Severity: {GetSeverityFromDef(letter.def)}");
            sb.AppendLine($"Title: {letter.label}");
            sb.AppendLine($"Content: {letter.text}");
            sb.AppendLine();
            sb.AppendLine("[INSTRUCTION]");
            sb.AppendLine("You have received a game event notification. Based on the event type and content:");
            sb.AppendLine("- For RAIDS/THREATS: Offer tactical advice or ask if player needs orbital support");
            sb.AppendLine("- For DEATHS: Express condolences or make a sardonic comment if death was avoidable");
            sb.AppendLine("- For MENTAL BREAKS: Comment on the colonist's weakness or offer mood management tips");
            sb.AppendLine("- For TRADE: Suggest useful purchases or sales");
            sb.AppendLine("- For POSITIVE events: Celebrate briefly");
            sb.AppendLine("- For trivial events: You may choose to say nothing (respond with [NO_COMMENT])");
            sb.AppendLine();
            sb.AppendLine("Keep your response brief (1-2 sentences). Match your personality as the Legion AI.");
            sb.AppendLine("If you don't think this event warrants a response, reply with exactly: [NO_COMMENT]");
            
            return sb.ToString();
        }
        
        private static string GetSeverityFromDef(LetterDef def)
        {
            if (def == LetterDefOf.ThreatBig) return "CRITICAL";
            if (def == LetterDefOf.ThreatSmall) return "WARNING";
            if (def == LetterDefOf.Death) return "SERIOUS";
            if (def == LetterDefOf.NegativeEvent) return "MODERATE";
            if (def == LetterDefOf.PositiveEvent) return "GOOD";
            return "INFO";
        }
    }
}
```

#### 2.5 AIAutoCommentary.cs (新建)
```csharp
namespace WulaFallenEmpire.EventSystem.AI
{
    public static class AIAutoCommentary
    {
        private static Queue<Letter> pendingLetters = new Queue<Letter>();
        private static bool isProcessing = false;
        
        public static void ProcessLetter(Letter letter)
        {
            // 检查是否应该评论
            if (!LetterAnalyzer.ShouldComment(letter))
            {
                WulaLog.Debug($"[AI Commentary] Skipping letter: {letter.label}");
                return;
            }
            
            // 加入队列
            pendingLetters.Enqueue(letter);
            
            // 开始处理（如果还没在处理中）
            if (!isProcessing)
            {
                ProcessNextLetter();
            }
        }
        
        private static async void ProcessNextLetter()
        {
            if (pendingLetters.Count == 0)
            {
                isProcessing = false;
                return;
            }
            
            isProcessing = true;
            var letter = pendingLetters.Dequeue();
            
            try
            {
                var category = LetterAnalyzer.Categorize(letter);
                var prompt = LetterToPromptConverter.Convert(letter, category);
                
                // 获取 AI 核心
                var aiCore = Find.World?.GetComponent<AIIntelligenceCore>();
                if (aiCore == null)
                {
                    WulaLog.Debug("[AI Commentary] AIIntelligenceCore not found.");
                    ProcessNextLetter();
                    return;
                }
                
                // 发送到 AI 并等待响应
                string response = await aiCore.SendSystemMessageAsync(prompt);
                
                // 检查是否选择不评论
                if (string.IsNullOrEmpty(response) || response.Contains("[NO_COMMENT]"))
                {
                    WulaLog.Debug($"[AI Commentary] AI chose not to comment on: {letter.label}");
                }
                else
                {
                    // 显示 AI 的评论
                    DisplayAICommentary(response, letter);
                }
            }
            catch (Exception ex)
            {
                WulaLog.Debug($"[AI Commentary] Error processing letter: {ex.Message}");
            }
            
            // 延迟处理下一个，避免刷屏
            await Task.Delay(2000);
            ProcessNextLetter();
        }
        
        private static void DisplayAICommentary(string response, Letter originalLetter)
        {
            // 方式1: 作为小型通知显示在 WulaLink 小 UI
            var overlay = Find.WindowStack.Windows.OfType<Overlay_WulaLink>().FirstOrDefault();
            if (overlay != null)
            {
                overlay.AddAIMessage(response);
            }
            
            // 方式2: 作为 Message 显示在屏幕左上角
            Messages.Message($"[P.I.A]: {response}", MessageTypeDefOf.SilentInput);
        }
    }
}
```

---

## 实现步骤

### 阶段 1: 基础设施 (预计 1 小时)
1. [ ] 在 `WulaModSettings.cs` 添加新设置字段
2. [ ] 在设置 UI 中添加开关
3. [ ] 添加对应的 Keyed 翻译

### 阶段 2: Letter 拦截 (预计 30 分钟)
1. [ ] 创建 `Patch_LetterStack.cs` Harmony Patch
2. [ ] 确保 Patch 正确注册到 Harmony 实例
3. [ ] 测试 Letter 拦截是否正常工作

### 阶段 3: Letter 分析 (预计 1 小时)
1. [ ] 创建 `LetterAnalyzer.cs` 分类逻辑
2. [ ] 创建 `LetterToPromptConverter.cs` 转换逻辑
3. [ ] 测试不同类型 Letter 的分类准确性

### 阶段 4: AI 集成 (预计 1.5 小时)
1. [ ] 创建 `AIAutoCommentary.cs` 管理类
2. [ ] 集成到现有的 `AIIntelligenceCore` 系统
3. [ ] 实现队列处理避免刷屏
4. [ ] 添加 `SendSystemMessageAsync` 方法到 AIIntelligenceCore

### 阶段 5: UI 显示 (预计 30 分钟)
1. [ ] 决定评论显示方式（WulaLink UI / Message / 独立通知）
2. [ ] 实现显示逻辑
3. [ ] 测试显示效果

### 阶段 6: 测试与优化 (预计 1 小时)
1. [ ] 测试各类 Letter 的评论效果
2. [ ] 调整评论概率和过滤规则
3. [ ] 优化提示词以获得更好的 AI 回应
4. [ ] 添加速率限制避免 API 过载

---

## 需要添加的翻译键

```xml
<!-- AI Auto Commentary Settings -->
<Wula_AISettings_AutoCommentary>启用 AI 自动评论</Wula_AISettings_AutoCommentary>
<Wula_AISettings_AutoCommentaryDesc>开启后，P.I.A 会自动对游戏事件（袭击、死亡、贸易等）发表评论或提供建议。</Wula_AISettings_AutoCommentaryDesc>
<Wula_AISettings_CommentaryChance>评论概率</Wula_AISettings_CommentaryChance>
<Wula_AISettings_CommentaryChanceDesc>AI 对中性事件发表评论的概率。负面事件（如袭击）总是会评论。</Wula_AISettings_CommentaryChanceDesc>
<Wula_AISettings_NegativeOnly>仅评论负面事件</Wula_AISettings_NegativeOnly>
<Wula_AISettings_NegativeOnlyDesc>开启后，AI 只会对负面事件（袭击、死亡、疾病等）发表评论。</Wula_AISettings_NegativeOnlyDesc>
```

---

## 注意事项

1. **API 限流**: 需要实现请求队列和速率限制，避免短时间内发送过多请求
2. **异步处理**: 所有 AI 请求必须异步处理，避免阻塞游戏主线程
3. **用户控制**: 提供足够的设置选项让用户控制评论频率和类型
4. **优雅降级**: 如果 AI 服务不可用，静默失败而不影响游戏
5. **内存管理**: 队列大小限制，避免积累过多未处理的 Letter

---

## 预期效果示例

**场景 1: 袭击通知**
```
[Letter] 海盗袭击！一群海盗正在向你的殖民地进发。
[P.I.A] 检测到敌对势力入侵。需要我启动轨道监视协议吗？
```

**场景 2: 殖民者死亡**
```
[Letter] 张三死了。他被一只疯狂的松鼠咬死了。
[P.I.A] ...被松鼠咬死？这位殖民者的战斗技能令人印象深刻。
```

**场景 3: 贸易商到来**
```
[Letter] 商队到来。一个来自外部势力的商队想要与你交易。
[P.I.A] 贸易商队抵达。我注意到你的钢铁储备较低，建议优先采购。
```

---

## 依赖项

- Harmony 2.0+ (用于 Patch)
- 现有的 AIIntelligenceCore 系统
- 现有的 WulaModSettings 系统
- 现有的 Overlay_WulaLink UI

---

*文档版本: 1.0*
*创建时间: 2025-12-28*
