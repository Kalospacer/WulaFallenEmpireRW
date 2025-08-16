using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;
using Verse;
using Verse.AI;
using Verse.Sound;

namespace WulaFallenEmpire
{
    public class CompProperties_MaintenancePod : CompProperties
    {
        public SoundDef enterSound;
        public SoundDef exitSound;
        public EffecterDef operatingEffecter;
        public int baseDurationTicks = 60000;
        public float ticksPerSeverity = 0f;
        public float powerConsumptionRunning = 250f;
        public float powerConsumptionIdle = 50f;
        public HediffDef hediffToRemove;
        public float componentCostPerSeverity = 1f;
        public int baseComponentCost = 0;
        public float minSeverityToMaintain = 0.75f;
        public float hediffSeverityAfterCycle = 0.01f;

        public CompProperties_MaintenancePod()
        {
            compClass = typeof(CompMaintenancePod);
        }
    }

    [StaticConstructorOnStartup]
    public class CompMaintenancePod : ThingComp, IThingHolder
    {
        // ===================== Fields =====================
        private ThingOwner innerContainer;
        private CompPowerTrader powerComp;
        private CompRefuelable refuelableComp;
        private int ticksRemaining;
        private MaintenancePodState state = MaintenancePodState.Idle;

        private static readonly Texture2D CancelIcon = ContentFinder<Texture2D>.Get("UI/Designators/Cancel");
        private static readonly Texture2D EnterIcon = ContentFinder<Texture2D>.Get("UI/Commands/PodEject");

        // ===================== Properties =====================
        public CompProperties_MaintenancePod Props => (CompProperties_MaintenancePod)props;
        public MaintenancePodState State => state;
        public Pawn Occupant => innerContainer.FirstOrDefault() as Pawn;
        public bool PowerOn => powerComp != null && powerComp.PowerOn;

        public float RequiredComponents(Pawn pawn)
        {
            if (pawn == null || Props.hediffToRemove == null) return Props.baseComponentCost;
            Hediff hediff = pawn.health.hediffSet.GetFirstHediffOfDef(Props.hediffToRemove);
            if (hediff == null) return Props.baseComponentCost;
            return Props.baseComponentCost + (int)(hediff.Severity * Props.componentCostPerSeverity);
        }

        public int RequiredDuration(Pawn pawn)
        {
            if (pawn == null || Props.hediffToRemove == null) return Props.baseDurationTicks;
            Hediff hediff = pawn.health.hediffSet.GetFirstHediffOfDef(Props.hediffToRemove);
            if (hediff == null) return Props.baseDurationTicks;
            return Props.baseDurationTicks + (int)(hediff.Severity * Props.ticksPerSeverity);
        }

        // ===================== Setup =====================
        public CompMaintenancePod()
        {
            innerContainer = new ThingOwner<Thing>(this, false, LookMode.Deep);
        }

        public override void PostSpawnSetup(bool respawningAfterLoad)
        {
            base.PostSpawnSetup(respawningAfterLoad);
            powerComp = parent.TryGetComp<CompPowerTrader>();
            refuelableComp = parent.TryGetComp<CompRefuelable>();
        }

        public override void PostExposeData()
        {
            base.PostExposeData();
            Scribe_Values.Look(ref state, "state", MaintenancePodState.Idle);
            Scribe_Values.Look(ref ticksRemaining, "ticksRemaining", 0);
            Scribe_Deep.Look(ref innerContainer, "innerContainer", this);
        }

        public override void PostDestroy(DestroyMode mode, Map previousMap)
        {
            base.PostDestroy(mode, previousMap);
            // If the pod is deconstructed or destroyed, eject the occupant to prevent deletion.
            if (mode == DestroyMode.Deconstruct || mode == DestroyMode.KillFinalize)
            {
                Log.Warning($"[WulaPodDebug] Pod destroyed (mode: {mode}). Ejecting pawn.");
                EjectPawn();
            }
        }

