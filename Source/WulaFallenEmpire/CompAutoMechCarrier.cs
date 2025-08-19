using RimWorld;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;
using Verse;
using Verse.AI.Group;

namespace WulaFallenEmpire
{
    public class CompAutoMechCarrier : CompMechCarrier
    {
        #region Reflected Fields
        private static FieldInfo spawnedPawnsField;
        private static FieldInfo cooldownTicksRemainingField;
        private static FieldInfo innerContainerField;
        
        private List<Pawn> SpawnedPawns
        {
            get
            {
                if (spawnedPawnsField == null)
                    spawnedPawnsField = typeof(CompMechCarrier).GetField("spawnedPawns", BindingFlags.NonPublic | BindingFlags.Instance);
                return (List<Pawn>)spawnedPawnsField.GetValue(this);
            }
        }

        private int CooldownTicksRemaining
        {
            get
            {
                if (cooldownTicksRemainingField == null)
                    cooldownTicksRemainingField = typeof(CompMechCarrier).GetField("cooldownTicksRemaining", BindingFlags.NonPublic | BindingFlags.Instance);
                return (int)cooldownTicksRemainingField.GetValue(this);
            }
            set
            {
                if (cooldownTicksRemainingField == null)
                    cooldownTicksRemainingField = typeof(CompMechCarrier).GetField("cooldownTicksRemaining", BindingFlags.NonPublic | BindingFlags.Instance);
                cooldownTicksRemainingField.SetValue(this, value);
            }
        }
        
        private ThingOwner InnerContainer
        {
            get
            {
                if (innerContainerField == null)
                    innerContainerField = typeof(CompMechCarrier).GetField("innerContainer", BindingFlags.NonPublic | BindingFlags.Instance);
                return (ThingOwner)innerContainerField.GetValue(this);
            }
        }
        #endregion

        public CompProperties_AutoMechCarrier AutoProps => (CompProperties_AutoMechCarrier)props;

        private int TotalPawnCapacity => AutoProps.productionQueue.Sum(e => e.count);
        
        private int LiveSpawnedPawnsCount(PawnKindDef kind)
        {
            SpawnedPawns.RemoveAll(p => p == null || p.Destroyed);
            return SpawnedPawns.Count(p => p.kindDef == kind);
        }

        private AcceptanceReport CanSpawnNow(PawnKindDef kind)
        {
            if (parent is Pawn pawn && (pawn.IsSelfShutdown() || !pawn.Awake() || pawn.Downed || pawn.Dead || !pawn.Spawned))
                return false;
            if (CooldownTicksRemaining > 0)
                return "CooldownTime".Translate() + " " + CooldownTicksRemaining.ToStringSecondsFromTicks();
            
            PawnProductionEntry entry = AutoProps.productionQueue.First(e => e.pawnKind == kind);
            int cost = entry.cost ?? Props.costPerPawn;

            if (!AutoProps.freeProduction && InnerContainer.TotalStackCountOfDef(Props.fixedIngredient) < cost)
                return "MechCarrierNotEnoughResources".Translate();
            return true;
        }

        private void TrySpawnPawn(PawnKindDef kind)
        {
            PawnGenerationRequest request = new PawnGenerationRequest(kind, parent.Faction, PawnGenerationContext.NonPlayer, -1, forceGenerateNewPawn: true);
            Pawn pawn = PawnGenerator.GeneratePawn(request);
            GenSpawn.Spawn(pawn, parent.Position, parent.Map);
            SpawnedPawns.Add(pawn);
            
            if (parent is Pawn p && p.GetLord() != null)
                p.GetLord().AddPawn(pawn);

            if (!AutoProps.freeProduction)
            {
                PawnProductionEntry entry = AutoProps.productionQueue.First(e => e.pawnKind == kind);
                int costLeft = entry.cost ?? Props.costPerPawn;
                
                List<Thing> things = new List<Thing>(InnerContainer);
                for (int j = 0; j < things.Count; j++)
                {
                    Thing thing = InnerContainer.Take(things[j], Mathf.Min(things[j].stackCount, costLeft));
                    costLeft -= thing.stackCount;
                    thing.Destroy();
                    if (costLeft <= 0) break;
                }
            }
            
            PawnProductionEntry spawnEntry = AutoProps.productionQueue.First(e => e.pawnKind == kind);
            CooldownTicksRemaining = spawnEntry.cooldownTicks ?? Props.cooldownTicks;

            if (Props.spawnedMechEffecter != null)
                EffecterTrigger(Props.spawnedMechEffecter, Props.attachSpawnedMechEffecter, pawn);
            if (Props.spawnEffecter != null)
                EffecterTrigger(Props.spawnEffecter, Props.attachSpawnedEffecter, parent);
        }

        private void EffecterTrigger(EffecterDef effecterDef, bool attach, Thing target)
        {
            Effecter effecter = new Effecter(effecterDef);
            effecter.Trigger(attach ? ((TargetInfo)target) : new TargetInfo(target.Position, target.Map), TargetInfo.Invalid);
            effecter.Cleanup();
        }

        public override void CompTick()
        {
            base.CompTick();

            if (parent.IsHashIntervalTick(60)) // 每秒检查一次
            {
                // 检查是否有抑制生产的Hediff
                if (AutoProps.disableHediff != null && (parent as Pawn)?.health.hediffSet.HasHediff(AutoProps.disableHediff) == true)
                {
                    return; // 有Hediff，停止生产
                }
                
                // 1. 先检查是否满员
                bool isFull = true;
                foreach (var entry in AutoProps.productionQueue)
                {
                    if (LiveSpawnedPawnsCount(entry.pawnKind) < entry.count)
                    {
                        isFull = false;
                        break;
                    }
                }
                
                if (isFull)
                {
                    return; // 如果已满员，则不进行任何操作，包括冷却计时
                }

                // 2. 如果未满员，才检查冷却时间
                if (CooldownTicksRemaining > 0) return;

                // 3. 寻找空位并生产
                foreach (var entry in AutoProps.productionQueue)
                {
                    if (LiveSpawnedPawnsCount(entry.pawnKind) < entry.count)
                    {
                        if (CanSpawnNow(entry.pawnKind).Accepted)
                        {
                            TrySpawnPawn(entry.pawnKind);
                            break; // 每次只生产一个
                        }
                    }
                }
            }
        }
        
        public override IEnumerable<Gizmo> CompGetGizmosExtra()
        {
            // 移除所有Gizmo逻辑
            return Enumerable.Empty<Gizmo>();
        }

        public override string CompInspectStringExtra()
        {
            SpawnedPawns.RemoveAll(p => p == null || p.Destroyed);
            string text = "Pawns: " + SpawnedPawns.Count + " / " + TotalPawnCapacity;
            
            foreach (var entry in AutoProps.productionQueue)
            {
                text += $"\n- {entry.pawnKind.LabelCap}: {LiveSpawnedPawnsCount(entry.pawnKind)} / {entry.count}";
            }

            if (CooldownTicksRemaining > 0)
            {
                text += "\n" + "CooldownTime".Translate() + ": " + CooldownTicksRemaining.ToStringSecondsFromTicks();
            }

            if (!AutoProps.freeProduction)
            {
                text += "\n" + base.CompInspectStringExtra();
            }
            return text;
        }
    }
}