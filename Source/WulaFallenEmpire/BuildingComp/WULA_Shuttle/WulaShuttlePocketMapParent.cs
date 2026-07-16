using RimWorld.Planet;
using Verse;

namespace WulaFallenEmpire
{
    /// <summary>
    /// Pocket-map parent with an explicit, saved link to its mobile shuttle owner.
    /// </summary>
    public class WulaShuttlePocketMapParent : PocketMapParent
    {
        public Building_ArmedShuttleWithPocket ownerShuttle;

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_References.Look(ref ownerShuttle, "ownerShuttle");

            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                LongEventHandler.ExecuteWhenFinished(() => ownerShuttle?.Notify_PocketMapParentLoaded(this));
            }
        }

        public void Bind(Building_ArmedShuttleWithPocket shuttle, Map currentSourceMap, MapGeneratorDef generator)
        {
            ownerShuttle = shuttle;
            mapGenerator = generator;
            if (currentSourceMap != null && !currentSourceMap.IsPocketMap)
            {
                sourceMap = currentSourceMap;
            }
        }

        public override void Notify_MyMapRemoved(Map map)
        {
            base.Notify_MyMapRemoved(map);
            ownerShuttle?.Notify_PocketMapRemoved(map);
        }
    }
}