        public override void PostDeSpawn(Map map, DestroyMode mode = DestroyMode.Vanish)
        {
            base.PostDeSpawn(map, mode);
            // This handles cases like uninstalling where the pod is removed from the map
            // without being "destroyed". We still need to eject the occupant.
            Log.Warning($"[WulaPodDebug] Pod despawned. Ejecting pawn.");
            EjectPawn();
        }


        // ===================== IThingHolder Implementation =====================
        public void GetChildHolders(List<IThingHolder> outChildren)
        {
            ThingOwnerUtility.AppendThingHoldersFromThings(outChildren, GetDirectlyHeldThings());
        }

        public ThingOwner GetDirectlyHeldThings()
        {
            return innerContainer;
        }

        // ===================== Core Logic =====================
        public override void CompTick()
        {
            base.CompTick();
            if (!parent.Spawned) return;

            if (state == MaintenancePodState.Running)
            {
                if (PowerOn)
                {
                    ticksRemaining--;
                    if (ticksRemaining <= 0)
                    {
                        CycleFinished();
                    }
                }
            }

            if (powerComp != null)
            {
                powerComp.PowerOutput = -(state == MaintenancePodState.Running ? Props.powerConsumptionRunning : Props.powerConsumptionIdle);
            }
        }

        public void StartCycle(Pawn pawn)
        {
            Log.Warning($"[WulaPodDebug] StartCycle called for pawn: {pawn.LabelShortCap}");
            float required = RequiredComponents(pawn);
            if (refuelableComp.Fuel < required)
            {
                Log.Error($"[WulaPodDebug] ERROR: Tried to start cycle for {pawn.LabelShort} without enough components.");
                return;
            }

            if (required > 0)
            {
                refuelableComp.ConsumeFuel(required);
            }

            Log.Warning($"[WulaPodDebug] Pawn state before action: holdingOwner is {(pawn.holdingOwner == null ? "NULL" : "NOT NULL")}, Spawned is {pawn.Spawned}");

            // THE ACTUAL FIX: A pawn, whether held or not, must be despawned before being put in a container.
            if (pawn.Spawned)
            {
                Log.Warning($"[WulaPodDebug] Pawn is spawned. Despawning...");
                pawn.DeSpawn(DestroyMode.Vanish);
            }
            Log.Warning($"[WulaPodDebug] Attempting to add/transfer pawn to container.");
            innerContainer.TryAddOrTransfer(pawn);


            state = MaintenancePodState.Running;
            ticksRemaining = RequiredDuration(pawn);
            Log.Warning($"[WulaPodDebug] Cycle started. Ticks remaining: {ticksRemaining}");
        }

        private void CycleFinished()
        {
            Pawn occupant = Occupant;
            Log.Warning($"[WulaPodDebug] CycleFinished. Occupant: {(occupant == null ? "NULL" : occupant.LabelShortCap)}");
            if (occupant == null)
            {
                Log.Error("[WulaPodDebug] ERROR: Maintenance cycle finished, but no one was inside.");
                state = MaintenancePodState.Idle;
                return;
            }

            // 1. Fix the maintenance hediff
            bool maintenanceDone = false;
            if (Props.hediffToRemove != null)
            {
                Hediff hediff = occupant.health.hediffSet.GetFirstHediffOfDef(Props.hediffToRemove);
                if (hediff != null)
                {
                    hediff.Severity = Props.hediffSeverityAfterCycle;
                    Messages.Message("WULA_MaintenanceComplete".Translate(occupant.Named("PAWN")), occupant, MessageTypeDefOf.PositiveEvent);
                    maintenanceDone = true;
                }
            }

            // 2. Heal all other injuries
            int injuriesHealed = 0;
            while (HealthUtility.TryGetWorstHealthCondition(occupant, out var hediffToFix, out var _))
            {
                // Ensure we don't try to "heal" the maintenance hediff itself
                if (hediffToFix.def == Props.hediffToRemove)
                {
                    break;
                }

                HealthUtility.FixWorstHealthCondition(occupant);
                injuriesHealed++;
            }

            if (injuriesHealed > 0)
            {
                Messages.Message("WULA_MaintenanceHealedAllWounds".Translate(occupant.Named("PAWN")), occupant, MessageTypeDefOf.PositiveEvent);
            }
            else if (!maintenanceDone)
            {
                // If nothing was done at all, give a neutral message
                Messages.Message("WULA_MaintenanceNoEffect".Translate(occupant.Named("PAWN")), occupant, MessageTypeDefOf.NeutralEvent);
            }

            EjectPawn();
        }

