using RimWorld;
using System.Collections.Generic;
using UnityEngine;
using Verse;

namespace WulaFallenEmpire
{
    public class CompTransformIntoBuilding : ThingComp
    {
        private CompProperties_TransformIntoBuilding Props => (CompProperties_TransformIntoBuilding)props;
        private Pawn Pawn => (Pawn)parent;
        
        // 恢复数据
        private ThingDef restoreBuildingDef;
        private int restoreMechanoidCount;

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
                if (!CanTransformNow())
                {
                    command.Disable("无法在当前位置转换");
                }

                yield return command;
            }
        }

        private string GetGizmoDescription()
        {
            string desc = Props.gizmoDesc;
            if (restoreBuildingDef != null)
            {
                desc += $"\n\n将恢复为: {restoreBuildingDef.LabelCap}";
            }
            return desc;
        }

        private Texture2D GetGizmoIcon()
        {
            if (!Props.gizmoIconPath.NullOrEmpty())
            {
                return ContentFinder<Texture2D>.Get(Props.gizmoIconPath);
            }
            return TexCommand.Install;
        }

        private bool CanTransformNow()
        {
            if (parent == null || !parent.Spawned)
                return false;

            // 检查空间是否足够
            ThingDef buildingDef = restoreBuildingDef ?? Props.targetBuildingDef;
            if (buildingDef == null)
                return false;

            foreach (IntVec3 cell in GenAdj.CellsOccupiedBy(Pawn.Position, Rot4.North, buildingDef.Size))
            {
                if (!cell.InBounds(Pawn.Map) || !cell.Walkable(Pawn.Map) || cell.GetEdifice(Pawn.Map) != null)
                {
                    return false;
                }
            }

            return true;
        }

        public void TransformToBuilding()
        {
            if (Pawn == null || !Pawn.Spawned)
                return;

            Map map = Pawn.Map;
            IntVec3 position = Pawn.Position;
            Faction faction = Pawn.Faction;

            // 确定要生成的建筑类型
            ThingDef buildingDef = restoreBuildingDef ?? Props.targetBuildingDef;
            if (buildingDef == null)
            {
                Messages.Message("无法确定目标建筑类型", MessageTypeDefOf.RejectInput);
                return;
            }

            // 移除Pawn
            Pawn.DeSpawn(DestroyMode.Vanish);

            // 生成建筑
            Building newBuilding = (Building)GenSpawn.Spawn(buildingDef, position, map, WipeMode.Vanish);
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
            PlayTransformEffects(position, map);
        }

        private void PlayTransformEffects(IntVec3 position, Map map)
        {
            //// 播放转换视觉效果
            //for (int i = 0; i < 3; i++)
            //{
            //    MoteMaker.ThrowSmoke(position.ToVector3Shifted() + new Vector3(0, 0, 0.5f), map, 1.5f);
            //    MoteMaker.ThrowMicroSparks(position.ToVector3Shifted(), map);
            //}
            
            //// 播放音效
            //SoundDefOf.MechClusterDefeated.PlayOneShot(new TargetInfo(position, map));
        }
    }
}
