using System;
using UnityEngine;

namespace WulaFallenEmpire.EventSystem.AI
{
    /// <summary>
    /// Unity 屏幕截取工具类，用于 VLM 视觉分析
    /// </summary>
    public static class ScreenCaptureUtility
    {
        private const int MaxImageSize = 1024; // 限制图片大小以节省 API 费用
        
        /// <summary>
        /// 截取当前屏幕并返回 Base64 编码的 PNG
        /// </summary>
        public static string CaptureScreenAsBase64()
        {
            try
            {
                // 使用 Unity 截屏
                Texture2D screenshot = ScreenCapture.CaptureScreenshotAsTexture();
                if (screenshot == null)
                {
                    WulaLog.Debug("[ScreenCapture] CaptureScreenshotAsTexture returned null");
                    return null;
                }
                
                // 缩放以适配 API 限制
                Texture2D resized = ResizeTexture(screenshot, MaxImageSize);
                
                // 编码为 PNG
                byte[] pngBytes = resized.EncodeToPNG();
                
                // 清理资源
                UnityEngine.Object.Destroy(screenshot);
                if (resized != screenshot)
                {
                    UnityEngine.Object.Destroy(resized);
                }
                
                WulaLog.Debug($"[ScreenCapture] Captured {pngBytes.Length} bytes");
                return Convert.ToBase64String(pngBytes);
            }
            catch (Exception ex)
            {
                WulaLog.Debug($"[ScreenCapture] Failed: {ex.Message}");
                return null;
            }
        }
        
        /// <summary>
        /// 缩放纹理到指定最大尺寸
        /// </summary>
        private static Texture2D ResizeTexture(Texture2D source, int maxSize)
        {
            int width = source.width;
            int height = source.height;
            
            // 计算缩放比例
            if (width <= maxSize && height <= maxSize)
            {
                return source; // 无需缩放
            }
            
            float ratio = (float)maxSize / Mathf.Max(width, height);
            int newWidth = Mathf.RoundToInt(width * ratio);
            int newHeight = Mathf.RoundToInt(height * ratio);
            
            // 创建缩放后的纹理
            RenderTexture rt = RenderTexture.GetTemporary(newWidth, newHeight);
            Graphics.Blit(source, rt);
            
            RenderTexture previous = RenderTexture.active;
            RenderTexture.active = rt;
            
            Texture2D resized = new Texture2D(newWidth, newHeight, TextureFormat.RGB24, false);
            resized.ReadPixels(new Rect(0, 0, newWidth, newHeight), 0, 0);
            resized.Apply();
            
            RenderTexture.active = previous;
            RenderTexture.ReleaseTemporary(rt);
            
            WulaLog.Debug($"[ScreenCapture] Resized from {width}x{height} to {newWidth}x{newHeight}");
            return resized;
        }
    }
}
