using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using RimWorld;
using UnityEngine;
using Verse;

namespace WulaFallenEmpire.EventSystem.AI.Agent
{
    /// <summary>
    /// 自主 Agent 循环 - 持续观察游戏并做出决策
    /// 用户只需给出开放式指令如"帮我挖铁"或"帮我玩殖民地"
    /// </summary>
    public class AutonomousAgentLoop : GameComponent
    {
        public static AutonomousAgentLoop Instance { get; private set; }
        
        // Agent 状态
        private bool _isRunning;
        private string _currentObjective;
        private float _lastDecisionTime;
        private int _decisionCount;
        private readonly List<string> _actionHistory = new List<string>();
        
        // 配置
        private const float DecisionIntervalSeconds = 3f; // 每 3 秒决策一次
        private const int MaxActionsPerObjective = 100;
        
        // 事件
        public event Action<string> OnDecisionMade;
        public event Action<string> OnObjectiveComplete;
        public event Action<string> OnError;
        
        public bool IsRunning => _isRunning;
        public string CurrentObjective => _currentObjective;
        public int DecisionCount => _decisionCount;
        
        public AutonomousAgentLoop(Game game)
        {
            Instance = this;
        }
        
        /// <summary>
        /// 开始执行开放式目标
        /// </summary>
        public void StartObjective(string objective)
        {
            if (string.IsNullOrWhiteSpace(objective))
            {
                OnError?.Invoke("目标不能为空");
                return;
            }
            
            _currentObjective = objective;
            _isRunning = true;
            _decisionCount = 0;
            _actionHistory.Clear();
            _lastDecisionTime = Time.realtimeSinceStartup;
            
            WulaLog.Debug($"[AgentLoop] Started objective: {objective}");
            Messages.Message($"AI Agent 开始执行: {objective}", MessageTypeDefOf.NeutralEvent);
            
            // 立即执行第一次决策
            _ = ExecuteDecisionCycleAsync();
        }
        
        /// <summary>
        /// 停止 Agent
        /// </summary>
        public void Stop()
        {
            if (!_isRunning) return;
            
            _isRunning = false;
            WulaLog.Debug($"[AgentLoop] Stopped after {_decisionCount} decisions");
            Messages.Message($"AI Agent 已停止，执行了 {_decisionCount} 次决策", MessageTypeDefOf.NeutralEvent);
        }
        
        public override void GameComponentTick()
        {
            if (!_isRunning) return;
            
            // 检查是否到达决策间隔
            if (Time.realtimeSinceStartup - _lastDecisionTime < DecisionIntervalSeconds) return;
            
            // 检查是否超过最大操作次数
            if (_decisionCount >= MaxActionsPerObjective)
            {
                Messages.Message($"AI Agent: 已达到最大操作次数 ({MaxActionsPerObjective})，暂停执行", MessageTypeDefOf.CautionInput);
                Stop();
                return;
            }
            
            _lastDecisionTime = Time.realtimeSinceStartup;
            
            // 异步执行决策
            _ = ExecuteDecisionCycleAsync();
        }
        
        /// <summary>
        /// 执行一次决策循环: Observe → Think → Act
        /// </summary>
        private async Task ExecuteDecisionCycleAsync()
        {
            try
            {
                // 1. Observe - 收集游戏状态
                var gameState = StateObserver.CaptureState();
                string stateText = gameState.ToPromptText();
                
                // 2. 构建决策提示词
                string prompt = BuildDecisionPrompt(stateText);
                
                // 3. Think - 调用 AI 获取决策
                var settings = WulaFallenEmpireMod.settings;
                if (settings == null || string.IsNullOrEmpty(settings.apiKey))
                {
                    OnError?.Invoke("API Key 未配置");
                    Stop();
                    return;
                }
                
                string apiKey = settings.useGeminiProtocol ? settings.geminiApiKey : settings.apiKey;
                string baseUrl = settings.useGeminiProtocol ? settings.geminiBaseUrl : settings.baseUrl;
                string model = settings.useGeminiProtocol ? settings.geminiModel : settings.model;

                var client = new SimpleAIClient(apiKey, baseUrl, model, settings.useGeminiProtocol);
                
                string decision;
                string base64Image = null;

                // 如果启用了视觉特性，则在决策前截图 (Autonomous Loop 默认认为是开启视觉即全自动，或者我们可以加逻辑判断，但暂时保持 VLM 开启即截图对于 Agent Loop 来说更合理，因为它需要时刻观察)
                // 实际上，Agent Loop 通常需要全视觉，所以我们这里只检查 enableVlmFeatures
                if (settings.enableVlmFeatures)
                {
                    base64Image = ScreenCaptureUtility.CaptureScreenAsBase64();
                    if (settings.showThinkingProcess)
                    {
                        Messages.Message("AI Agent: 正在通过视觉传感器分析实地情况...", MessageTypeDefOf.NeutralEvent);
                    }
                }
                else if (settings.showThinkingProcess)
                {
                    Messages.Message("AI Agent: 正在分析传感器遥测数据...", MessageTypeDefOf.NeutralEvent);
                }

                // 直接调用 GetChatCompletionAsync (它已支持 multimodal 参数)
                var messages = new List<(string role, string message)>
                {
                    ("user", prompt)
                };
                decision = await client.GetChatCompletionAsync(GetAgentSystemPrompt(), messages, 512, 0.3f, base64Image: base64Image);
                
                if (string.IsNullOrEmpty(decision))
                {
                    WulaLog.Debug("[AgentLoop] Empty decision received");
                    return;
                }
                
                _decisionCount++;
                WulaLog.Debug($"[AgentLoop] Decision #{_decisionCount}: {decision.Substring(0, Math.Min(100, decision.Length))}...");
                
                // 4. Act - 执行决策
                ExecuteDecision(decision);
                
                // 5. 记录历史
                _actionHistory.Add($"[{_decisionCount}] {decision.Substring(0, Math.Min(50, decision.Length))}");
                if (_actionHistory.Count > 20)
                {
                    _actionHistory.RemoveAt(0);
                }
                
                OnDecisionMade?.Invoke(decision);
                
                // 6. 检查是否完成目标
                if (decision.Contains("<objective_complete") || decision.Contains("目标已完成"))
                {
                    OnObjectiveComplete?.Invoke(_currentObjective);
                    Stop();
                }
            }
            catch (Exception ex)
            {
                WulaLog.Debug($"[AgentLoop] Error in decision cycle: {ex.Message}");
                OnError?.Invoke(ex.Message);
            }
        }
        
