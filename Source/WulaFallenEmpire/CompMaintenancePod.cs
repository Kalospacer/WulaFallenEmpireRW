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

        public void EjectPawn()
        {
            Pawn occupant = Occupant;
            Log.Warning($"[WulaPodDebug] EjectPawn. Occupant: {(occupant == null ? "NULL" : occupant.LabelShortCap)}");
            if (occupant != null)
            {
                GenPlace.TryPlaceThing(occupant, parent.InteractionCell, parent.Map, ThingPlaceMode.Near);
                if (Props.exitSound != null)
                {
                    SoundStarter.PlayOneShot(Props.exitSound, new TargetInfo(parent.Position, parent.Map));
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
            foreach (Pawn p in parent.Map.mapPawns.FreeColonists.Where(pawn => pawn.def.defName == "WulaSpecies" || pawn.def.defName == "WulaSpeciesReal"))
            {
                if (p.health.hediffSet.HasHediff(Props.hediffToRemove))
                {
                    if (!p.CanReach(parent, PathEndMode.InteractionCell, Danger.Deadly))
                    {
                        options.Add(new FloatMenuOption(p.LabelShortCap + " (" + "CannotReach".Translate() + ")", null));
                    }
                    else if (p.Downed)
                    {
                        float required = RequiredComponents(p);
                        if (refuelableComp.Fuel < required)
                        {
                            options.Add(new FloatMenuOption(p.LabelShortCap + " (" + "Incapacitated".Translate() + ", " + "WULA_MaintenancePod_NotEnoughComponents".Translate(required.ToString("F0")) + ")", null));
                        }
                        else
                        {
                            Action action = delegate
                            {
                                var potentialRescuers = parent.Map.mapPawns.FreeColonistsSpawned.Where(colonist =>
                                    !colonist.Downed && colonist.CanReserveAndReach(p, PathEndMode.OnCell, Danger.Deadly) && colonist.CanReserveAndReach(parent, PathEndMode.InteractionCell, Danger.Deadly));

                                if (!potentialRescuers.Any())
                                {
                                    Messages.Message("WULA_MaintenancePod_NoRescuer".Translate(p.Named("PAWN")), MessageTypeDefOf.RejectInput);
                                    return;
                                }

                                var rescuerOptions = new List<FloatMenuOption>();
                                foreach (var rescuer in potentialRescuers)
                                {
                                    rescuerOptions.Add(new FloatMenuOption(rescuer.LabelCap, delegate
                                    {
                                        var haulJob = JobMaker.MakeJob(JobDefOf_WULA.WULA_HaulToMaintenancePod, p, parent);
                                        haulJob.count = 1;
                                        rescuer.jobs.TryTakeOrderedJob(haulJob, JobTag.Misc);
                                    }));
                                }
                                Find.WindowStack.Add(new FloatMenu(rescuerOptions));
                            };
                            options.Add(new FloatMenuOption(p.LabelShortCap + " (" + "Incapacitated".Translate() + ")", action));
                        }
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
            return options;
        }
    }

    public enum MaintenancePodState
    {
        Idle,
        Running,
    }
}