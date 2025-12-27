using System;
using System.Runtime.InteropServices;
using System.Threading;
using UnityEngine;

namespace WulaFallenEmpire.EventSystem.AI.Agent
{
    /// <summary>
    /// 纯视觉交互工具集 - 仿照 Python VLM Agent
    /// 当没有原生 API 可用时，AI 可以通过这些工具操作任何界面
    /// </summary>
    public static class VisualInteractionTools
    {
        // Windows API
        [DllImport("user32.dll")]
        private static extern bool SetCursorPos(int X, int Y);
        
        [DllImport("user32.dll")]
        private static extern void mouse_event(uint dwFlags, uint dx, uint dy, uint dwData, int dwExtraInfo);
        
        [DllImport("user32.dll")]
        private static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, int dwExtraInfo);
        
        [DllImport("user32.dll")]
        private static extern short VkKeyScan(char ch);
        
        // 鼠标事件标志
        private const uint MOUSEEVENTF_LEFTDOWN = 0x0002;
        private const uint MOUSEEVENTF_LEFTUP = 0x0004;
        private const uint MOUSEEVENTF_RIGHTDOWN = 0x0008;
        private const uint MOUSEEVENTF_RIGHTUP = 0x0010;
        private const uint MOUSEEVENTF_WHEEL = 0x0800;
        
        // 键盘事件标志
        private const uint KEYEVENTF_KEYDOWN = 0x0000;
        private const uint KEYEVENTF_KEYUP = 0x0002;
        
        // 虚拟键码
        private const byte VK_CONTROL = 0x11;
        private const byte VK_SHIFT = 0x10;
        private const byte VK_ALT = 0x12;
        private const byte VK_RETURN = 0x0D;
        private const byte VK_BACK = 0x08;
        private const byte VK_ESCAPE = 0x1B;
        private const byte VK_TAB = 0x09;
        private const byte VK_LWIN = 0x5B;
        private const byte VK_F4 = 0x73;
        
        /// <summary>
        /// 1. 鼠标点击 - 在比例坐标处点击
        /// </summary>
        public static string MouseClick(float x, float y, string button = "left", int clicks = 1)
        {
            try
            {
                int screenX = Mathf.RoundToInt(x * Screen.width);
                int windowsY = Mathf.RoundToInt(y * Screen.height);
                
                SetCursorPos(screenX, windowsY);
                Thread.Sleep(20);
                
                for (int i = 0; i < clicks; i++)
                {
                    if (button == "right")
                    {
                        mouse_event(MOUSEEVENTF_RIGHTDOWN, 0, 0, 0, 0);
                        mouse_event(MOUSEEVENTF_RIGHTUP, 0, 0, 0, 0);
                    }
                    else
                    {
                        mouse_event(MOUSEEVENTF_LEFTDOWN, 0, 0, 0, 0);
                        mouse_event(MOUSEEVENTF_LEFTUP, 0, 0, 0, 0);
                    }
                    if (i < clicks - 1) Thread.Sleep(50);
                }
                
                string buttonText = button == "right" ? "右键" : "左键";
                string clickText = clicks == 2 ? "双击" : "单击";
                return $"Success: 在 ({screenX}, {windowsY}) 处{buttonText}{clickText}";
            }
            catch (Exception ex)
            {
                return $"Error: 点击失败 - {ex.Message}";
            }
        }
        
        /// <summary>
        /// 2. 输入文本 - 在指定位置点击后输入文本（通过剪贴板）
        /// </summary>
        public static string TypeText(float x, float y, string text)
        {
            try
            {
                // 先点击
                MouseClick(x, y);
                Thread.Sleep(100);
                
                // 通过剪贴板输入
                GUIUtility.systemCopyBuffer = text;
                Thread.Sleep(50);
                
                // Ctrl+V 粘贴
                keybd_event(VK_CONTROL, 0, KEYEVENTF_KEYDOWN, 0);
                keybd_event(0x56, 0, KEYEVENTF_KEYDOWN, 0); // V
                keybd_event(0x56, 0, KEYEVENTF_KEYUP, 0);
                keybd_event(VK_CONTROL, 0, KEYEVENTF_KEYUP, 0);
                
                return $"Success: 在 ({x:F3}, {y:F3}) 处输入文本: {text}";
            }
            catch (Exception ex)
            {
                return $"Error: 输入文本失败 - {ex.Message}";
            }
        }
        
