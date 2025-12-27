using System;
using System.Threading.Tasks;
using UnityEngine;

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
            "适用于点击无法通过 API 操作的 mod 按钮或 UI 元素。先使用 analyze_screen 获取目标位置。";
        
        public override string UsageSchema => 
            "<visual_click><x>0-1之间的X比例</x><y>0-1之间的Y比例</y><right_click>可选，true为右键</right_click></visual_click>";
        
        public override string Execute(string args)
        {
            try
            {
                var argsDict = ParseXmlArgs(args);
                
                // 解析 X 坐标
                if (!argsDict.TryGetValue("x", out string xStr) || !float.TryParse(xStr, out float x))
                {
                    return "Error: 缺少有效的 x 坐标 (0-1之间的比例值)";
                }
                
                // 解析 Y 坐标
                if (!argsDict.TryGetValue("y", out string yStr) || !float.TryParse(yStr, out float y))
                {
                    return "Error: 缺少有效的 y 坐标 (0-1之间的比例值)";
                }
                
                // 验证范围
                if (x < 0 || x > 1 || y < 0 || y > 1)
                {
                    return $"Error: 坐标 ({x}, {y}) 超出范围，必须在 0-1 之间";
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
                    return $"Success: 已在屏幕位置 ({screenX}, {screenY}) 执行{clickType}点击";
                }
                else
                {
                    return "Error: 点击操作失败";
                }
            }
            catch (Exception ex)
            {
                WulaLog.Debug($"[Tool_VisualClick] Error: {ex}");
                return $"Error: 点击操作失败 - {ex.Message}";
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
        
        public override string Execute(string args)
        {
            try
            {
                var argsDict = ParseXmlArgs(args);
                
                if (!argsDict.TryGetValue("text", out string text) || string.IsNullOrEmpty(text))
                {
                    return "Error: 缺少要输入的文本";
                }
                
                // 使用剪贴板方式输入（支持中文）
                GUIUtility.systemCopyBuffer = text;
                
                // 模拟 Ctrl+V 粘贴
                // 注意：这需要额外的键盘模拟实现
                // 暂时返回成功，实际使用时需要完善
                
                WulaLog.Debug($"[Tool_VisualTypeText] 已将文本复制到剪贴板: {text}");
                return $"Success: 已将文本复制到剪贴板。请手动按 Ctrl+V 粘贴，或等待键盘模拟功能完善。";
            }
            catch (Exception ex)
            {
                WulaLog.Debug($"[Tool_VisualTypeText] Error: {ex}");
                return $"Error: 输入文本失败 - {ex.Message}";
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
        
        public override string Execute(string args)
        {
            try
            {
                var argsDict = ParseXmlArgs(args);
                
                if (!argsDict.TryGetValue("delta", out string deltaStr) || !int.TryParse(deltaStr, out int delta))
                {
                    return "Error: 缺少有效的 delta 值";
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
                return $"Success: 已{direction}滚动 {Math.Abs(delta)} 单位";
            }
            catch (Exception ex)
            {
                WulaLog.Debug($"[Tool_VisualScroll] Error: {ex}");
                return $"Error: 滚动操作失败 - {ex.Message}";
            }
        }
    }
}
