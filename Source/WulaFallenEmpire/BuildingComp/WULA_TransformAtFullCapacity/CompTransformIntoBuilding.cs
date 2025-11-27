using RimWorld;
using System.Collections.Generic;
using UnityEngine;
using Verse;
using System.Text;

namespace WulaFallenEmpire
{
    public class CompTransformIntoBuilding : ThingComp
    {
        private CompProperties_TransformIntoBuilding Props => (CompProperties_TransformIntoBuilding)props;
        private Pawn Pawn => (Pawn)parent;
        
        // 恢复数据
        private ThingDef restoreBuildingDef;
        private int restoreMechanoidCount;

        // 缓存校验结果
        private bool? lastValidationResult = null;
        private string lastValidationReason = null;
        private int lastValidationTick = 0;

        public override void Initialize(CompProperties props)
        {
            base.Initialize(props);
        }

        public override void PostExposeData()
        {
            base.PostExposeData();
            Scribe_Defs.Look(ref restoreBuildingDef, "restoreBuildingDef");
            Scribe_Values.Look(ref restoreMechanoidCount, "restoreMechanoidCount", 0);
        }

        // 设置恢复数据
        public void SetRestoreData(ThingDef buildingDef, int mechanoidCount)
        {
            restoreBuildingDef = buildingDef;
            restoreMechanoidCount = mechanoidCount;
        }

        public override IEnumerable<Gizmo> CompGetGizmosExtra()
        {
            if (parent.Faction == Faction.OfPlayer && Pawn != null)
            {
                Command_Action command = new Command_Action
                {
                    defaultLabel = Props.gizmoLabel,
                    defaultDesc = GetGizmoDescription(),
                    icon = GetGizmoIcon(),
                    action = TransformToBuilding
                };

                // 检查是否可以转换
                string failReason;
                bool canTransform = CanTransformNow(out failReason);
                
                if (!canTransform)
                {
                    command.Disable(failReason);
                }

                yield return command;
            }
        }

        private string GetGizmoDescription()
        {
            StringBuilder sb = new StringBuilder();
            sb.Append(Props.gizmoDesc);

            if (restoreBuildingDef != null)
            {
                sb.AppendLine();
                sb.AppendLine();
                sb.Append($"将恢复为: {restoreBuildingDef.LabelCap}");
                
                if (restoreMechanoidCount > 0)
                {
                    sb.AppendLine();
                    sb.Append($"恢复机械族储存: {restoreMechanoidCount}");
                }
            }

            // 添加空间校验信息
            string failReason;
            bool isValid = CanTransformNow(out failReason);
            
            sb.AppendLine();
            sb.AppendLine();
            if (isValid)
            {
                sb.Append("<color=green>✓ 当前位置可以放置建筑</color>");
            }
            else
            {
                sb.Append($"<color=red>✗ {failReason}</color>");
            }

            return sb.ToString();
        }

        private Texture2D GetGizmoIcon()
        {
            if (!Props.gizmoIconPath.NullOrEmpty())
            {
                return ContentFinder<Texture2D>.Get(Props.gizmoIconPath);
            }
            return TexCommand.Install;
        }

        /// <summary>
        /// 检查是否可以转换（带详细失败原因）
        /// </summary>
        private bool CanTransformNow(out string failReason)
        {
            failReason = null;

            if (parent == null || !parent.Spawned)
            {
                failReason = "单位未生成或已销毁";
                return false;
            }

            // 确定要生成的建筑类型
            ThingDef buildingDef = restoreBuildingDef ?? Props.targetBuildingDef;
            if (buildingDef == null)
            {
                failReason = "无法确定目标建筑类型";
                return false;
            }

            // 使用缓存优化性能（每60 tick检查一次）
            if (lastValidationResult.HasValue && Find.TickManager.TicksGame - lastValidationTick < 60)
            {
                failReason = lastValidationReason;
                return lastValidationResult.Value;
            }

            // 执行完整的空间校验（排除被转换的Pawn本身）
            bool isValid = TransformValidationUtility.CanPlaceBuildingAt(
                buildingDef, 
                Pawn.Position, 
                Pawn.Map, 
                Pawn.Faction, 
                Pawn, // 排除被转换的Pawn本身
                out failReason
            );

            // 更新缓存
            lastValidationResult = isValid;
            lastValidationReason = failReason;
            lastValidationTick = Find.TickManager.TicksGame;

            return isValid;
        }