        /// <summary>
        /// 3. 滚动窗口 - 在指定位置滚动
        /// </summary>
        public static string ScrollWindow(float x, float y, string direction = "up", int amount = 3)
        {
            try
            {
                int screenX = Mathf.RoundToInt(x * Screen.width);
                int windowsY = Mathf.RoundToInt(y * Screen.height);
                
                SetCursorPos(screenX, windowsY);
                Thread.Sleep(20);
                
                int wheelDelta = (direction == "up" ? 1 : -1) * 120 * amount;
                mouse_event(MOUSEEVENTF_WHEEL, 0, 0, (uint)wheelDelta, 0);
                
                string dir = direction == "up" ? "向上" : "向下";
                return $"Success: 在 ({screenX}, {windowsY}) 处{dir}滚动 {amount} 步";
            }
            catch (Exception ex)
            {
                return $"Error: 滚动失败 - {ex.Message}";
            }
        }
        
        /// <summary>
        /// 4. 鼠标拖拽 - 从起点拖到终点
        /// </summary>
        public static string MouseDrag(float startX, float startY, float endX, float endY, float durationSec = 0.5f)
        {
            try
            {
                int sx = Mathf.RoundToInt(startX * Screen.width);
                int sy = Mathf.RoundToInt(startY * Screen.height);
                int ex = Mathf.RoundToInt(endX * Screen.width);
                int ey = Mathf.RoundToInt(endY * Screen.height);
                
                // 移动到起点
                SetCursorPos(sx, sy);
                Thread.Sleep(50);
                
                // 按下
                mouse_event(MOUSEEVENTF_LEFTDOWN, 0, 0, 0, 0);
                
                // 平滑移动
                int steps = Mathf.Max(5, Mathf.RoundToInt(durationSec * 20));
                int delayMs = Mathf.RoundToInt(durationSec * 1000 / steps);
                
                for (int i = 1; i <= steps; i++)
                {
                    float t = (float)i / steps;
                    int cx = Mathf.RoundToInt(Mathf.Lerp(sx, ex, t));
                    int cy = Mathf.RoundToInt(Mathf.Lerp(sy, ey, t));
                    SetCursorPos(cx, cy);
                    Thread.Sleep(delayMs);
                }
                
                // 释放
                mouse_event(MOUSEEVENTF_LEFTUP, 0, 0, 0, 0);
                
                return $"Success: 从 ({startX:F3}, {startY:F3}) 拖拽到 ({endX:F3}, {endY:F3})";
            }
            catch (Exception ex)
            {
                return $"Error: 拖拽失败 - {ex.Message}";
            }
        }
        
        /// <summary>
        /// 5. 等待 - 暂停指定秒数
        /// </summary>
        public static string Wait(float seconds)
        {
            try
            {
                Thread.Sleep(Mathf.RoundToInt(seconds * 1000));
                return $"Success: 等待了 {seconds} 秒";
            }
            catch (Exception ex)
            {
                return $"Error: 等待失败 - {ex.Message}";
            }
        }
        
        /// <summary>
        /// 6. 按下回车键
        /// </summary>
        public static string PressEnter()
        {
            try
            {
                keybd_event(VK_RETURN, 0, KEYEVENTF_KEYDOWN, 0);
                keybd_event(VK_RETURN, 0, KEYEVENTF_KEYUP, 0);
                return "Success: 按下回车键";
            }
            catch (Exception ex)
            {
                return $"Error: 按键失败 - {ex.Message}";
            }
        }
        
        /// <summary>
        /// 7. 按下 Escape 键
        /// </summary>
        public static string PressEscape()
        {
            try
            {
                keybd_event(VK_ESCAPE, 0, KEYEVENTF_KEYDOWN, 0);
                keybd_event(VK_ESCAPE, 0, KEYEVENTF_KEYUP, 0);
                return "Success: 按下 Escape 键";
            }
            catch (Exception ex)
            {
                return $"Error: 按键失败 - {ex.Message}";
            }
        }
        
