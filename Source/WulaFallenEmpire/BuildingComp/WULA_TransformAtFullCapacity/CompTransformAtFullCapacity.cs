using RimWorld;
using System.Collections.Generic;
using UnityEngine;
using Verse;

namespace WulaFallenEmpire
{
    public class CompTransformAtFullCapacity : ThingComp
    {
        private CompProperties_TransformAtFullCapacity Props => (CompProperties_TransformAtFullCapacity)props;
        
        // 存储转换前的计数，用于恢复
        private int storedCountAtTransform = 0;
        
        public Building_MechanoidRecycler Recycler => parent as Building_MechanoidRecycler;
        public bool IsCooldownActive => Recycler?.IsCooldownActive ?? false;
        public bool IsAtFullCapacity => Recycler?.StoredCount >= Props.requiredCapacity;

        public override void Initialize(CompProperties props)
        {
            base.Initialize(props);
        }

        public override void PostExposeData()
        {
            base.PostExposeData();
            Scribe_Values.Look(ref storedCountAtTransform, "storedCountAtTransform", 0);
        }

        public override IEnumerable<Gizmo> CompGetGizmosExtra()
        {
            if (parent.Faction == Faction.OfPlayer && Recycler != null)
            {
                Command_Action command = new Command_Action
                {
                    defaultLabel = Props.gizmoLabel,
                    defaultDesc = GetGizmoDescription(),
                    icon = GetGizmoIcon(),
                    action = TransformToPawn
                };

                // 禁用条件
                if (IsCooldownActive)
                {
                    command.Disable($"建筑刚部署，需要等待 {Recycler.GetRemainingCooldownHours():F1} 小时后才能转换");
                }
                else if (!IsAtFullCapacity)
                {
                    command.Disable($"需要储存 {Props.requiredCapacity} 个机械族，当前: {Recycler.StoredCount}/{Props.requiredCapacity}");
                }

                yield return command;
            }
        }

        private string GetGizmoDescription()
        {
            string desc = Props.gizmoDesc;
            if (IsCooldownActive)
            {
                desc += $"\n\n冷却时间剩余: {Recycler.GetRemainingCooldownHours():F1} 小时";
            }
            desc += $"\n目标单位: {Props.targetPawnKind.LabelCap}";
            return desc;
        }

        private Texture2D GetGizmoIcon()
        {
            if (!Props.gizmoIconPath.NullOrEmpty())
            {
                return ContentFinder<Texture2D>.Get(Props.gizmoIconPath);
            }
            return TexCommand.ReleaseAnimals;
        }

        public void NotifyStorageUpdated()
        {
            // 当存储更新时，可以触发视觉效果
            if (IsAtFullCapacity && !IsCooldownActive)
            {
                // 播放满容量提示效果
                //MoteMaker.ThrowLightningGlow(parent.DrawPos, parent.Map, 2f);
            }
        }

        public void TransformToPawn()
        {
            if (Recycler == null || !parent.Spawned)
                return;

            Map map = parent.Map;
            IntVec3 position = parent.Position;
            Faction faction = parent.Faction;

            // 存储当前的机械族计数（用于恢复）
            storedCountAtTransform = Recycler.StoredCount;

            // 消耗存储的机械族
            if (!Recycler.ConsumeMechanoids(Props.requiredCapacity))
            {
                Messages.Message("机械族数量不足", MessageTypeDefOf.RejectInput);
                return;
            }

            // 生成目标Pawn
            PawnGenerationRequest request = new PawnGenerationRequest(
                Props.targetPawnKind,
                faction,
                PawnGenerationContext.NonPlayer,
                -1,
                forceGenerateNewPawn: true,
                allowDead: false,
                allowDowned: false,
                canGeneratePawnRelations: false,
                mustBeCapableOfViolence: true
            );

            Pawn newPawn = PawnGenerator.GeneratePawn(request);
            
            // 添加转换组件并设置恢复数据
            var transformComp = newPawn.GetComp<CompTransformIntoBuilding>();
            if (transformComp != null)
            {
                transformComp.SetRestoreData(parent.def, storedCountAtTransform);
            }
            else
            {
                // 动态添加组件
                var compProps = new CompProperties_TransformIntoBuilding
                {
                    targetBuildingDef = parent.def
                };
                transformComp = new CompTransformIntoBuilding();
                transformComp.parent = newPawn;
                transformComp.props = compProps;
                newPawn.AllComps.Add(transformComp);
                transformComp.Initialize(compProps);
                transformComp.SetRestoreData(parent.def, storedCountAtTransform);
            }

            // 移除建筑
            parent.DeSpawn(DestroyMode.Vanish);
            
            // 生成Pawn
            GenSpawn.Spawn(newPawn, position, map, WipeMode.Vanish);
            
            // 选中新生成的Pawn
            if (Find.Selector.IsSelected(parent))
            {
                Find.Selector.Select(newPawn);
            }

            Messages.Message($"{parent.Label} 已转换为 {newPawn.LabelCap}", MessageTypeDefOf.PositiveEvent);
            
            // 播放转换效果
            PlayTransformEffects(position, map);
        }

        private void PlayTransformEffects(IntVec3 position, Map map)
        {
            //// 播放转换视觉效果
            //for (int i = 0; i < 3; i++)
            //{
            //    MoteMaker.ThrowSmoke(position.ToVector3Shifted() + new Vector3(0, 0, 0.5f), map, 1.5f);
            //    MoteMaker.ThrowLightningGlow(position.ToVector3Shifted(), map, 2f);
            //}
            
            //// 播放音效
            //SoundDefOf.PsychicPulseGlobal.PlayOneShot(new TargetInfo(position, map));
        }
    }
}
