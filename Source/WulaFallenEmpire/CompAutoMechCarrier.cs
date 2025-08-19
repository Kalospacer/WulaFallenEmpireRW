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
        private bool isAutoSpawning;

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

        public override void PostSpawnSetup(bool respawningAfterLoad)
        {
            base.PostSpawnSetup(respawningAfterLoad);
            if (!respawningAfterLoad)
            {
                isAutoSpawning = AutoProps.startsAsAutoSpawn;
            }
        }

        public override void PostExposeData()
        {
            base.PostExposeData();
            Scribe_Values.Look(ref isAutoSpawning, "isAutoSpawning", AutoProps.startsAsAutoSpawn);
        }

        private AcceptanceReport CanSpawnNow(PawnKindDef kind)
        {
            if (parent is Pawn pawn && (pawn.IsSelfShutdown() || !pawn.Awake() || pawn.Downed || pawn.Dead || !pawn.Spawned))
                return false;
            if (CooldownTicksRemaining > 0)
                return "CooldownTime".Translate() + " " + CooldownTicksRemaining.ToStringSecondsFromTicks();
            if (!AutoProps.freeProduction && InnerContainer.TotalStackCountOfDef(Props.fixedIngredient) < Props.costPerPawn)
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
                int costLeft = Props.costPerPawn;
                List<Thing> things = new List<Thing>(InnerContainer);
                for (int j = 0; j < things.Count; j++)
                {
                    Thing thing = InnerContainer.Take(things[j], Mathf.Min(things[j].stackCount, costLeft));
                    costLeft -= thing.stackCount;
                    thing.Destroy();
                    if (costLeft <= 0) break;
                }
            }

            CooldownTicksRemaining = Props.cooldownTicks;
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
            if (isAutoSpawning && parent.IsHashIntervalTick(60)) // 每秒检查一次
            {
                if (CooldownTicksRemaining > 0) return;

                foreach (var entry in AutoProps.productionQueue)
                {
                    if (LiveSpawnedPawnsCount(entry.pawnKind) < entry.count)
                    {
                        if (CanSpawnNow(entry.pawnKind).Accepted)
                        {
                            TrySpawnPawn(entry.pawnKind);
                            break; // 每次只生产一个，然后等待下一次冷却
                        }
                    }
                }
            }
        }
        
        public override IEnumerable<Gizmo> CompGetGizmosExtra()
        {
            // 拦截并改造基类的Gizmo
            foreach (var gizmo in base.CompGetGizmosExtra())
            {
                // 通过图标来稳定地识别目标按钮
                if (gizmo is Command_ActionWithCooldown command && command.icon == ContentFinder<Texture2D>.Get("UI/Gizmos/ReleaseWarUrchins"))
                {
                    // 我们只改造这个按钮，其他按钮原样返回
                    var modifiedCommand = new Command_ActionWithCooldown
                    {
                        // 保留冷却进度条的逻辑
                        cooldownPercentGetter = command.cooldownPercentGetter,
                        // 保留原版图标
                        icon = command.icon,
                        // 修改功能为切换自动生产
                        action = () => { isAutoSpawning = !isAutoSpawning; },
                        // 修改标签和描述
                        defaultLabel = "WULA_AutoSpawn_Label".Translate(),
                        defaultDesc = "WULA_AutoSpawn_Desc".Translate()
                    };

                    // 如果自动生产开启，则禁用按钮并显示红叉
                    if (isAutoSpawning)
                    {
                        modifiedCommand.Disable("WULA_AutoSpawn_On_Reason".Translate());
                    }
                    
                    yield return modifiedCommand;
                }
                else
                {
                    // 其他按钮（如开发者按钮）原样返回
                    yield return gizmo;
                }
            }
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