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
    // Properties for our custom maintenance pod
    public class CompProperties_MaintenancePod : CompProperties
    {
        public SoundDef enterSound;
        public SoundDef exitSound;
        public EffecterDef operatingEffecter;
        public int durationTicks = 60000; // Default to 1 day
        public float powerConsumptionRunning = 250f;
        public float powerConsumptionIdle = 50f;
        public HediffDef hediffToRemove;
        public ThingDef componentDef;
        public float componentCostPerSeverity = 1f; // How many components per 1.0 severity
        public int baseComponentCost = 0; // A flat cost in addition to severity cost
        public float minSeverityToMaintain = 0.75f; // The hediff severity required to trigger maintenance

        public CompProperties_MaintenancePod()
        {
            compClass = typeof(CompMaintenancePod);
        }
    }

    [StaticConstructorOnStartup]
    public class CompMaintenancePod : ThingComp, IThingHolder, IStoreSettingsParent
    {
        // ===================== Fields =====================
        private ThingOwner innerContainer;
        private CompPowerTrader powerComp;
        private StorageSettings allowedComponentSettings;
        public float storedComponents = 0f;
        public int capacity = 50; // Let's define a capacity
        private int ticksRemaining;
        private MaintenancePodState state = MaintenancePodState.Idle;

        private static readonly Texture2D CancelIcon = ContentFinder<Texture2D>.Get("UI/Designators/Cancel");
        private static readonly Texture2D EnterIcon = ContentFinder<Texture2D>.Get("UI/Commands/PodEject"); // Re-using an icon

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

        public bool CanAcceptComponents(int count) => storedComponents + count <= capacity;

        // ===================== Setup =====================
        public CompMaintenancePod()
        {
            innerContainer = new ThingOwner<Thing>(this, false, LookMode.Deep);
        }


        public override void Initialize(CompProperties props)
        {
            base.Initialize(props);
            allowedComponentSettings = new StorageSettings(this);
            if (Props.componentDef != null)
            {
                allowedComponentSettings.filter = new ThingFilter();
                allowedComponentSettings.filter.SetAllow(Props.componentDef, true);
            }
        }

        public override void PostSpawnSetup(bool respawningAfterLoad)
        {
            base.PostSpawnSetup(respawningAfterLoad);
            powerComp = parent.TryGetComp<CompPowerTrader>();
        }

        public override void PostExposeData()
        {
            base.PostExposeData();
            Scribe_Values.Look(ref state, "state", MaintenancePodState.Idle);
            Scribe_Values.Look(ref storedComponents, "storedComponents", 0f);
            Scribe_Values.Look(ref ticksRemaining, "ticksRemaining", 0);
            Scribe_Deep.Look(ref innerContainer, "innerContainer", this);
            Scribe_Deep.Look(ref allowedComponentSettings, "allowedComponentSettings", this);
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

        // ===================== IStoreSettingsParent Implementation =====================
        public StorageSettings GetStoreSettings() => allowedComponentSettings;
        public StorageSettings GetParentStoreSettings() => null; // No parent settings
        public void Notify_SettingsChanged() { }
        public bool StorageTabVisible => false; // We show it in the inspect string

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
            
            // Update power consumption based on state
            if (powerComp != null)
            {
                powerComp.PowerOutput = - (state == MaintenancePodState.Running ? Props.powerConsumptionRunning : Props.powerConsumptionIdle);
            }
        }

        public void StartCycle(Pawn pawn)
        {
            float required = RequiredComponents(pawn);
            if (storedComponents < required)
            {
                Log.Error($"[WulaFallenEmpire] Tried to start maintenance cycle for {pawn.LabelShort} without enough components. This should have been checked earlier.");
                return;
            }

            storedComponents -= required;
            state = MaintenancePodState.Running;
            ticksRemaining = Props.durationTicks;

            // Move pawn inside
            pawn.DeSpawn();
            innerContainer.TryAdd(pawn, true);
        }

        private void CycleFinished()
        {
            Pawn occupant = Occupant;
            if (occupant == null)
            {
                Log.Error("[WulaFallenEmpire] Maintenance cycle finished, but no one was inside.");
                state = MaintenancePodState.Idle;
                return;
            }

            // Apply effects
            if (Props.hediffToRemove != null)
            {
                Hediff hediff = occupant.health.hediffSet.GetFirstHediffOfDef(Props.hediffToRemove);
                if (hediff != null)
                {
                    hediff.Severity = 0.01f;
                    Messages.Message("WULA_MaintenanceComplete".Translate(occupant.Named("PAWN")), occupant, MessageTypeDefOf.PositiveEvent);
                }
            }
            
            EjectPawn();
        }

        public void EjectPawn()
        {
            Pawn occupant = Occupant;
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
        }
        

        public void AddComponents(Thing components)
        {
            int count = components.stackCount;
            storedComponents += count;
            components.Destroy();
        }
        
        // ===================== UI & Gizmos =====================
        public override string CompInspectStringExtra()
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("WULA_MaintenancePod_Status".Translate() + ": " + $"WULA_MaintenancePod_State_{state}".Translate());

            if (state == MaintenancePodState.Running)
            {
                sb.AppendLine("Contains".Translate() + ": " + Occupant.NameShortColored.Resolve());
                sb.AppendLine("TimeLeft".Translate() + ": " + ticksRemaining.ToStringTicksToPeriod());
            }
            
            sb.AppendLine("WULA_MaintenancePod_StoredComponents".Translate() + ": " + storedComponents.ToString("F0") + " / " + capacity.ToString("F0"));

            if (!PowerOn)
            {
                sb.AppendLine("NoPower".Translate().Colorize(Color.red));
            }

            return sb.ToString().TrimEnd();
        }

        public override IEnumerable<Gizmo> CompGetGizmosExtra()
        {
            // Gizmo to order a pawn to enter
            if (state == MaintenancePodState.Idle && PowerOn)
            {
                var enterCommand = new Command_Action
                {
                    defaultLabel = "WULA_MaintenancePod_Enter".Translate(),
                    defaultDesc = "WULA_MaintenancePod_EnterDesc".Translate(),
                    icon = EnterIcon,
                    action = () =>
                    {
                        List<FloatMenuOption> options = new List<FloatMenuOption>();
                        foreach (Pawn p in parent.Map.mapPawns.FreeColonists)
                        {
                            if (p.health.hediffSet.HasHediff(Props.hediffToRemove))
                            {
                                float required = RequiredComponents(p);
                                if (storedComponents >= required)
                                {
                                    options.Add(new FloatMenuOption(p.LabelShort, () =>
                                    {
                                        Job job = JobMaker.MakeJob(JobDefOf_WULA.WULA_EnterMaintenancePod, parent);
                                        p.jobs.TryTakeOrderedJob(job, JobTag.Misc);
                                    }));
                                }
                                else
                                {
                                    options.Add(new FloatMenuOption(p.LabelShort + " (" + "WULA_MaintenancePod_NotEnoughComponents".Translate(required.ToString("F0")) + ")", null));
                                }
                            }
                        }
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

            // Gizmo to cancel and eject
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
        }
    }

    public enum MaintenancePodState
    {
        Idle,       // Waiting for a pawn or components
        Running,    // Occupied and performing maintenance
    }
}