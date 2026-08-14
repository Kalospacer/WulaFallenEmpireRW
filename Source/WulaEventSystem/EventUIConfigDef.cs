using System.Collections.Generic;
using UnityEngine;
using Verse;

namespace WulaFallenEmpire
{
    public class EventUIConfigDef : Def
    {
        // ===== 主窗口设置 =====
        public bool showMainWindow = true;
        public Vector2 windowSize = new Vector2(600f, 750f);
        
        // ===== 游戏控制设置 =====
        public bool pauseGameOnOpen = true;  // 新增：窗口打开时是否暂停游戏
        
        // ===== 背景设置 =====
        public bool useDefaultBackground = true;
        public string customBackgroundImagePath;

        // ===== 元素显示控制 =====
        public bool showPortrait = true;
        public bool showLabel = true;
        public bool showCharacterName = true;
        public bool showDescriptions = true;
        public bool showOptions = true;

        // ===== 元素尺寸设置 =====
        public Vector2 portraitSize = new Vector2(600f, 200f);
        public Vector2 labelSize = new Vector2(600f, 30f);
        public Vector2 characterNameSize = new Vector2(600f, 50f);
        public Vector2 descriptionsSize = new Vector2(600f, 200f);
        public Vector2 optionsListSize = new Vector2(600f, 200f);
        public Vector2 optionSize = new Vector2(500f, 30f);

        // ===== 元素间距设置 (x=上间距, y=下间距) =====
        public Vector2 portraitMargins = new Vector2(0f, 20f);
        public Vector2 labelMargins = new Vector2(20f, 20f);
        public Vector2 characterNameMargins = new Vector2(20f, 20f);
        public Vector2 descriptionsMargins = new Vector2(20f, 20f);
        public Vector2 optionsListMargins = new Vector2(20f, 0f);

        // ===== 描述区域内边距 (x=上下间距, y=左右间距) =====
        public Vector2 descriptionsPadding = new Vector2(0f, 0f);

        // ===== 选项列表内边距 (x=左右间距, y=上下间距) =====
        public Vector2 optionsListPadding = new Vector2(50f, 20f);

        // ===== 选项设置 =====
        public float optionSpacing = 10f;

        // ===== 调试和样式设置 =====
        public bool drawBorders = false;
        public bool showDefName = false;
        public GameFont labelFont = GameFont.Small;

        // ===== 计算属性 =====
        public float TotalHeight
        {
            get
            {
                float height = 0f;
                
                if (showPortrait)
                    height += portraitSize.y + portraitMargins.x + portraitMargins.y;
                
                if (showLabel)
                    height += labelSize.y + labelMargins.x + labelMargins.y;
                
                if (showCharacterName)
                    height += characterNameSize.y + characterNameMargins.x + characterNameMargins.y;
                
                if (showDescriptions)
                    height += descriptionsSize.y + descriptionsMargins.x + descriptionsMargins.y;
                
                if (showOptions)
                    height += optionsListSize.y + optionsListMargins.x + optionsListMargins.y;
                
                return height;
            }
        }

        // ===== 辅助方法 =====
        public Rect GetScaledRect(Vector2 originalSize, Rect container, bool centerHorizontal = true)
        {
            float scaleX = container.width / windowSize.x;
            float scaleY = container.height / windowSize.y;
            float scale = Mathf.Min(scaleX, scaleY);
            
            Vector2 scaledSize = new Vector2(originalSize.x * scale, originalSize.y * scale);
            Vector2 position = new Vector2(
                centerHorizontal ? (container.width - scaledSize.x) / 2 : 0,
                0
            );
            
            return new Rect(position.x, position.y, scaledSize.x, scaledSize.y);
        }

        public float GetScaledMargin(float margin, Rect container)
        {
            float scaleY = container.height / windowSize.y;
            return margin * scaleY;
        }

        public Vector2 GetScaledOptionSize(Rect container)
        {
            float scaleX = container.width / windowSize.x;
            float scaleY = container.height / windowSize.y;
            float scale = Mathf.Min(scaleX, scaleY);
            
            return new Vector2(optionSize.x * scale, optionSize.y * scale);
        }

        public float GetScaledOptionSpacing(Rect container)
        {
            float scaleY = container.height / windowSize.y;
            return optionSpacing * scaleY;
        }

        public Vector2 GetScaledOptionsListPadding(Rect container)
        {
            float scaleX = container.width / windowSize.x;
            float scaleY = container.height / windowSize.y;
            
            return new Vector2(
                optionsListPadding.x * scaleX,
                optionsListPadding.y * scaleY
            );
        }

        public Vector2 GetScaledDescriptionsPadding(Rect container)
        {
            float scaleX = container.width / windowSize.x;
            float scaleY = container.height / windowSize.y;
            
            return new Vector2(
                descriptionsPadding.x * scaleX,
                descriptionsPadding.y * scaleY
            );
        }
    }
}
