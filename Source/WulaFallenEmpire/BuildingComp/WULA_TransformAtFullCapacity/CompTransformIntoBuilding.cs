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
        
        // 恢复数据 - 存储建筑定义和机械族数量
        private ThingDef restoreBuildingDef;
        private int restoreMechCount = 6; // 默认6个，符合你的需求

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
            Scribe_Values.Look(ref restoreMechCount, "restoreMechCount", 6); // 默认6个
        }

        // 设置恢复数据 - 设置建筑定义和机械族数量
        public void SetRestoreData(ThingDef buildingDef, int mechCount = 6)
        {
            restoreBuildingDef = buildingDef;
            restoreMechCount = mechCount;
        }

        public override IEnumerable<Gizmo> CompGetGizmosExtra()
        {
            if (parent.Faction == Faction.OfPlayer && Pawn != null)
            {
                Command_Action command = new Command_Action
                {
                    defaultLabel = Props.gizmoLabel.Translate(),
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
            sb.Append(Props.gizmoDesc.Translate());

            if (restoreBuildingDef != null)
            {
                sb.AppendLine();
                sb.AppendLine();
                sb.Append("WULA_WillRestoreTo".Translate(restoreBuildingDef.LabelCap));
                
                // 显示恢复的机械族数量
                sb.AppendLine();
                sb.Append("WULA_RestoreMechCount".Translate(restoreMechCount));
                
                // 显示目标建筑的最大存储容量
                var recyclerProps = restoreBuildingDef.GetCompProperties<CompProperties_MechanoidRecycler>();
                if (recyclerProps != null)
                {
                    sb.AppendLine();
                    sb.Append("WULA_MaxStorageCapacity".Translate(recyclerProps.maxStorageCapacity));
                }
            }

            // 添加空间校验信息
            string failReason;
            bool isValid = CanTransformNow(out failReason);
            
            sb.AppendLine();
            sb.AppendLine();
            if (isValid)
            {
                sb.Append("WULA_PositionValid".Translate());
            }
            else
            {
                sb.Append("WULA_PositionInvalid".Translate(failReason));
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
                failReason = "WULA_UnitNotSpawned".Translate();
                return false;
            }

            // 确定要生成的建筑类型
            ThingDef buildingDef = restoreBuildingDef ?? Props.targetBuildingDef;
            if (buildingDef == null)
            {
                failReason = "WULA_CannotDetermineBuildingType".Translate();
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
                failReason = "WULA_CannotDetermineBuildingType".Translate();
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

            failReason = "WULA_NoSuitablePositionFound".Translate();
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
                Messages.Message("WULA_CannotDetermineBuildingType".Translate(), MessageTypeDefOf.RejectInput);
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
                    Messages.Message("WULA_DeployingAtNearbyPosition".Translate(desiredPosition), MessageTypeDefOf.NeutralEvent);
                }
                else
                {
                    Messages.Message("WULA_CannotDeployBuilding".Translate(failReason), MessageTypeDefOf.RejectInput);
                    return;
                }
            }

            // 移除Pawn
            Pawn.DeSpawn(DestroyMode.Vanish);

            // 生成建筑
            Building newBuilding = (Building)GenSpawn.Spawn(buildingDef, desiredPosition, map, WipeMode.Vanish);
            newBuilding.SetFaction(faction);

            // 关键修改：恢复机械族数量
            var recycler = newBuilding as Building_MechanoidRecycler;
            if (recycler != null)
            {
                recycler.SetMechanoidCount(restoreMechCount);
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

            Messages.Message("WULA_PawnDeployedAsBuilding".Translate(Pawn.LabelCap, newBuilding.Label, restoreMechCount), 
                MessageTypeDefOf.PositiveEvent);
            
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
