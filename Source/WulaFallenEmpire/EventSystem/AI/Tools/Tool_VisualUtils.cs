using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
using WulaFallenEmpire.EventSystem.AI.Agent;

namespace WulaFallenEmpire.EventSystem.AI.Tools
{
    public abstract class VisualToolBase : AITool
    {
        protected bool GetFloat(Dictionary<string, string> dict, string key, out float result)
        {
            result = 0f;
            if (dict.TryGetValue(key, out string val) && float.TryParse(val, out result))
                return true;
            return false;
        }

        public abstract override Task<string> ExecuteAsync(string args);
    }

    /// <summary>
    /// 视觉拖拽工具
    /// </summary>
    public class Tool_VisualDrag : VisualToolBase
    {
        public override string Name => "visual_drag";
        public override string Description => "从起始坐标拖拽到结束坐标。适用于框选单位、拖动滑块或地图。";
        public override string UsageSchema => "<visual_drag><start_x>0-1</start_x><start_y>0-1</start_y><end_x>0-1</end_x><end_y>0-1</end_y><duration>秒(默认0.5)</duration></visual_drag>";

        public override Task<string> ExecuteAsync(string args)
        {
            try
            {
                var dict = ParseXmlArgs(args);
                if (!GetFloat(dict, "start_x", out float sx) || !GetFloat(dict, "start_y", out float sy) ||
                    !GetFloat(dict, "end_x", out float ex) || !GetFloat(dict, "end_y", out float ey))
                    return Task.FromResult("Error: 缺少有效的坐标参数 (0-1)");
                
                float duration = 0.5f;
                if (GetFloat(dict, "duration", out float d)) duration = d;

                return Task.FromResult(VisualInteractionTools.MouseDrag(sx, sy, ex, ey, duration));
            }
            catch (Exception ex) { return Task.FromResult($"Error: {ex.Message}"); }
        }
    }

    /// <summary>
    /// 视觉快捷键工具 (通用)
    /// </summary>
    public class Tool_VisualHotkey : VisualToolBase
    {
        public override string Name => "visual_hotkey";
        public override string Description => "在指定位置点击（可选）并按下快捷键。支持组合键如 'ctrl+c', 'alt+f4', 单键如 'enter', 'esc', 'r', 'space'。";
        public override string UsageSchema => "<visual_hotkey><key>快捷键</key><x>可选</x><y>可选</y></visual_hotkey>";

        public override Task<string> ExecuteAsync(string args)
        {
            try
            {
                var dict = ParseXmlArgs(args);
                string key = dict.ContainsKey("key") ? dict["key"] : "";
                if (string.IsNullOrEmpty(key)) return Task.FromResult("Error: 缺少 key 参数");

                // 如果提供了坐标，先点击
                if (GetFloat(dict, "x", out float x) && GetFloat(dict, "y", out float y))
                {
                    return Task.FromResult(VisualInteractionTools.PressHotkey(x, y, key));
                }
                else
                {
                    // 在当前位置直接按键
                    var pos = MouseSimulator.GetCurrentPosition();
                    float propX = Mathf.Clamp01((float)pos.x / Screen.width);
                    float propY = Mathf.Clamp01(1.0f - ((float)pos.y / Screen.height));
                    return Task.FromResult(VisualInteractionTools.PressHotkey(propX, propY, key));
                }
            }
            catch (Exception ex) { return Task.FromResult($"Error: {ex.Message}"); }
        }
    }

    /// <summary>
    /// 视觉等待工具
    /// </summary>
    public class Tool_VisualWait : VisualToolBase
    {
        public override string Name => "visual_wait";
        public override string Description => "等待指定时间。用于等待UI动画或加载。";
        public override string UsageSchema => "<visual_wait><seconds>秒数</seconds></visual_wait>";

        public override Task<string> ExecuteAsync(string args)
        {
            try
            {
                var dict = ParseXmlArgs(args);
                if (!GetFloat(dict, "seconds", out float seconds)) return Task.FromResult("Error: 缺少 seconds 参数");
                return Task.FromResult(VisualInteractionTools.Wait(seconds));
            }
            catch (Exception ex) { return Task.FromResult($"Error: {ex.Message}"); }
        }
    }

    /// <summary>
    /// 视觉删除文本工具
    /// </summary>
    public class Tool_VisualDeleteText : VisualToolBase
    {
        public override string Name => "visual_delete_text";
        public override string Description => "点击指定位置并按 Backspace 删除指定数量的字符。用于清空输入框。";
        public override string UsageSchema => "<visual_delete_text><x>0-1</x><y>0-1</y><count>字符数(默认1)</count></visual_delete_text>";

        public override Task<string> ExecuteAsync(string args)
        {
            try
            {
                var dict = ParseXmlArgs(args);
                if (!GetFloat(dict, "x", out float x) || !GetFloat(dict, "y", out float y))
                    return Task.FromResult("Error: 缺少有效的坐标参数");
                
                int count = 1;
                if (dict.TryGetValue("count", out string cStr) && int.TryParse(cStr, out int c)) count = c;

                return Task.FromResult(VisualInteractionTools.DeleteText(x, y, count));
            }
            catch (Exception ex) { return Task.FromResult($"Error: {ex.Message}"); }
        }
    }
}
