using RimWorld;
using UnityEngine;
using Verse;
using WulaFallenEmpire;

namespace WulaFallenEmpire
{
    public class Need_WulaEnergy : Need
    {
        private NeedDefExtension_Energy ext;

        private NeedDefExtension_Energy Ext
        {
            get
            {
                if (ext == null)
                {
                    if (def == null)
                    {
                        return null;
                    }
                    ext = def.GetModExtension<NeedDefExtension_Energy>();
                }
                return ext;
            }
        }

        private float EnergyFallPerTick
        {
            get
            {
                if (Ext != null)
                {
                    // 从XML读取每天消耗值，并转换为每tick消耗值，并应用StatDef因子
                    return (Ext.fallPerDay / 60000f) * pawn.GetStatValue(WulaStatDefOf.WulaEnergyFallRateFactor);
                }
                // 如果XML中没有定义，则使用一个默认值
                return 2.6666667E-05f;
            }
        }

        public bool IsShutdown => CurLevel <= 0.01f;

        public override int GUIChangeArrow
        {
            get
            {
                if (IsFrozen) return 0;
                return -1;
            }
        }

        public override float MaxLevel
        {
            get
            {
                if (Ext != null)
                {
                    // 应用StatDef偏移量
                    return Ext.maxLevel + pawn.GetStatValue(WulaStatDefOf.WulaEnergyMaxLevelOffset);
                }
                return 1f;
            }
        }

        public Need_WulaEnergy(Pawn pawn) : base(pawn)
        {
        }

        public override void NeedInterval()
        {
            if (!IsFrozen)
            {
                CurLevel -= EnergyFallPerTick * 150f;
            }

            if (IsShutdown)
            {
                HealthUtility.AdjustSeverity(pawn, HediffDef.Named("WULA_Shutdown"), 0.05f);
            }
            else
            {
                Hediff hediff = pawn.health.hediffSet.GetFirstHediffOfDef(HediffDef.Named("WULA_Shutdown"));
                if (hediff != null)
                {
                    pawn.health.RemoveHediff(hediff);
                }
            }
        }

        public override void SetInitialLevel()
        {
            CurLevelPercentage = 1.0f;
        }

        public override string GetTipString()
        {
            return (LabelCap + ": " + CurLevelPercentage.ToStringPercent()).Colorize(ColoredText.TipSectionTitleColor) + "\n" +
                   def.description;
        }

        public override void DrawOnGUI(Rect rect, int maxThresholdMarkers = int.MaxValue, float customMargin = -1f, bool drawArrows = true, bool doTooltip = true, Rect? rectForTooltip = null, bool drawLabel = true)
        {
            if (threshPercents == null)
            {
                threshPercents = new System.Collections.Generic.List<float>();
            }
            threshPercents.Clear();
            base.DrawOnGUI(rect, maxThresholdMarkers, customMargin, drawArrows, doTooltip, rectForTooltip, drawLabel);
        }

        public override void ExposeData()
        {
            base.ExposeData();
        }
    }
}
