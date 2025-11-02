using System.Collections.Generic;
using Verse;

namespace WulaFallenEmpire
{
    public class CompProperties_StorageTurret : CompProperties
    {
        public CompProperties_StorageTurret()
        {
            this.compClass = typeof(CompStorageTurret);
        }

        public ThingDef turretDef;
        public float angleOffset;
        public bool autoAttack = true;
        public int maxTurrets = 5; // 最大炮塔数量
        public float turretSpacing = 1f; // 炮塔间距
    }
}
