using System;
using System.Runtime.InteropServices;
using UnityEngine;

namespace WulaFallenEmpire.EventSystem.AI.Agent
{
    /// <summary>
    /// 模拟鼠标输入工具 - 用于 VLM 视觉操作模式
    /// 支持移动鼠标、点击、拖拽等操作
    /// </summary>
    public static class MouseSimulator
    {
        // Windows API 导入
        [DllImport("user32.dll")]
        private static extern bool SetCursorPos(int X, int Y);
        
        [DllImport("user32.dll")]
        private static extern void mouse_event(uint dwFlags, uint dx, uint dy, uint dwData, int dwExtraInfo);
        
        [DllImport("user32.dll")]
        private static extern bool GetCursorPos(out POINT lpPoint);
        
        [StructLayout(LayoutKind.Sequential)]
        public struct POINT
        {
            public int X;
            public int Y;
        }
        
        // 鼠标事件标志
        private const uint MOUSEEVENTF_LEFTDOWN = 0x0002;
        private const uint MOUSEEVENTF_LEFTUP = 0x0004;
        private const uint MOUSEEVENTF_RIGHTDOWN = 0x0008;
        private const uint MOUSEEVENTF_RIGHTUP = 0x0010;
        private const uint MOUSEEVENTF_WHEEL = 0x0800;
        
        /// <summary>
        /// 将比例坐标 (0-1) 转换为屏幕坐标
        /// </summary>
        public static (int x, int y) ProportionalToScreen(float propX, float propY)
        {
            int screenWidth = Screen.width;
            int screenHeight = Screen.height;
            
            int x = Mathf.Clamp(Mathf.RoundToInt(propX * screenWidth), 0, screenWidth - 1);
            int y = Mathf.Clamp(Mathf.RoundToInt(propY * screenHeight), 0, screenHeight - 1);
            
            return (x, y);
        }
        
        /// <summary>
        /// 移动鼠标到指定屏幕坐标
        /// </summary>
        public static bool MoveTo(int screenX, int screenY)
        {
            try
            {
                // Windows坐标系原点在左上角，与 VLM Agent 使用的坐标 convention 一致
                int windowsY = screenY;
                
                return SetCursorPos(screenX, windowsY);
            }
            catch (Exception ex)
            {
                WulaLog.Debug($"[MouseSimulator] MoveTo failed: {ex.Message}");
                return false;
            }
        }
        
        /// <summary>
        /// 移动鼠标到比例坐标 (0-1)
        /// </summary>
        public static bool MoveToProportional(float propX, float propY)
        {
            var (x, y) = ProportionalToScreen(propX, propY);
            return MoveTo(x, y);
        }
        
        /// <summary>
        /// 在当前位置执行左键点击
        /// </summary>
        public static void LeftClick()
        {
            try
            {
                mouse_event(MOUSEEVENTF_LEFTDOWN, 0, 0, 0, 0);
                mouse_event(MOUSEEVENTF_LEFTUP, 0, 0, 0, 0);
            }
            catch (Exception ex)
            {
                WulaLog.Debug($"[MouseSimulator] LeftClick failed: {ex.Message}");
            }
        }
        
        /// <summary>
        /// 在当前位置执行右键点击
        /// </summary>
        public static void RightClick()
        {
            try
            {
                mouse_event(MOUSEEVENTF_RIGHTDOWN, 0, 0, 0, 0);
                mouse_event(MOUSEEVENTF_RIGHTUP, 0, 0, 0, 0);
            }
            catch (Exception ex)
            {
                WulaLog.Debug($"[MouseSimulator] RightClick failed: {ex.Message}");
            }
        }
        
        /// <summary>
        /// 在指定屏幕坐标执行左键点击
        /// </summary>
        public static bool ClickAt(int screenX, int screenY, bool rightClick = false)
        {
            try
            {
                if (!MoveTo(screenX, screenY))
                {
                    return false;
                }
                
                // 短暂延迟确保鼠标位置更新
                System.Threading.Thread.Sleep(10);
                
                if (rightClick)
                {
                    RightClick();
                }
                else
                {
                    LeftClick();
                }
                
                return true;
            }
            catch (Exception ex)
            {
                WulaLog.Debug($"[MouseSimulator] ClickAt failed: {ex.Message}");
                return false;
            }
        }
        
        /// <summary>
        /// 在比例坐标 (0-1) 执行点击
        /// </summary>
        public static bool ClickAtProportional(float propX, float propY, bool rightClick = false)
        {
            var (x, y) = ProportionalToScreen(propX, propY);
            return ClickAt(x, y, rightClick);
        }
        
        /// <summary>
        /// 滚动鼠标滚轮
        /// </summary>
        public static void Scroll(int delta)
        {
            try
            {
                // delta 为正向上滚动，为负向下滚动
                // Windows 使用 WHEEL_DELTA = 120 作为一个单位
                uint wheelDelta = (uint)(delta * 120);
                mouse_event(MOUSEEVENTF_WHEEL, 0, 0, wheelDelta, 0);
            }
            catch (Exception ex)
            {
                WulaLog.Debug($"[MouseSimulator] Scroll failed: {ex.Message}");
            }
        }
        
        /// <summary>
        /// 执行拖拽操作
        /// </summary>
        public static bool Drag(int startX, int startY, int endX, int endY, int durationMs = 200)
        {
            try
            {
                // 移动到起始位置
                MoveTo(startX, startY);
                System.Threading.Thread.Sleep(20);
                
                // 按下左键
                mouse_event(MOUSEEVENTF_LEFTDOWN, 0, 0, 0, 0);
                
                // 平滑移动到目标位置
                int steps = Math.Max(5, durationMs / 20);
                for (int i = 1; i <= steps; i++)
                {
                    float t = (float)i / steps;
                    int x = Mathf.RoundToInt(Mathf.Lerp(startX, endX, t));
                    int y = Mathf.RoundToInt(Mathf.Lerp(startY, endY, t));
                    MoveTo(x, y);
                    System.Threading.Thread.Sleep(durationMs / steps);
                }
                
                // 释放左键
                mouse_event(MOUSEEVENTF_LEFTUP, 0, 0, 0, 0);
                
                return true;
            }
            catch (Exception ex)
            {
                WulaLog.Debug($"[MouseSimulator] Drag failed: {ex.Message}");
                return false;
            }
        }
        
        /// <summary>
        /// 获取当前鼠标位置
        /// </summary>
        public static (int x, int y) GetCurrentPosition()
        {
            try
            {
                if (GetCursorPos(out POINT point))
                {
                    return (point.X, point.Y);
                }
            }
            catch { }
            return (0, 0);
        }
    }
}
