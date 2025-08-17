using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace WulaFallenEmpire
{
    public class Projectile_WulaLineAttack : Projectile
    {
        private List<Thing> alreadyDamaged = new List<Thing>();
        private Vector3 lastTickPosition;

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Collections.Look(ref alreadyDamaged, "alreadyDamaged", LookMode.Reference);
            Scribe_Values.Look(ref lastTickPosition, "lastTickPosition");
            if (alreadyDamaged == null)
            {
                alreadyDamaged = new List<Thing>();
            }
        }

        public override void Launch(Thing launcher, Vector3 origin, LocalTargetInfo usedTarget, LocalTargetInfo intendedTarget, ProjectileHitFlags hitFlags, bool preventFriendlyFire = false, Thing equipment = null, ThingDef targetCoverDef = null)
        {
            base.Launch(launcher, origin, usedTarget, intendedTarget, hitFlags, preventFriendlyFire, equipment, targetCoverDef);
            this.lastTickPosition = origin;
            this.alreadyDamaged.Clear();
        }

        protected override void Tick()
        {
            Vector3 startPos = this.lastTickPosition;
            
            base.Tick(); // 这会更新弹丸的位置，并可能调用Impact()

            if (this.Destroyed)
            {
                return;
            }
            
            Vector3 endPos = this.ExactPosition;

            // 调用路径伤害检测
            DamageMissedPawns(startPos, endPos);

            // 为下一帧更新位置
            this.lastTickPosition = endPos;
        }

        protected override void Impact(Thing hitThing, bool blockedByShield = false)
        {
            // 在最终碰撞前，最后一次检查从上一帧到当前碰撞点的路径
            DamageMissedPawns(this.lastTickPosition, this.ExactPosition);
            
            // 如果最终目标还没被路径伤害击中，在这里造成一次伤害
            if (hitThing != null && !alreadyDamaged.Contains(hitThing))
            {
                 DamageInfo dinfo = new DamageInfo(this.def.projectile.damageDef, (float)this.DamageAmount, this.ArmorPenetration, this.ExactRotation.eulerAngles.y, this.launcher, null, this.equipmentDef, DamageInfo.SourceCategory.ThingOrUnknown, this.intendedTarget.Thing);
                 hitThing.TakeDamage(dinfo);
            }

            // 调用基类方法来处理XML中定义的爆炸等最终效果
            base.Impact(hitThing, blockedByShield);
        }

        private void DamageMissedPawns(Vector3 startPos, Vector3 endPos)
        {
            if (startPos == endPos) return;

            Map map = this.Map;
            float distance = Vector3.Distance(startPos, endPos);
            Vector3 direction = (endPos - startPos).normalized;

            for (float i = 0; i < distance; i += 0.5f)
            {
                Vector3 checkPos = startPos + direction * i;
                IntVec3 checkCell = new IntVec3(checkPos);

                if (!checkCell.InBounds(map)) continue;
                
                var thingsInCell = new HashSet<Thing>(map.thingGrid.ThingsListAt(checkCell));
                foreach (Thing thing in thingsInCell)
                {
                    if (thing is Pawn pawn && pawn != this.launcher && !alreadyDamaged.Contains(pawn) && GenHostility.HostileTo(pawn, this.launcher.Faction))
                    {
                        var dinfo = new DamageInfo(this.def.projectile.damageDef, (float)this.DamageAmount, this.ArmorPenetration, this.ExactRotation.eulerAngles.y, this.launcher, null, this.equipmentDef, DamageInfo.SourceCategory.ThingOrUnknown, this.intendedTarget.Thing);
                        pawn.TakeDamage(dinfo);
                        alreadyDamaged.Add(pawn);
                    }
                }
            }
        }
    }
}