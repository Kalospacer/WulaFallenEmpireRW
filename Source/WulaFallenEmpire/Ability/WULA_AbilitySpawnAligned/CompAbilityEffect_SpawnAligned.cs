using RimWorld;
using Verse;

namespace WulaFallenEmpire
{
    public class CompAbilityEffect_SpawnAligned : CompAbilityEffect_Spawn
    {
        public new CompProperties_AbilitySpawnAligned Props => (CompProperties_AbilitySpawnAligned)props;

        public override void Apply(LocalTargetInfo target, LocalTargetInfo dest)
        {
            base.Apply(target, dest);
            
            // 获取刚刚生成的物品
            Thing spawnedThing = target.Cell.GetFirstThing(parent.pawn.Map, Props.thingDef);
            if (spawnedThing != null && Props.alignFaction)
            {
                AlignThingFaction(spawnedThing);
            }
        }

        /// <summary>
        /// 将生成的物品与施法者阵营对齐
        /// </summary>
        private void AlignThingFaction(Thing spawnedThing)
        {
            Faction casterFaction = parent.pawn.Faction;
            
            // 处理生物
            if (spawnedThing is Pawn spawnedPawn)
            {
                AlignPawnFaction(spawnedPawn, casterFaction);
            }
            // 处理建筑
            else if (spawnedThing is Building building)
            {
                AlignBuildingFaction(building, casterFaction);
            }
            // 处理其他有阵营的物品
            else
            {
                AlignThingWithCompsFaction(spawnedThing, casterFaction);
            }
        }

        /// <summary>
        /// 对齐生物阵营
        /// </summary>
        private void AlignPawnFaction(Pawn pawn, Faction casterFaction)
        {
            // 设置生物阵营
            if (pawn.Faction != casterFaction)
            {
                pawn.SetFaction(casterFaction);
            }

            // 如果是野生动物，尝试驯服
            if (pawn.Faction == null && pawn.RaceProps.Animal && casterFaction == Faction.OfPlayer)
            {
                pawn.SetFaction(casterFaction);
            }
        }

        /// <summary>
        /// 对齐建筑阵营
        /// </summary>
        private void AlignThingWithCompsFaction(Thing thing, Faction casterFaction)
        {
            if (thing.Faction != casterFaction)
            {
                thing.SetFaction(casterFaction);
            }
        }

        /// <summary>
        /// 对齐建筑阵营
        /// </summary>
        private void AlignBuildingFaction(Building building, Faction casterFaction)
        {
            if (building.Faction != casterFaction)
            {
                building.SetFaction(casterFaction);
            }
        }

        public override bool Valid(LocalTargetInfo target, bool throwMessages = false)
        {
            // 先调用基类的验证
            if (!base.Valid(target, throwMessages))
            {
                return false;
            }

            // 额外的阵营检查
            if (Props.alignFaction && parent.pawn.Faction == null)
            {
                if (throwMessages)
                {
                    Messages.Message("CannotSpawnAlignedWithoutFaction".Translate(), 
                        parent.pawn, MessageTypeDefOf.RejectInput, false);
                }
                return false;
            }

            return true;
        }
    }
}
