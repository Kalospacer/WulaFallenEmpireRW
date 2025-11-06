// CompProperties_AbilityBombardment.cs
using RimWorld;
using Verse;
using UnityEngine;

namespace WulaFallenEmpire
{
    public class CompProperties_AbilityBombardment : CompProperties_AbilityEffect
    {
        // 轰炸区域配置
        public int bombardmentWidth = 5;           // 轰炸区域宽度
        public int bombardmentLength = 8;          // 轰炸区域长度
        
        // 目标选择配置
        public float targetSelectionChance = 0.6f; // 每个格子被选中的概率
        public int minTargetCells = 3;             // 最小目标格子数
        public int maxTargetCells = 15;            // 最大目标格子数
        
        // 时间配置
        public int warmupTicks = 120;              // 前摇时间
        public int rowDelayTicks = 30;             // 每排之间的延迟
        public int impactDelayTicks = 10;          // 单个轰炸的延迟（同一排内）
        
        // 视觉效果配置
        public bool showBombardmentArea = true;    // 是否显示轰炸区域
        public float effecterScale = 1.0f;         // 效果器缩放
        public Color areaPreviewColor = new Color(1f, 0.3f, 0.1f, 0.3f); // 区域预览颜色
        
        // Skyfaller 配置
        public ThingDef skyfallerDef;              // 使用的 Skyfaller
        public ThingDef projectileDef;             // 备用的抛射体定义（如果 skyfaller 不可用）
        
        public CompProperties_AbilityBombardment()
        {
            this.compClass = typeof(CompAbilityEffect_Bombardment);
        }
    }
}
