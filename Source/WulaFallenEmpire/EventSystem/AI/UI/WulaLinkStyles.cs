using System;
using UnityEngine;
using Verse;

namespace WulaFallenEmpire.EventSystem.AI.UI
{
    [StaticConstructorOnStartup]
    public static class WulaLinkStyles
    {
        // =================================================================================
        // Colors
        // =================================================================================
        
        // Background Colors - Semi-transparent red theme
        public static readonly Color BackgroundColor = new Color(0.2f, 0.05f, 0.05f, 0.85f);
        public static readonly Color HeaderColor = new Color(0.5f, 0.2f, 0.2f, 1f);
        public static readonly Color InputBarColor = new Color(0.16f, 0.08f, 0.08f, 0.95f);
        public static readonly Color SystemAccentColor = new Color(0.6f, 0.2f, 0.2f, 1f);
        
        // Message Bubble Colors - Matching Dialog_CustomDisplay button style
        public static readonly Color SenseiBubbleColor = new Color(0.5f, 0.2f, 0.2f, 1f);
        public static readonly Color StudentBubbleColor = new Color(0.15f, 0.15f, 0.2f, 0.9f);
        public static readonly Color StudentStrokeColor = new Color(0.6f, 0.3f, 0.3f, 1f);
        
        // Text Colors
        public static readonly Color TextColor = new Color32(220, 220, 220, 255);
        public static readonly Color SenseiTextColor = new Color32(240, 240, 240, 255);
        public static readonly Color StudentTextColor = new Color32(230, 230, 230, 255);
        public static readonly Color InputBorderColor = new Color32(60, 60, 60, 255);
        
        // =================================================================================
        // Fonts
        // =================================================================================
        public static GameFont MessageFont = GameFont.Small;
        public static GameFont HeaderFont = GameFont.Medium;
        
        // =================================================================================
        // Textures
        // =================================================================================
        public static readonly Texture2D TexCircleMask;
        public static readonly Texture2D TexSendIcon;
        public static readonly Texture2D TexPaperClip;
        public static readonly Texture2D TexWhite;
        
        static WulaLinkStyles()
        {
            TexCircleMask = ContentFinder<Texture2D>.Get("Base/UI/WulaLink/CircleMask", false);
            TexSendIcon = ContentFinder<Texture2D>.Get("Base/UI/WulaLink/Send", false);
            TexPaperClip = ContentFinder<Texture2D>.Get("Base/UI/WulaLink/Clip", false);
            TexWhite = SolidColorMaterials.NewSolidColorTexture(Color.white);
        }
    }
}