        /// <summary>
        /// 查找最近的可用位置
        /// </summary>
        private bool TryFindNearbyValidPosition(out IntVec3 validPosition, out string failReason)
        {
            validPosition = IntVec3.Invalid;
            failReason = null;

            ThingDef buildingDef = restoreBuildingDef ?? Props.targetBuildingDef;
            if (buildingDef == null)
            {
                failReason = "无法确定目标建筑类型";
                return false;
            }

            // 在周围搜索可用位置
            for (int radius = 1; radius <= 5; radius++)
            {
                foreach (IntVec3 cell in GenRadial.RadialPatternInRadius(radius))
                {
                    IntVec3 checkPos = Pawn.Position + cell;
                    
                    if (TransformValidationUtility.CanPlaceBuildingAt(buildingDef, checkPos, Pawn.Map, Pawn.Faction, Pawn, out failReason))
                    {
                        validPosition = checkPos;
                        return true;
                    }
                }
            }

            failReason = "周围没有找到合适的放置位置";
            return false;
        }

        public void TransformToBuilding()
        {
            if (Pawn == null || !Pawn.Spawned)
                return;

            Map map = Pawn.Map;
            IntVec3 desiredPosition = Pawn.Position;
            Faction faction = Pawn.Faction;

            // 确定要生成的建筑类型
            ThingDef buildingDef = restoreBuildingDef ?? Props.targetBuildingDef;
            if (buildingDef == null)
            {
                Messages.Message("无法确定目标建筑类型", MessageTypeDefOf.RejectInput);
                return;
            }

            // 最终校验（排除被转换的Pawn本身）
            string failReason;
            if (!TransformValidationUtility.CanPlaceBuildingAt(buildingDef, desiredPosition, map, faction, Pawn, out failReason))
            {
                // 尝试寻找附近的位置
                IntVec3 alternativePosition;
                if (TryFindNearbyValidPosition(out alternativePosition, out failReason))
                {
                    desiredPosition = alternativePosition;
                    Messages.Message($"将在附近位置 {desiredPosition} 部署建筑", MessageTypeDefOf.NeutralEvent);
                }
                else
                {
                    Messages.Message($"无法部署建筑: {failReason}", MessageTypeDefOf.RejectInput);
                    return;
                }
            }

            // 移除Pawn
            Pawn.DeSpawn(DestroyMode.Vanish);

            // 生成建筑
            Building newBuilding = (Building)GenSpawn.Spawn(buildingDef, desiredPosition, map, WipeMode.Vanish);
            newBuilding.SetFaction(faction);

            // 恢复机械族计数
            var recycler = newBuilding as Building_MechanoidRecycler;
            if (recycler != null && restoreMechanoidCount > 0)
            {
                recycler.SetMechanoidCount(restoreMechanoidCount);
            }

            // 添加建筑转换组件
            var transformComp = newBuilding.TryGetComp<CompTransformAtFullCapacity>();
            if (transformComp == null)
            {
                // 动态添加组件
                var compProps = new CompProperties_TransformAtFullCapacity
                {
                    targetPawnKind = Pawn.kindDef
                };
                transformComp = new CompTransformAtFullCapacity();
                transformComp.parent = newBuilding;
                transformComp.props = compProps;
                newBuilding.AllComps.Add(transformComp);
                transformComp.Initialize(compProps);
            }

            // 选中新生成的建筑
            if (Find.Selector.IsSelected(Pawn))
            {
                Find.Selector.Select(newBuilding);
            }

            Messages.Message($"{Pawn.LabelCap} 已部署为 {newBuilding.Label}", MessageTypeDefOf.PositiveEvent);
            
            // 播放转换效果
            PlayTransformEffects(desiredPosition, map);
            
            // 清除缓存
            lastValidationResult = null;
            lastValidationReason = null;
        }

        private void PlayTransformEffects(IntVec3 position, Map map)
        {
            // 播放转换视觉效果
            //for (int i = 0; i < 3; i++)
            //{
            //    MoteMaker.ThrowSmoke(position.ToVector3Shifted() + new Vector3(0, 0, 0.5f), map, 1.5f);
            //    MoteMaker.ThrowMicroSparks(position.ToVector3Shifted(), map);
            //}
        }

        // 每tick更新校验状态（用于实时反馈）
        public override void CompTick()
        {
            base.CompTick();
            
            // 每60 tick清除一次缓存，确保校验结果实时更新
            if (Find.TickManager.TicksGame % 60 == 0)
            {
                lastValidationResult = null;
            }
        }
    }
}