        public void EjectPawn(bool interrupted = false)
        {
            Pawn occupant = Occupant;
            Log.Warning($"[WulaPodDebug] EjectPawn. Occupant: {(occupant == null ? "NULL" : occupant.LabelShortCap)}");
            if (occupant != null)
            {
                Map mapToUse = parent.Map ?? Find.CurrentMap;
                if (mapToUse == null)
                {
                    // Try to find the map from nearby things
                    mapToUse = GenClosest.ClosestThing_Global(occupant.Position, Gen.YieldSingle(parent), 99999f, (thing) => thing.Map != null)?.Map;
                }
        
                if (mapToUse != null)
                {
                    innerContainer.TryDropAll(parent.InteractionCell, mapToUse, ThingPlaceMode.Near);
                    if (Props.exitSound != null)
                    {
                        SoundStarter.PlayOneShot(Props.exitSound, new TargetInfo(parent.Position, mapToUse));
                    }
                }
                else
                {
                    Log.Warning($"[WulaPodDebug] EjectPawn aborted: No valid map found.");
                    return;
                }
        
                // Additional logic to handle occupant if needed
                if (interrupted)
                {
                    occupant.needs?.mood.thoughts.memories.TryGainMemory(ThoughtDefOf.SoakingWet);
                    occupant.health?.AddHediff(HediffDefOf.BiosculptingSickness);
                }
            }
            innerContainer.Clear();
            state = MaintenancePodState.Idle;
            Log.Warning($"[WulaPodDebug] EjectPawn finished. State set to Idle.");
        }

        // ===================== UI & Gizmos =====================
        public override string CompInspectStringExtra()
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("WULA_MaintenancePod_Status".Translate() + ": " + $"WULA_MaintenancePod_State_{state}".Translate());

            if (state == MaintenancePodState.Running)
            {
                if (Occupant != null)
                {
                    sb.AppendLine("Contains".Translate() + ": " + Occupant.NameShortColored.Resolve());
                }
                sb.AppendLine("TimeLeft".Translate() + ": " + ticksRemaining.ToStringTicksToPeriod());
            }

            if (!PowerOn)
            {
                sb.AppendLine("NoPower".Translate().Colorize(Color.red));
            }

            return sb.ToString().TrimEnd();
        }

        public override IEnumerable<Gizmo> CompGetGizmosExtra()
        {
            foreach (var gizmo in base.CompGetGizmosExtra())
            {
                yield return gizmo;
            }

            if (state == MaintenancePodState.Idle && PowerOn)
            {
                var enterCommand = new Command_Action
                {
                    defaultLabel = "WULA_MaintenancePod_Enter".Translate(),
                    defaultDesc = "WULA_MaintenancePod_EnterDesc".Translate(),
                    icon = EnterIcon,
                    action = () =>
                    {
                        List<FloatMenuOption> options = GetPawnOptions();
                        if (options.Any())
                        {
                            Find.WindowStack.Add(new FloatMenu(options));
                        }
                        else
                        {
                            Messages.Message("WULA_MaintenancePod_NoOneNeeds".Translate(), MessageTypeDefOf.RejectInput);
                        }
                    }
                };
                yield return enterCommand;
            }

            if (state == MaintenancePodState.Running)
            {
                var cancelCommand = new Command_Action
                {
                    defaultLabel = "CommandCancelConstructionLabel".Translate(),
                    defaultDesc = "WULA_MaintenancePod_CancelDesc".Translate(),
                    icon = CancelIcon,
                    action = () =>
                    {
                        EjectPawn();
                        Messages.Message("WULA_MaintenanceCanceled".Translate(), MessageTypeDefOf.NegativeEvent);
                    }
                };
                yield return cancelCommand;
            }

            // DEV GIZMO
            if (DebugSettings.godMode && state == MaintenancePodState.Running)
            {
                var finishCommand = new Command_Action
                {
                    defaultLabel = "DEV: Finish Cycle",
                    action = () =>
                    {
                        Log.Warning("[WulaPodDebug] DEV: Force finishing cycle.");
                        CycleFinished();
                    }
                };
                yield return finishCommand;
            }
        }

