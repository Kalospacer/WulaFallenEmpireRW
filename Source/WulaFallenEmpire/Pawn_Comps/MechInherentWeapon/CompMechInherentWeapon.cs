// CompMechInherentWeapon.cs
using RimWorld;
using System.Collections.Generic;
using Verse;

namespace WulaFallenEmpire
{
    public class CompMechInherentWeapon : ThingComp
    {
        public CompProperties_MechInherentWeapon Props => (CompProperties_MechInherentWeapon)props;
        
        private Pawn mech => parent as Pawn;
        private int lastCheckTick = -1;
        
        public override void PostSpawnSetup(bool respawningAfterLoad)
        {
            base.PostSpawnSetup(respawningAfterLoad);
            
            // 机甲生成时立即检查一次
            CheckAndEquipWeapon();
        }
        
        public override void CompTick()
        {
            base.CompTick();
            
            if (mech == null || mech.Dead || !mech.Spawned || Props.weaponDef == null)
                return;
                
            // 每250ticks检查一次
            int currentTick = Find.TickManager.TicksGame;
            if (currentTick - lastCheckTick >= 250)
            {
                CheckAndEquipWeapon();
                lastCheckTick = currentTick;
            }
        }
        
        private void CheckAndEquipWeapon()
        {
            if (mech == null || mech.Dead || !mech.Spawned || Props.weaponDef == null)
                return;
                
            // 检查当前装备
            ThingWithComps currentWeapon = mech.equipment?.Primary;
            
            // 如果当前装备的不是指定武器，或者没有装备武器
            if (currentWeapon == null || currentWeapon.def != Props.weaponDef)
            {
                // 丢弃当前武器
                if (currentWeapon != null)
                {
                    DropCurrentWeapon(currentWeapon);
                }
                
                // 装备固有武器
                EquipInherentWeapon();
            }
        }
        
        private void DropCurrentWeapon(ThingWithComps weapon)
        {
            if (weapon == null || mech.Map == null || mech.equipment == null)
                return;
                
            // 从装备中移除
            if (mech.equipment.Contains(weapon))
            {
                mech.equipment.Remove(weapon);
            }
            
            // 放置到机甲位置
            GenPlace.TryPlaceThing(weapon, mech.Position, mech.Map, ThingPlaceMode.Near);
        }
        
        private void EquipInherentWeapon()
        {
            if (mech == null || mech.equipment == null || Props.weaponDef == null)
                return;
                
            // 创建固有武器
            ThingWithComps inherentWeapon = ThingMaker.MakeThing(Props.weaponDef) as ThingWithComps;
            if (inherentWeapon == null)
                return;
                
            // 装备武器
            mech.equipment.AddEquipment(inherentWeapon);
        }
        
        public override void PostExposeData()
        {
            base.PostExposeData();
            Scribe_Values.Look(ref lastCheckTick, "lastCheckTick", -1);
        }
    }
}
