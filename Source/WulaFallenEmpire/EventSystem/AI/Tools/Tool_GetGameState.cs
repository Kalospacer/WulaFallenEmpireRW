using System;

namespace WulaFallenEmpire.EventSystem.AI.Tools
{
    /// <summary>
    /// 获取当前游戏状态工具 - 让 AI 了解殖民地当前情况
    /// </summary>
    public class Tool_GetGameState : AITool
    {
        public override string Name => "get_game_state";
        
        public override string Description => 
            "获取当前游戏状态的详细报告，包括殖民者状态、资源、建筑进度、威胁等信息。在做出任何操作决策前应先调用此工具了解当前情况。";
        
        public override string UsageSchema => 
            "<get_game_state/>";
        
        public override string Execute(string args)
        {
            try
            {
                var snapshot = Agent.StateObserver.CaptureState();
                
                if (snapshot == null)
                {
                    return "Error: 无法捕获游戏状态，可能没有活动的地图。";
                }
                
                string stateText = snapshot.ToPromptText();
                
                if (string.IsNullOrWhiteSpace(stateText))
                {
                    return "Error: 游戏状态为空。";
                }
                
                return stateText;
            }
            catch (Exception ex)
            {
                WulaLog.Debug($"[Tool_GetGameState] Error: {ex}");
                return $"Error: 获取游戏状态失败 - {ex.Message}";
            }
        }
    }
}