        private List<FloatMenuOption> GetPawnOptions()
        {
            List<FloatMenuOption> options = new List<FloatMenuOption>();
            // Now iterates over all pawns on the map, not just colonists.
            foreach (Pawn p in parent.Map.mapPawns.AllPawns.Where(pawn => pawn.def.defName == "WulaSpecies" || pawn.def.defName == "WulaSpeciesReal"))
            {
                if (p.health.hediffSet.HasHediff(Props.hediffToRemove))
                {
                    // If the pawn is downed or not a free colonist, they need to be brought to the pod.
                    if (p.Downed || !p.IsFreeColonist)
                    {
                        float required = RequiredComponents(p);
                        if (refuelableComp.Fuel < required)
                        {
                            options.Add(new FloatMenuOption(p.LabelShortCap + " (" + p.KindLabel + ", " + "WULA_MaintenancePod_NotEnoughComponents".Translate(required.ToString("F0")) + ")", null));
                        }
                        else
                        {
                            // Find colonists who can haul the pawn.
                            var potentialHaulers = parent.Map.mapPawns.FreeColonistsSpawned.Where(colonist =>
                                !colonist.Downed && colonist.CanReserveAndReach(p, PathEndMode.OnCell, Danger.Deadly) && colonist.CanReserveAndReach(parent, PathEndMode.InteractionCell, Danger.Deadly));

                            if (!potentialHaulers.Any())
                            {
                                // If no one can haul, then it's unreachable.
                                options.Add(new FloatMenuOption(p.LabelShortCap + " (" + p.KindLabel + ", " + "CannotReach".Translate() + ")", null));
                            }
                            else
                            {
                                Action action = delegate
                                {
                                    // Create a menu to select which colonist should do the hauling.
                                    var haulerOptions = new List<FloatMenuOption>();
                                    foreach (var hauler in potentialHaulers)
                                    {
                                        haulerOptions.Add(new FloatMenuOption(hauler.LabelCap, delegate
                                        {
                                            var haulJob = JobMaker.MakeJob(JobDefOf_WULA.WULA_HaulToMaintenancePod, p, parent);
                                            haulJob.count = 1;
                                            hauler.jobs.TryTakeOrderedJob(haulJob, JobTag.Misc);
                                        }));
                                    }
                                    Find.WindowStack.Add(new FloatMenu(haulerOptions));
                                };
                                options.Add(new FloatMenuOption(p.LabelShortCap + " (" + p.KindLabel + ")", action));
                            }
                        }
                    }
                    // If the pawn is a free colonist and can walk, they can go on their own.
                    else
                    {
                        if (!p.CanReach(parent, PathEndMode.InteractionCell, Danger.Deadly))
                        {
                            options.Add(new FloatMenuOption(p.LabelShortCap + " (" + "CannotReach".Translate() + ")", null));
                        }
                        else
                        {
                            float required = RequiredComponents(p);
                            if (refuelableComp.Fuel >= required)
                            {
                                options.Add(new FloatMenuOption(p.LabelShortCap, () =>
                                {
                                    Job job = JobMaker.MakeJob(JobDefOf_WULA.WULA_EnterMaintenancePod, parent);
                                    p.jobs.TryTakeOrderedJob(job, JobTag.Misc);
                                }));
                            }
                            else
                            {
                                options.Add(new FloatMenuOption(p.LabelShortCap + " (" + "WULA_MaintenancePod_NotEnoughComponents".Translate(required.ToString("F0")) + ")", null));
                            }
                        }
                    }
                }
            }
            return options;
        }
    }

    public enum MaintenancePodState
    {
        Idle,
        Running,
    }
}