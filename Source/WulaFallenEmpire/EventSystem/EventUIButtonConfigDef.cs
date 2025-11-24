using UnityEngine;
using Verse;

namespace WulaFallenEmpire
{
    public class EventUIButtonConfigDef : Def
    {
        // ===== 基础按钮颜色设置 =====
        public Color normalColor = new Color(0.5f, 0.2f, 0.2f, 1f);
        public Color hoverColor = new Color(0.6f, 0.3f, 0.3f, 1f);
        public Color activeColor = new Color(0.4f, 0.4f, 0.4f, 1f);
        public Color disabledColor = new Color(0.15f, 0.15f, 0.15f, 0.6f);

        // ===== 文本颜色设置 =====
        public Color textNormalColor = new Color(0.9f, 0.9f, 0.9f, 1f);
        public Color textHoverColor = new Color(1f, 1f, 1f, 1f);
        public Color textActiveColor = new Color(1f, 1f, 1f, 1f);
        public Color textDisabledColor = new Color(0.6f, 0.6f, 0.6f, 1f);

        // ===== 边框设置 =====
        public bool drawBorder = true;
        public Color borderColor = new Color(0.6f, 0.2f, 0.2f, 1f);
        public int borderWidth = 1;

        // ===== 效果设置 =====
        public bool showDisabledStrikethrough = true;
        public Color strikethroughColor = new Color(0.6f, 0.6f, 0.6f, 0.8f);
        public float strikethroughWidth = 1f;

        // ===== 圆角设置 =====
        public bool useRoundedCorners = true;
        public float cornerRadius = 5f;

        // ===== 阴影设置 =====
        public bool drawShadow = false;
        public Color shadowColor = new Color(0f, 0f, 0f, 0.5f);
        public Vector2 shadowOffset = new Vector2(2f, 2f);

        // ===== 动画设置 =====
        public bool enableHoverAnimation = false;
        public float hoverScaleFactor = 1.02f;
        public float animationDuration = 0.1f;

        // ===== 字体设置 =====
        public GameFont buttonFont = GameFont.Small;
        public bool useCustomFont = false;
        public string customFontPath;

        // ===== 获取配置实例 =====
        private static EventUIButtonConfigDef config;
        public static EventUIButtonConfigDef Config
        {
            get
            {
                if (config == null)
                {
                    config = DefDatabase<EventUIButtonConfigDef>.GetNamed("Wula_EventUIButtonConfig");
                    if (config == null)
                    {
                        // 创建默认配置
                        config = new EventUIButtonConfigDef();
                    }
                }
                return config;
            }
        }
    }
}
