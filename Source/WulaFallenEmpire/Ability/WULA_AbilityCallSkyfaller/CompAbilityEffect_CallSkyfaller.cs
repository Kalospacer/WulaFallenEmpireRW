using RimWorld;
using Verse;
using UnityEngine;
using System.Collections.Generic;

namespace WulaFallenEmpire
{
    public class CompAbilityEffect_CallSkyfaller : CompAbilityEffect
    {
        public new CompProperties_AbilityCallSkyfaller Props => (CompProperties_AbilityCallSkyfaller)props;
        
        public override void Apply(LocalTargetInfo target, LocalTargetInfo dest)
        {
            base.Apply(target, dest);
            
            if (parent.pawn == null || parent.pawn.Map == null || !target.IsValid)
                return;

            try
            {
                // 创建延时召唤
                CallSkyfallerDelayed(target.Cell);
                
                WulaLog.Debug($"[CallSkyfaller] Scheduled skyfaller at {target.Cell} with {Props.delayTicks} ticks delay");
            }
            catch (System.Exception ex)
            {
                WulaLog.Debug($"[CallSkyfaller] Error calling skyfaller: {ex}");
            }
        }

        private void CallSkyfallerDelayed(IntVec3 targetCell)
        {
            // 使用延时动作来召唤skyfaller
            parent.pawn.Map.GetComponent<MapComponent_SkyfallerDelayed>()?
                .ScheduleSkyfaller(Props.skyfallerDef, targetCell, Props.delayTicks, parent.pawn);
        }

        // 绘制预览效果
        public override void DrawEffectPreview(LocalTargetInfo target)
        {
            base.DrawEffectPreview(target);
            
            if (parent.pawn == null || parent.pawn.Map == null || !target.IsValid)
                return;

            try
            {
                // 绘制圆形预览区域
                DrawCircularPreview(target.Cell);
            }
            catch (System.Exception)
            {
                // 忽略预览绘制错误
            }
        }

        private void DrawCircularPreview(IntVec3 center)
        {
            Map map = parent.pawn.Map;
            
            // 获取圆形区域内的所有单元格
            var previewCells = GenRadial.RadialCellsAround(center, Props.previewRadius, true);
            
            // 绘制预览区域
            foreach (var cell in previewCells)
            {
                if (cell.InBounds(map))
                {
                    GenDraw.DrawFieldEdges(new List<IntVec3> { cell }, Props.previewColor, 0.2f);
                }
            }
            
            // 绘制目标点高亮
            GenDraw.DrawTargetHighlight(center);
        }

        public override string ExtraLabelMouseAttachment(LocalTargetInfo target)
        {
            return $"召唤空投舱: {Props.delayTicks}刻后到达";
        }
    }
}
