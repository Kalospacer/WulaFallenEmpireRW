using RimWorld;
using Verse;
using UnityEngine;

namespace WulaFallenEmpire
{
    public class CompAbilityEffect_EnergyLance : CompAbilityEffect_WithDest
    {
        public new CompProperties_AbilityEnergyLance Props => (CompProperties_AbilityEnergyLance)props;

        public override void Apply(LocalTargetInfo target, LocalTargetInfo dest)
        {
            base.Apply(target, dest);
            
            if (parent.pawn == null || parent.pawn.Map == null)
                return;

            try
            {
                // 使用配置的光束类型
                ThingDef lanceDef = Props.energyLanceDef ?? ThingDef.Named("EnergyLance");
                
                // 创建EnergyLance
                EnergyLance.MakeEnergyLance(
                    lanceDef,
                    target.Cell,
                    dest.Cell,
                    parent.pawn.Map,
                    Props.moveDistance,
                    Props.useFixedDistance,
                    Props.durationTicks,
                    parent.pawn
                );
                
                WulaLog.Debug($"[EnergyLance] Started {lanceDef.defName} from {target.Cell} to {dest.Cell}");
            }
            catch (System.Exception ex)
            {
                WulaLog.Debug($"[EnergyLance] Error starting EnergyLance: {ex}");
            }
        }

        // 绘制预览保持不变
        public new void DrawHighlight(LocalTargetInfo target)
        {
            if (selectedTarget.IsValid)
            {
                DrawBeamPathPreview(selectedTarget.Cell, target.Cell);
            }
            else
            {
                GenDraw.DrawTargetHighlight(target);
            }
        }

        private void DrawBeamPathPreview(IntVec3 startCell, IntVec3 endCell)
        {
            Map map = parent.pawn.Map;
            
            Vector3 startPos = startCell.ToVector3();
            Vector3 direction = (endCell.ToVector3() - startPos).normalized;
            Vector3 actualEndPos;
            
            if (Props.useFixedDistance)
            {
                actualEndPos = startPos + direction * Props.moveDistance;
            }
            else
            {
                actualEndPos = endCell.ToVector3();
            }
            
            IntVec3 actualEndCell = new IntVec3(
                Mathf.RoundToInt(actualEndPos.x),
                Mathf.RoundToInt(actualEndPos.y),
                Mathf.RoundToInt(actualEndPos.z)
            );
            
            DrawBeamLine(startCell, actualEndCell);
            GenDraw.DrawTargetHighlight(startCell);
            GenDraw.DrawTargetHighlight(actualEndCell);
            DrawEffectRadiusPreview(startCell);
            DrawEffectRadiusPreview(actualEndCell);
        }

        private void DrawBeamLine(IntVec3 startCell, IntVec3 endCell)
        {
            Vector3 startPos = startCell.ToVector3Shifted();
            Vector3 endPos = endCell.ToVector3Shifted();
            
            GenDraw.DrawLineBetween(startPos, endPos, SimpleColor.Yellow, 0.3f);
        }

        private void DrawEffectRadiusPreview(IntVec3 center)
        {
            Map map = parent.pawn.Map;
            GenDraw.DrawRadiusRing(center, 15f, Color.yellow);
        }

        public override string ExtraLabelMouseAttachment(LocalTargetInfo target)
        {
            if (selectedTarget.IsValid)
            {
                string beamType = Props.energyLanceDef?.label ?? "EnergyLance";
                return $"选择{beamType}方向\n移动距离: {Props.moveDistance}格\n模式: {(Props.useFixedDistance ? "固定距离" : "移动到终点")}";
            }
            else
            {
                return "选择光束起点";
            }
        }

        public override TargetingParameters targetParams => new TargetingParameters
        {
            canTargetLocations = true,
            canTargetPawns = false,
            canTargetBuildings = false,
            canTargetItems = false,
            mapObjectTargetsMustBeAutoAttackable = false
        };
    }
}
