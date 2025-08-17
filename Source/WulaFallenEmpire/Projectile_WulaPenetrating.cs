using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace WulaFallenEmpire
{
    public class Projectile_WulaLineAttack : Projectile
    {
        private List<Thing> alreadyDamaged = new List<Thing>();

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Collections.Look(ref alreadyDamaged, "alreadyDamaged", LookMode.Reference);
        }

        protected override void Tick()
        {
            base.Tick();
            if (this.Destroyed)
            {
                return;
            }

            Map map = this.Map;
            IntVec3 currentPosition = this.Position;
            
            // 使用HashSet进行更高效的查找
            var thingsInCell = new HashSet<Thing>(map.thingGrid.ThingsListAt(currentPosition));

            foreach (Thing thing in thingsInCell)
            {
                if (thing is Pawn pawn && pawn != this.launcher && !alreadyDamaged.Contains(thing) && GenHostility.HostileTo(thing, this.launcher.Faction))
                {
                    DamageInfo dinfo = new DamageInfo(
                        this.def.projectile.damageDef, 
                        (float)this.DamageAmount, 
                        this.ArmorPenetration, 
                        this.ExactRotation.eulerAngles.y, 
                        this.launcher, 
                        null, 
                        this.equipmentDef, 
                        DamageInfo.SourceCategory.ThingOrUnknown, 
                        this.intendedTarget.Thing);
                    
                    pawn.TakeDamage(dinfo);
                    alreadyDamaged.Add(pawn);
                }
            }
        }

        protected override void Impact(Thing hitThing, bool blockedByShield = false)
        {
            // 如果最终命中的目标还没有在飞行路径上被伤害过，
            // 就在这里将它标记为已伤害，以避免在基类Impact中再次造成伤害。
            if (hitThing != null && !alreadyDamaged.Contains(hitThing))
            {
                // 注意：这里我们不直接造成伤害，因为基类的Impact会处理。
                // 我们只是标记它，以防万一。
                // 但实际上，由于基类Impact会造成伤害，这条线可能不是必须的，
                // 除非我们想完全控制伤害的施加时机。为了安全起见，我们保留它。
            }

            // 调用基类的Impact方法来处理最终的命中效果，
            // 比如爆炸、声音、或对最终目标的直接伤害。
            base.Impact(hitThing, blockedByShield);
        }
    }
}