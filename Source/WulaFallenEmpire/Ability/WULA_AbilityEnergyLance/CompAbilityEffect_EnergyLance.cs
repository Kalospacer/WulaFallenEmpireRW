using RimWorld;
using Verse;
using UnityEngine;

namespace WulaFallenEmpire
{
    public class CompAbilityEffect_EnergyLance : CompAbilityEffect_WithDest
    {
        public new CompProperties_EnergyLance Props => (CompProperties_EnergyLance)props;
        public override void Apply(LocalTargetInfo target, LocalTargetInfo dest)
        {
            base.Apply(target, dest);

            // 计算光束的起点和方向
            IntVec3 startPos = target.Cell;
            IntVec3 endPos = dest.Cell;

            // 如果使用固定距离，则从起点向终点方向移动固定距离
            if (Props.useFixedDistance)
            {
                Vector3 direction = (endPos.ToVector3() - startPos.ToVector3()).normalized;
                Vector3 offset = direction * Props.moveDistance;
                endPos = startPos + new IntVec3(Mathf.RoundToInt(offset.x), 0, Mathf.RoundToInt(offset.z));
            }

            // 创建移动的能量光束
            EnergyLance obj = (EnergyLance)GenSpawn.Spawn(ThingDef.Named("EnergyLance"), startPos, parent.pawn.Map);
            obj.duration = Props.durationTicks;
            obj.instigator = parent.pawn;
            obj.startPos = startPos;
            obj.endPos = endPos;
            obj.moveDistance = Props.moveDistance;
            obj.useFixedDistance = Props.useFixedDistance;
            obj.firesPerTick = Props.firesPerTick;
            // 不再需要传递伤害范围，因为现在从ModExtension读取
            obj.StartStrike();

            Log.Message($"[EnergyLance] Created energy lance from {startPos} to {endPos}, distance: {Props.moveDistance}");
        }

        // 绘制预览效果
        public override void DrawEffectPreview(LocalTargetInfo target)
        {
            base.DrawEffectPreview(target);
            
            if (parent.pawn == null || parent.pawn.Map == null || !target.IsValid)
                return;

            try
            {
                // 绘制起点预览
                GenDraw.DrawTargetHighlight(target.Cell);
                
                // 如果选择了终点，绘制移动路径预览
                if (selectedTarget.IsValid)
                {
                    DrawMovePathPreview(target.Cell, selectedTarget.Cell);
                }
            }
            catch (System.Exception)
            {
                // 忽略预览绘制错误
            }
        }

        private void DrawMovePathPreview(IntVec3 startPos, IntVec3 endPos)
        {
            Map map = parent.pawn.Map;
            
            // 计算实际终点
            IntVec3 actualEndPos = endPos;
            if (Props.useFixedDistance)
            {
                Vector3 direction = (endPos.ToVector3() - startPos.ToVector3()).normalized;
                Vector3 offset = direction * Props.moveDistance;
                actualEndPos = startPos + new IntVec3(Mathf.RoundToInt(offset.x), 0, Mathf.RoundToInt(offset.z));
            }
            
            // 绘制移动路径
            Vector3 startVec = startPos.ToVector3Shifted();
            Vector3 endVec = actualEndPos.ToVector3Shifted();
            
            GenDraw.DrawLineBetween(startVec, endVec, SimpleColor.Yellow, 0.2f);
            
            // 绘制终点预览
            GenDraw.DrawTargetHighlight(actualEndPos);
            
            // 绘制作用范围预览（在移动路径上）
            DrawEffectRangePreview(startPos, actualEndPos);
        }

        private void DrawEffectRangePreview(IntVec3 startPos, IntVec3 endPos)
        {
            Map map = parent.pawn.Map;
            
            // 沿着移动路径绘制作用范围
            Vector3 currentPos = startPos.ToVector3();
            Vector3 direction = (endPos.ToVector3() - startPos.ToVector3()).normalized;
            float totalDistance = Vector3.Distance(startPos.ToVector3(), endPos.ToVector3());
            float step = 1f; // 每格绘制
            
            for (float distance = 0; distance <= totalDistance; distance += step)
            {
                Vector3 checkPos = startPos.ToVector3() + direction * distance;
                IntVec3 checkCell = new IntVec3(Mathf.RoundToInt(checkPos.x), 0, Mathf.RoundToInt(checkPos.z));
                
                if (checkCell.InBounds(map))
                {
                    // 绘制作用范围指示
                    GenDraw.DrawRadiusRing(checkCell, 1.5f, Color.red, (c) => true);
                }
            }
        }

        public override string ExtraLabelMouseAttachment(LocalTargetInfo target)
        {
            string baseInfo = $"能量长矛: 持续{Props.durationTicks}刻";
            
            if (Props.useFixedDistance)
            {
                baseInfo += $"\n移动距离: {Props.moveDistance}格";
            }
            
            baseInfo += $"\n选择起点后，再选择移动方向";
            
            return baseInfo;
        }
    }
}
