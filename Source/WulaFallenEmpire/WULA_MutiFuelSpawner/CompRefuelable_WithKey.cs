using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;

namespace WulaFallenEmpire
{
    // --- 1. The Properties Class ---
    public class CompProperties_Refuelable_WithKey : CompProperties
    {
        public float fuelConsumptionRate = 1f;
        public float fuelCapacity = 2f;
        public float initialFuelPercent;
        public float autoRefuelPercent = 0.3f;
        public ThingFilter fuelFilter;
        public bool consumeFuelOnlyWhenUsed = true;
        public bool showFuelGizmo = true;
        public bool targetFuelLevelConfigurable;
        public float initialConfigurableTargetFuelLevel = -1f;
        public string fuelLabel;
        public string outOfFuelMessage;
        public bool showAllowAutoRefuelToggle;
        public string saveKeysPrefix; // The only field we are adding

        public CompProperties_Refuelable_WithKey()
        {
            compClass = typeof(CompRefuelable_WithKey);
        }
    }

    // --- 2. The Component Class (Full Re-implementation) ---
    public class CompRefuelable_WithKey : ThingComp
    {
        // Re-implemented fields from CompRefuelable
        private float fuel;
        private float configuredTargetFuelLevel = -1f;
        public bool allowAutoRefuel = true;
        private CompFlickable flickComp;

        public new CompProperties_Refuelable_WithKey Props => (CompProperties_Refuelable_WithKey)props;

        public float Fuel => fuel;
        public bool HasFuel => fuel > 0f;
        public bool IsFull => TargetFuelLevel - fuel < 1f;
        public float FuelPercentOfMax => fuel / Props.fuelCapacity;
        public float TargetFuelLevel
        {
            get
            {
                if (configuredTargetFuelLevel >= 0f) return configuredTargetFuelLevel;
                if (Props.targetFuelLevelConfigurable) return Props.initialConfigurableTargetFuelLevel;
                return Props.fuelCapacity;
            }
            set => configuredTargetFuelLevel = Mathf.Clamp(value, 0f, Props.fuelCapacity);
        }
        public bool ShouldAutoRefuelNow => Fuel / TargetFuelLevel <= Props.autoRefuelPercent && !IsFull && TargetFuelLevel > 0f && (flickComp == null || flickComp.SwitchIsOn);


        public override void Initialize(CompProperties props)
        {
            base.Initialize(props);
            allowAutoRefuel = true; // Simplified from base
            fuel = Props.fuelCapacity * Props.initialFuelPercent;
            if(Props.initialConfigurableTargetFuelLevel > 0)
            {
                configuredTargetFuelLevel = Props.initialConfigurableTargetFuelLevel;
            }
        }

        public override void PostSpawnSetup(bool respawningAfterLoad)
        {
            base.PostSpawnSetup(respawningAfterLoad);
            flickComp = parent.GetComp<CompFlickable>();
        }

        // The ONLY method we actually change
        public override void PostExposeData()
        {
            base.PostExposeData();
            string prefix = Props.saveKeysPrefix;
            if (prefix.NullOrEmpty())
            {
                Log.ErrorOnce($"CompRefuelable_WithKey on {parent.def.defName} has a null or empty saveKeysPrefix.", GetHashCode());
                // Fallback to default scribing to avoid data loss
                Scribe_Values.Look(ref fuel, "fuel", 0f);
                Scribe_Values.Look(ref configuredTargetFuelLevel, "configuredTargetFuelLevel", -1f);
                Scribe_Values.Look(ref allowAutoRefuel, "allowAutoRefuel", true);
                return;
            }
            Scribe_Values.Look(ref fuel, prefix + "_fuel", 0f);
            Scribe_Values.Look(ref configuredTargetFuelLevel, prefix + "_configuredTargetFuelLevel", -1f);
            Scribe_Values.Look(ref allowAutoRefuel, prefix + "_allowAutoRefuel", true);
        }
        
        public void ConsumeFuel(float amount)
        {
            if (fuel <= 0f) return;
            fuel -= amount;
            if (fuel <= 0f)
            {
                fuel = 0f;
                parent.BroadcastCompSignal("RanOutOfFuel");
            }
        }

        public void Refuel(float amount)
        {
            fuel += amount;
            if (fuel > Props.fuelCapacity)
            {
                fuel = Props.fuelCapacity;
            }
            parent.BroadcastCompSignal("Refueled");
        }

        public void Notify_UsedThisTick()
        {
            if (Props.consumeFuelOnlyWhenUsed)
            {
                 ConsumeFuel(Props.fuelConsumptionRate / 60000f);
            }
        }
        
        public override IEnumerable<Gizmo> CompGetGizmosExtra()
        {
             if (!Props.showFuelGizmo || parent.Faction != Faction.OfPlayer) yield break;

            // Simplified Gizmo Status (can be replaced with copied Gizmo_RefuelableFuelStatus later)
            yield return new Gizmo_FuelStatus_Spawner(new FuelSystem(this)); // Using a dummy adapter

            // Copied Set Target Level Command
            if (Props.targetFuelLevelConfigurable)
            {
                var command = new Command_Action
                {
                    defaultLabel = "CommandSetTargetFuelLevel".Translate(),
                    defaultDesc = "CommandSetTargetFuelLevelDesc".Translate(),
                    icon = ContentFinder<Texture2D>.Get("UI/Commands/SetTargetFuelLevel"),
                    action = delegate
                    {
                        Dialog_Slider dialog = new Dialog_Slider(
                            "SetTargetFuelLevel".Translate(), 0, (int)Props.fuelCapacity, 
                            (val) => TargetFuelLevel = val, (int)TargetFuelLevel);
                        Find.WindowStack.Add(dialog);
                    }
                };
                yield return command;
            }
        }
    }

    // Dummy adapter to make the new Gizmo work temporarily
    public class FuelSystem
    {
        public CompRefuelable_WithKey comp;
        public FuelSystem(CompRefuelable_WithKey comp) { this.comp = comp; }
        public float Fuel => comp.Fuel;
        public float FuelPercent => comp.FuelPercentOfMax;
        public CompProperties_Refuelable props => comp.Props;
        public float TargetFuelLevel { get => comp.TargetFuelLevel; set => comp.TargetFuelLevel = value; }
    }
}