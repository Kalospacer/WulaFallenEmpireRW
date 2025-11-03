// Building_MaintenancePod.cs
using RimWorld;
using Verse;

namespace WulaFallenEmpire
{
    public class Building_MaintenancePod : Building
    {
        public CompMaintenancePod MaintenanceComp => GetComp<CompMaintenancePod>();

        protected override void Tick()
        {
            base.Tick();
            // 建筑级别的特殊逻辑可以在这里添加
        }

        public override string GetInspectString()
        {
            string baseString = base.GetInspectString();
            string maintenanceString = MaintenanceComp?.CompInspectStringExtra();

            if (!string.IsNullOrEmpty(maintenanceString))
            {
                if (!string.IsNullOrEmpty(baseString))
                    return baseString + "\n" + maintenanceString;
                return maintenanceString;
            }

            return baseString;
        }
    }
}