        /// <summary>
        /// 8. 删除文本 - 按 Backspace 删除指定数量字符
        /// </summary>
        public static string DeleteText(float x, float y, int count = 1)
        {
            try
            {
                MouseClick(x, y);
                Thread.Sleep(100);
                
                for (int i = 0; i < count; i++)
                {
                    keybd_event(VK_BACK, 0, KEYEVENTF_KEYDOWN, 0);
                    keybd_event(VK_BACK, 0, KEYEVENTF_KEYUP, 0);
                    Thread.Sleep(20);
                }
                
                return $"Success: 删除了 {count} 个字符";
            }
            catch (Exception ex)
            {
                return $"Error: 删除失败 - {ex.Message}";
            }
        }
        
        /// <summary>
        /// 9. 执行快捷键 - 如 Ctrl+C, Alt+F4 等
        /// </summary>
        public static string PressHotkey(float x, float y, string hotkey)
        {
            try
            {
                // 先点击获取焦点
                MouseClick(x, y);
                Thread.Sleep(100);
                
                // 解析快捷键
                var keys = hotkey.ToLowerInvariant().Replace("+", " ").Replace("-", " ").Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                
                // 按下修饰键
                foreach (var key in keys)
                {
                    byte vk = GetVirtualKeyCode(key);
                    if (vk != 0)
                    {
                        keybd_event(vk, 0, KEYEVENTF_KEYDOWN, 0);
                    }
                }
                
                Thread.Sleep(50);
                
                // 释放修饰键（逆序）
                for (int i = keys.Length - 1; i >= 0; i--)
                {
                    byte vk = GetVirtualKeyCode(keys[i]);
                    if (vk != 0)
                    {
                        keybd_event(vk, 0, KEYEVENTF_KEYUP, 0);
                    }
                }
                
                return $"Success: 执行快捷键 {hotkey}";
            }
            catch (Exception ex)
            {
                return $"Error: 快捷键失败 - {ex.Message}";
            }
        }
        
        /// <summary>
        /// 10. 关闭窗口 - Alt+F4
        /// </summary>
        public static string CloseWindow(float x, float y)
        {
            try
            {
                MouseClick(x, y);
                Thread.Sleep(100);
                
                keybd_event(VK_ALT, 0, KEYEVENTF_KEYDOWN, 0);
                keybd_event(VK_F4, 0, KEYEVENTF_KEYDOWN, 0);
                keybd_event(VK_F4, 0, KEYEVENTF_KEYUP, 0);
                keybd_event(VK_ALT, 0, KEYEVENTF_KEYUP, 0);
                
                return "Success: 关闭窗口";
            }
            catch (Exception ex)
            {
                return $"Error: 关闭窗口失败 - {ex.Message}";
            }
        }
        
        private static byte GetVirtualKeyCode(string keyName)
        {
            return keyName.ToLowerInvariant() switch
            {
                "ctrl" or "control" => VK_CONTROL,
                "shift" => VK_SHIFT,
                "alt" => VK_ALT,
                "enter" or "return" => VK_RETURN,
                "esc" or "escape" => VK_ESCAPE,
                "tab" => VK_TAB,
                "backspace" or "back" => VK_BACK,
                "win" or "windows" => VK_LWIN,
                "f4" => VK_F4,
                // 字母键
                "a" => 0x41, "b" => 0x42, "c" => 0x43, "d" => 0x44, "e" => 0x45,
                "f" => 0x46, "g" => 0x47, "h" => 0x48, "i" => 0x49, "j" => 0x4A,
                "k" => 0x4B, "l" => 0x4C, "m" => 0x4D, "n" => 0x4E, "o" => 0x4F,
                "p" => 0x50, "q" => 0x51, "r" => 0x52, "s" => 0x53, "t" => 0x54,
                "u" => 0x55, "v" => 0x56, "w" => 0x57, "x" => 0x58, "y" => 0x59,
                "z" => 0x5A,
                _ => 0
            };
        }
    }
}
