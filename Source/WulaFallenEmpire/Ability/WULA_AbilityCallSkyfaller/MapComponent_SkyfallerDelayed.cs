using System.Collections.Generic;
using RimWorld;
using Verse;

namespace WulaFallenEmpire
{
    public class MapComponent_SkyfallerDelayed : MapComponent
    {
        private List<DelayedSkyfaller> scheduledSkyfallers = new List<DelayedSkyfaller>();

        public MapComponent_SkyfallerDelayed(Map map) : base(map) { }

        public void ScheduleSkyfaller(ThingDef skyfallerDef, IntVec3 targetCell, int delayTicks, Pawn caster = null)
        {
            scheduledSkyfallers.Add(new DelayedSkyfaller
            {
                skyfallerDef = skyfallerDef,
                targetCell = targetCell,
                spawnTick = Find.TickManager.TicksGame + delayTicks,
                caster = caster
            });
        }

        public override void MapComponentTick()
        {
            base.MapComponentTick();

            int currentTick = Find.TickManager.TicksGame;
            for (int i = scheduledSkyfallers.Count - 1; i >= 0; i--)
            {
                var skyfaller = scheduledSkyfallers[i];
                if (currentTick >= skyfaller.spawnTick)
                {
                    SpawnSkyfaller(skyfaller);
                    scheduledSkyfallers.RemoveAt(i);
                }
            }
        }

        private void SpawnSkyfaller(DelayedSkyfaller delayedSkyfaller)
        {
            try
            {
                if (delayedSkyfaller.skyfallerDef == null) return;
                if (!delayedSkyfaller.targetCell.IsValid || !delayedSkyfaller.targetCell.InBounds(map)) return;

                Skyfaller skyfaller = SkyfallerMaker.MakeSkyfaller(delayedSkyfaller.skyfallerDef);
                GenSpawn.Spawn(skyfaller, delayedSkyfaller.targetCell, map);

                if (Prefs.DevMode)
                {
                    Log.Message($"[DelayedSkyfaller] Spawned '{delayedSkyfaller.skyfallerDef.defName}' at {delayedSkyfaller.targetCell}");
                }
            }
            catch (System.Exception ex)
            {
                Log.Error($"[DelayedSkyfaller] Error spawning skyfaller: {ex}");
            }
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Collections.Look(ref scheduledSkyfallers, "scheduledSkyfallers", LookMode.Deep);
        }
    }

    public class DelayedSkyfaller : IExposable
    {
        public ThingDef skyfallerDef;
        public IntVec3 targetCell;
        public int spawnTick;
        public Pawn caster;

        public void ExposeData()
        {
            Scribe_Defs.Look(ref skyfallerDef, "skyfallerDef");
            Scribe_Values.Look(ref targetCell, "targetCell");
            Scribe_Values.Look(ref spawnTick, "spawnTick");
            Scribe_References.Look(ref caster, "caster");
        }
    }
}

