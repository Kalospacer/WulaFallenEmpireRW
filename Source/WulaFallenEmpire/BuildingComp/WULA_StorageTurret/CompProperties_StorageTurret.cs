// File: CompProperties_StorageMultiTurretGun.cs
using RimWorld;
using System.Collections.Generic;
using Verse;

namespace WulaFallenEmpire
{
    public class CompProperties_StorageMultiTurretGun : CompProperties_TurretGun
    {
        // 炮塔ID，用于区分多个炮塔
        public int ID = 0;
        
        // 激活需要的机械族数量
        public int requiredMechanoids = 1;
        
        // 是否根据机械族数量自动激活
        public bool autoActivate = true;
        
        public CompProperties_StorageMultiTurretGun()
        {
            compClass = typeof(Comp_StorageMultiTurretGun);
        }
    }
}
