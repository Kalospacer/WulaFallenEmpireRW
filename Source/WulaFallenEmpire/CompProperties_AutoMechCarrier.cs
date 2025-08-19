using RimWorld;
using Verse;
using System.Collections.Generic;

namespace WulaFallenEmpire
{
    public class CompProperties_AutoMechCarrier : CompProperties_MechCarrier
    {
        // XML中定义，生产是否消耗资源
        public bool freeProduction = false;

        // 如果单位拥有这个Hediff，则停止生产
        public HediffDef disableHediff;

        // 定义生产队列
        public List<PawnProductionEntry> productionQueue = new List<PawnProductionEntry>();

        public CompProperties_AutoMechCarrier()
        {
            // 确保这个属性类指向我们新的功能实现类
            compClass = typeof(CompAutoMechCarrier);
        }

        public override IEnumerable<string> ConfigErrors(ThingDef parentDef)
        {
            foreach (string error in base.ConfigErrors(parentDef))
            {
                yield return error;
            }

            if (productionQueue.NullOrEmpty())
            {
                yield return "CompProperties_AutoMechCarrier must have at least one entry in productionQueue.";
            }
        }

        public override void ResolveReferences(ThingDef parentDef)
        {
            base.ResolveReferences(parentDef);
            // Prevent division by zero if costPerPawn is not set, which the base game AI might try to access.
            if (costPerPawn <= 0)
            {
                costPerPawn = 1;
            }

            // 如果spawnPawnKind为空（因为我们用了新的队列系统），
            // 就从队列里取第一个作为“假”值，以防止基类方法在生成Gizmo标签时出错。
            if (spawnPawnKind == null && !productionQueue.NullOrEmpty())
            {
                spawnPawnKind = productionQueue[0].pawnKind;
            }
        }
    }
}