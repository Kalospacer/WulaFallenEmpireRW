using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;

namespace WulaFallenEmpire
{
    // --- Properties Class ---
    public class CompProperties_RefuelableSpawner : CompProperties_Refuelable
    {
        public List<SpawnerProduct> products;
        public IntRange spawnIntervalRange = new IntRange(100, 100);
        public bool spawnForbidden;
        public bool inheritFaction;
        public bool showMessageIfOwned;

        public CompProperties_RefuelableSpawner()
        {
            compClass = typeof(CompRefuelableSpawner);
        }
    }

    // --- Component Class ---
    public class CompRefuelableSpawner : CompRefuelable
    {
        private int ticksUntilSpawn;

        public new CompProperties_RefuelableSpawner Props => (CompProperties_RefuelableSpawner)props;

        public override void PostExposeData()
        {
            base.PostExposeData(); 
            Scribe_Values.Look(ref ticksUntilSpawn, "ticksUntilSpawn", 0);
        }

        public override void PostSpawnSetup(bool respawningAfterLoad)
        {
            base.PostSpawnSetup(respawningAfterLoad);
            if (!respawningAfterLoad)
            {
                ResetCountdown();
            }
        }

        public override void CompTick()
        {
            base.CompTick(); 
            
            if (HasFuel && (parent.GetComp<CompPowerTrader>()?.PowerOn ?? true))
            {
                ticksUntilSpawn--;
                if (ticksUntilSpawn <= 0)
                {
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
            
            if (HasFuel)
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
        
        public override IEnumerable<Gizmo> CompGetGizmosExtra()
        {
            foreach (var g in base.CompGetGizmosExtra())
            {
                yield return g;
            }

            if (Prefs.DevMode)
            {
                yield return new Command_Action
                {
                    defaultLabel = "DEBUG: Spawn items",
                    action = delegate
                    {
                        TryDoSpawn();
                        ResetCountdown();
                    }
                };
            }
        }
    }
    
    public class SpawnerProduct
    {
        public ThingDef thingDef;
        public int count = 1;
    }
}