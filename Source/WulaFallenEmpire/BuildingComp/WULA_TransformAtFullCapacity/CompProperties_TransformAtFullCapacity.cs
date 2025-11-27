// CompProperties_TransformAtFullCapacity.cs
using RimWorld;
using Verse;

namespace WulaFallenEmpire
{
    public class CompProperties_TransformAtFullCapacity : CompProperties
    {
        public PawnKindDef targetPawnKind;
        public int requiredCapacity = 5;
        
        public string gizmoLabel = "转换为机械单位";
        public string gizmoDesc = "将储存的机械族转换为一个强大的机械单位。";
        public string gizmoIconPath;
        
        public CompProperties_TransformAtFullCapacity()
        {
            compClass = typeof(CompTransformAtFullCapacity);
        }
    }
}