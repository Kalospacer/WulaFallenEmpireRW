using System;
using System.Threading.Tasks;
using UnityEngine;
using WulaFallenEmpire.EventSystem.AI.Agent;

namespace WulaFallenEmpire.EventSystem.AI.Tools
{
    /// <summary>
    /// 视觉点击工具 - 使用 VLM 分析屏幕后模拟鼠标点击
    /// 适用于原版 API 无法直接操作的 mod UI 元素
    /// </summary>
    public class Tool_VisualClick : AITool
    {
        public override string Name => "visual_click";
        
        public override string Description => 
            "在指定的屏幕位置执行鼠标点击。坐标使用比例值 (0-1)，(0,0) 是左上角，(1,1) 是右下角。" +
            "适用于点击无法通过 API 操作的 mod 按钮或 UI 元素。先使用 analyze_screen 获取目标位置分析。";
        
        public override string UsageSchema => 
            "<visual_click><x>0-1之间的X比例</x><y>0-1之间的Y比例</y><right_click>可选，true为右键</right_click></visual_click>";
        
        public override Task<string> ExecuteAsync(string args)
        {
            try
            {
                var argsDict = ParseXmlArgs(args);
                
                // 解析 X 坐标
                if (!argsDict.TryGetValue("x", out string xStr) || !float.TryParse(xStr, out float x))
                {
                    return Task.FromResult("Error: 缺少有效的 x 坐标 (0-1之间的比例值)");
                }
                
                // 解析 Y 坐标
                if (!argsDict.TryGetValue("y", out string yStr) || !float.TryParse(yStr, out float y))
                {
                    return Task.FromResult("Error: 缺少有效的 y 坐标 (0-1之间的比例值)");
                }
                
                // 验证范围
                if (x < 0 || x > 1 || y < 0 || y > 1)
                {
                    return Task.FromResult($"Error: 坐标 ({x}, {y}) 超出范围，必须在 0-1 之间");
                }
                
                // 解析右键选项
                bool rightClick = false;
                if (argsDict.TryGetValue("right_click", out string rightStr))
                {
                    rightClick = rightStr.ToLowerInvariant() == "true" || rightStr == "1";
                }
                
                // 执行点击
                bool success = Agent.MouseSimulator.ClickAtProportional(x, y, rightClick);
                
                if (success)
                {
                    string clickType = rightClick ? "右键" : "左键";
                    int screenX = Mathf.RoundToInt(x * Screen.width);
                    int screenY = Mathf.RoundToInt(y * Screen.height);
                    
                    WulaLog.Debug($"[Tool_VisualClick] {clickType}点击 ({x:F3}, {y:F3}) -> 屏幕 ({screenX}, {screenY})");
                    return Task.FromResult($"Success: 已在屏幕位置 ({screenX}, {screenY}) 执行{clickType}点击");
                }
                else
                {
                    return Task.FromResult("Error: 点击操作失败");
                }
            }
            catch (Exception ex)
            {
                WulaLog.Debug($"[Tool_VisualClick] Error: {ex}");
                return Task.FromResult($"Error: 点击操作失败 - {ex.Message}");
            }
        }
    }
    
    /// <summary>
    /// 视觉输入文本工具 - 在当前焦点位置输入文本
    /// </summary>
    public class Tool_VisualTypeText : AITool
    {
        public override string Name => "visual_type_text";
        
        public override string Description => 
            "在当前焦点位置输入文本。适用于需要文本输入的对话框或输入框。应先用 visual_click 点击输入框获取焦点。";
        
        public override string UsageSchema => 
            "<visual_type_text><text>要输入的文本</text></visual_type_text>";
        
        public override Task<string> ExecuteAsync(string args)
        {
            try
            {
                var argsDict = ParseXmlArgs(args);
                
                if (!argsDict.TryGetValue("text", out string text) || string.IsNullOrEmpty(text))
                {
                    return Task.FromResult("Error: 缺少要输入的文本");
                }
                
                // 获取当前鼠标位置
                var pos = MouseSimulator.GetCurrentPosition();
                
                float propX = Mathf.Clamp01((float)pos.x / Screen.width);
                float propY = Mathf.Clamp01((float)pos.y / Screen.height);
                
                WulaLog.Debug($"[VisualTypeText] Current Pos: ({pos.x}, {pos.y}) -> Proportional: ({propX:F3}, {propY:F3})");
                
                return Task.FromResult(VisualInteractionTools.TypeText(propX, propY, text));
            }
            catch (Exception ex)
            {
                WulaLog.Debug($"[Tool_VisualTypeText] Error: {ex}");
                return Task.FromResult($"Error: 输入文本失败 - {ex.Message}");
            }
        }
    }
    
    /// <summary>
    /// 视觉滚动工具 - 在当前位置滚动鼠标滚轮
    /// </summary>
    public class Tool_VisualScroll : AITool
    {
        public override string Name => "visual_scroll";
        
        public override string Description => 
            "在当前鼠标位置滚动。可选先移动到指定位置再滚动。delta 正数向上滚动，负数向下滚动。";
        
        public override string UsageSchema => 
            "<visual_scroll><delta>滚动量，正数向上负数向下</delta><x>可选，0-1 X坐标</x><y>可选，0-1 Y坐标</y></visual_scroll>";
        
        public override Task<string> ExecuteAsync(string args)
        {
            try
            {
                var argsDict = ParseXmlArgs(args);
                
                if (!argsDict.TryGetValue("delta", out string deltaStr) || !int.TryParse(deltaStr, out int delta))
                {
                    return Task.FromResult("Error: 缺少有效的 delta 值");
                }
                
                // 可选：先移动到指定位置
                if (argsDict.TryGetValue("x", out string xStr) && argsDict.TryGetValue("y", out string yStr))
                {
                    if (float.TryParse(xStr, out float x) && float.TryParse(yStr, out float y))
                    {
                        Agent.MouseSimulator.MoveToProportional(x, y);
                        System.Threading.Thread.Sleep(10);
                    }
                }
                
                Agent.MouseSimulator.Scroll(delta);
                
                string direction = delta > 0 ? "向上" : "向下";
                return Task.FromResult($"Success: 已{direction}滚动 {Math.Abs(delta)} 单位");
            }
            catch (Exception ex)
            {
                WulaLog.Debug($"[Tool_VisualScroll] Error: {ex}");
                return Task.FromResult($"Error: 滚动操作失败 - {ex.Message}");
            }
        }
    }
}
