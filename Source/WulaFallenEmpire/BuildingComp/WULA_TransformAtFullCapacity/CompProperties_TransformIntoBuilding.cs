
// CompProperties_TransformIntoBuilding.cs
using RimWorld;
using Verse;

namespace WulaFallenEmpire
{
    public class CompProperties_TransformIntoBuilding : CompProperties
    {
        public ThingDef targetBuildingDef;

        public string gizmoLabel = "部署为建筑";
        public string gizmoDesc = "转换为功能建筑形态。";
        public string gizmoIconPath;

        public CompProperties_TransformIntoBuilding()
        {
            compClass = typeof(CompTransformIntoBuilding);
        }
    }
}