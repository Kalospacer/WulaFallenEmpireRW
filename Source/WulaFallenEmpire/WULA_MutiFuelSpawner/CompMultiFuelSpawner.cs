using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;

namespace WulaFallenEmpire
{
    public class SpawnerProduct
    {
        public ThingDef thingDef;
        public int count = 1;
    }

    // --- Properties Class ---
    public class CompProperties_MultiFuelSpawner : CompProperties
    {
        public List<SpawnerProduct> products;
        public IntRange spawnIntervalRange = new IntRange(100, 100);
        public bool spawnForbidden;
        public bool inheritFaction;
        public bool showMessageIfOwned;

        public CompProperties_MultiFuelSpawner()
        {
            compClass = typeof(CompMultiFuelSpawner);
        }
    }

    // --- Component Class ---
    public class CompMultiFuelSpawner : ThingComp
    {
        private int ticksUntilSpawn;
        private List<CompRefuelableWithKey> fuelComps;

        public CompProperties_MultiFuelSpawner Props => (CompProperties_MultiFuelSpawner)props;

        public override void PostSpawnSetup(bool respawningAfterLoad)
        {
            base.PostSpawnSetup(respawningAfterLoad);
            if (!respawningAfterLoad)
            {
                ResetCountdown();
            }
            fuelComps = parent.GetComps<CompRefuelableWithKey>().ToList();
        }

        public override void PostExposeData()
        {
            base.PostExposeData();
            Scribe_Values.Look(ref ticksUntilSpawn, "ticksUntilSpawn", 0);
        }

        public override void CompTick()
        {
            base.CompTick();

            if (fuelComps.NullOrEmpty()) return;
            
            bool allFuelsOk = fuelComps.All(c => c.HasFuel);
            
            if (allFuelsOk && (parent.GetComp<CompPowerTrader>()?.PowerOn ?? true))
            {
                ticksUntilSpawn--;
                if (ticksUntilSpawn <= 0)
                {
                    foreach (var comp in fuelComps)
                    {
                        comp.Notify_UsedThisTick();
                    }
                    
                    TryDoSpawn();
                    ResetCountdown();
                }
            }
        }

        public void TryDoSpawn()
        {
            if (Props.products.NullOrEmpty()) return;

            foreach (var product in Props.products)
            {
                Thing thing = ThingMaker.MakeThing(product.thingDef);
                thing.stackCount = product.count;

                if (Props.inheritFaction && thing.Faction != parent.Faction)
                {
                    thing.SetFaction(parent.Faction);
                }

                if (GenPlace.TryPlaceThing(thing, parent.Position, parent.Map, ThingPlaceMode.Near, out Thing resultingThing))
                {
                    if (Props.spawnForbidden)
                    {
                        resultingThing.SetForbidden(true);
                    }

                    if (Props.showMessageIfOwned && parent.Faction == Faction.OfPlayer)
                    {
                        Messages.Message("MessageCompSpawnerSpawnedItem".Translate(resultingThing.LabelCap), resultingThing, MessageTypeDefOf.PositiveEvent);
                    }
                }
            }
        }

        private void ResetCountdown()
        {
            ticksUntilSpawn = Props.spawnIntervalRange.RandomInRange;
        }

        public override string CompInspectStringExtra()
        {
            string text = base.CompInspectStringExtra(); 
            
            if (fuelComps.All(c => c.HasFuel))
            {
                if (!text.NullOrEmpty())
                {
                    text += "\n";
                }
                string productsStr = Props.products.Select(p => (string)p.thingDef.LabelCap).ToCommaList();
                text += "NextSpawnedItemIn".Translate(productsStr) + ": " + ticksUntilSpawn.ToStringTicksToPeriod();
            }
            
            return text;
        }
    }
}