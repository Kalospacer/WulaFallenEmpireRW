using RimWorld;
using Verse;
using UnityEngine;

namespace WulaFallenEmpire
{
    public class CompProperties_AbilityCallSkyfaller : CompProperties_AbilityEffect
    {
        // 基础配置
        public int delayTicks = 120;                // 延时（刻）
        public ThingDef skyfallerDef;               // 使用的 Skyfaller
        
        // 预览配置
        public float previewRadius = 5f;            // 预览半径
        public Color previewColor = new Color(1f, 0.5f, 0.1f, 0.3f); // 预览颜色
        
        public CompProperties_AbilityCallSkyfaller()
        {
            this.compClass = typeof(CompAbilityEffect_CallSkyfaller);
        }
    }
}
