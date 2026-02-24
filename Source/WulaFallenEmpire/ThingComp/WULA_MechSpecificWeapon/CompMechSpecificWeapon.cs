// File: CompMechOnlyWeapon.cs
using System.Collections.Generic;
using Verse;

namespace WulaFallenEmpire
{
    /// <summary>
    /// 简单的机甲专用武器组件
    /// </summary>
    public class CompMechOnlyWeapon : ThingComp
    {
        public List<ThingDef> allowedMechRaces;
        
        public override void Initialize(CompProperties props)
        {
            base.Initialize(props);
            allowedMechRaces = ((CompProperties_MechOnlyWeapon)props).allowedMechRaces;
        }
        
        /// <summary>
        /// 检查机甲是否可以装备此武器
        /// </summary>
        public bool CanBeEquippedByMech(Pawn mech)
        {
            if (mech == null || mech.def == null) return false;
            return allowedMechRaces != null && allowedMechRaces.Contains(mech.def);
        }
    }
    
    public class CompProperties_MechOnlyWeapon : CompProperties
    {
        public List<ThingDef> allowedMechRaces = new List<ThingDef>();
        
        public CompProperties_MechOnlyWeapon()
        {
            compClass = typeof(CompMechOnlyWeapon);
        }
    }
}
