using RimWorld;
using System.Collections.Generic;
using UnityEngine;
using Verse;

namespace WulaFallenEmpire
{
    public class CompTransformAtFullCapacity : ThingComp
    {
        private CompProperties_TransformAtFullCapacity Props => (CompProperties_TransformAtFullCapacity)props;
        
        // 移除存储计数的字段，不再进行数量传递
        
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
            // 移除存储计数的保存
        }

        public override IEnumerable<Gizmo> CompGetGizmosExtra()
        {
            if (parent.Faction == Faction.OfPlayer && Recycler != null)
            {
                Command_Action command = new Command_Action
                {
                    defaultLabel = Props.gizmoLabel.Translate(),
                    defaultDesc = GetGizmoDescription(),
                    icon = GetGizmoIcon(),
                    action = TransformToPawn
                };

                // 禁用条件
                if (IsCooldownActive)
                {
                    command.Disable("WULA_BuildingCooldown".Translate(Recycler.GetRemainingCooldownHours().ToString("F1")));
                }
                else if (!IsAtFullCapacity)
                {
                    command.Disable("WULA_NeedMoreMechs".Translate(Props.requiredCapacity, Recycler.StoredCount, Props.requiredCapacity));
                }

                yield return command;
            }
        }

        private string GetGizmoDescription()
        {
            string desc = Props.gizmoDesc.Translate();
            if (IsCooldownActive)
            {
                desc += "\n\n" + "WULA_CooldownRemaining".Translate(Recycler.GetRemainingCooldownHours().ToString("F1"));
            }
            desc += "\n" + "WULA_TargetUnit".Translate(Props.targetPawnKind.LabelCap);
            desc += "\n" + "WULA_MechsRequired".Translate(Props.requiredCapacity);
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

        public void TransformToPawn()
        {
            if (Recycler == null || !parent.Spawned)
                return;
            Map map = parent.Map;
            IntVec3 position = parent.Position;
            Faction faction = parent.Faction;
            // 记录建筑定义用于后续更新
            ThingDef buildingDef = parent.def;
            // 消耗存储的机械族
            if (!Recycler.ConsumeMechanoids(Props.requiredCapacity))
            {
                Messages.Message("WULA_NotEnoughMechs".Translate(), MessageTypeDefOf.RejectInput);
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

            // 关键修改：传递当前的机械族数量（6个）
            var transformComp = newPawn.GetComp<CompTransformIntoBuilding>();
            if (transformComp != null)
            {
                // 传递建筑定义和机械族数量
                transformComp.SetRestoreData(parent.def, Props.requiredCapacity);
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
                // 传递建筑定义和机械族数量
                transformComp.SetRestoreData(parent.def, Props.requiredCapacity);
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
            Messages.Message("WULA_BuildingTransformedToPawn".Translate(parent.Label, newPawn.LabelCap, Props.requiredCapacity),
                MessageTypeDefOf.PositiveEvent);

            // 播放转换效果
            PlayTransformEffects(position, map);

            Log.Message($"[TransformSystem] Building -> Pawn transformation completed at {position}. Path grid updated.");
        }
    }
}