        private string BuildDecisionPrompt(string gameStateText)
        {
            var sb = new StringBuilder();
            
            sb.AppendLine("# 当前任务");
            sb.AppendLine($"**目标**: {_currentObjective}");
            sb.AppendLine($"**已执行决策次数**: {_decisionCount}");
            sb.AppendLine();
            
            sb.AppendLine(gameStateText);
            
            if (_actionHistory.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine("# 最近操作历史");
                foreach (var action in _actionHistory)
                {
                    sb.AppendLine($"- {action}");
                }
            }
            
            sb.AppendLine();
            sb.AppendLine("# 请决定下一步操作");
            sb.AppendLine("分析当前状态，输出一个 XML 工具调用来推进目标。");
            sb.AppendLine("如果目标已完成，输出 <objective_complete/>。");
            sb.AppendLine("如果不需要操作（等待中），输出 <no_action/>。");
            
            return sb.ToString();
        }
        
        private string GetAgentSystemPrompt()
        {
            return @"你是一个自主 RimWorld 游戏 AI Agent。你的任务是独立完成用户给出的开放式目标。

# 核心原则
1. **自主决策**: 不要等待用户指示，主动分析情况并采取行动
2. **循序渐进**: 每次只执行一个操作，观察结果后再决定下一步
3. **问题应对**: 遇到障碍时自己想办法解决
4. **目标导向**: 始终围绕目标推进，避免无关操作

# 可用工具
- get_game_state: 获取详细游戏状态
- designate_mine: <designate_mine><x>X坐标</x><z>Z坐标</z><radius>可选半径</radius></designate_mine> 标记采矿
- draft_pawn: <draft_pawn><pawn_name>名字</pawn_name><draft>true/false</draft></draft_pawn> 征召殖民者
- analyze_screen: <analyze_screen><context>分析目标</context></analyze_screen> 分析屏幕（需要VLM）
- visual_click: <visual_click><x>0-1比例</x><y>0-1比例</y></visual_click> 模拟点击

# 输出格式
直接输出一个 XML 工具调用，不要解释。
如果目标已完成: <objective_complete/>
如果需要等待: <no_action/>

# 注意事项
- 坐标使用游戏内整数坐标，不是屏幕比例
- 优先使用 API 工具（designate_mine 等），视觉工具用于 mod 内容
- 保持简洁高效";
        }
        
        private void ExecuteDecision(string decision)
        {
            // 解析并执行 AI 的决策
            // 从 AIIntelligenceCore 借用工具执行逻辑
            
            var core = AIIntelligenceCore.Instance;
            if (core == null)
            {
                WulaLog.Debug("[AgentLoop] AIIntelligenceCore not available");
                return;
            }
            
            // 提取工具调用并执行
            // 暂时使用简单的正则匹配，实际应整合 AIIntelligenceCore 的解析逻辑
            
            if (decision.Contains("<no_action") || decision.Contains("<objective_complete"))
            {
                // 不需要执行
                return;
            }
            
            // 委托给 AIIntelligenceCore 执行工具
            // TODO: 整合更完善的工具执行逻辑
            WulaLog.Debug($"[AgentLoop] Executing: {decision}");
        }
        
        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref _currentObjective, "agentObjective", "");
            Scribe_Values.Look(ref _isRunning, "agentRunning", false);
            Scribe_Values.Look(ref _decisionCount, "agentDecisionCount", 0);
        }
    }
}
