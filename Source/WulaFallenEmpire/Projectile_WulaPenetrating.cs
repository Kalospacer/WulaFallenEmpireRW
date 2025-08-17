using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace WulaFallenEmpire
{
    // Final, robust extension class for configuring path-based penetration.
    public class Wula_PathPierce_Extension : DefModExtension
    {
        // Set to a positive number for limited hits, or -1 for infinite penetration.
        public int maxHits = 3; 
        // The percentage of damage lost per hit. 0.25 means 25% damage loss per hit.
        public float damageFalloff = 0.25f; 
    }

    public class Projectile_WulaLineAttack : Projectile
    {
        private int hitCounter = 0;
        private List<Thing> alreadyDamaged = new List<Thing>();
        private Vector3 lastTickPosition;

        private Wula_PathPierce_Extension Props => def.GetModExtension<Wula_PathPierce_Extension>();

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref hitCounter, "hitCounter", 0);
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
            this.hitCounter = 0;
        }

        protected override void Tick()
        {
            Vector3 startPos = this.lastTickPosition;
            base.Tick(); 

            if (this.Destroyed) return;

            Vector3 endPos = this.ExactPosition;
            
            CheckPathForDamage(startPos, endPos);

            this.lastTickPosition = endPos;
        }
        
        protected override void Impact(Thing hitThing, bool blockedByShield = false)
        {
            CheckPathForDamage(lastTickPosition, this.ExactPosition);
            
            if (hitThing != null && alreadyDamaged.Contains(hitThing))
            {
                 base.Impact(null, blockedByShield);
            }
            else
            {
                 base.Impact(hitThing, blockedByShield);
            }
        }

        private void CheckPathForDamage(Vector3 startPos, Vector3 endPos)
        {
            if (startPos == endPos) return;

            int maxHits = Props?.maxHits ?? 1;
            bool infinitePenetration = maxHits < 0;

            if (!infinitePenetration && hitCounter >= maxHits) return;

            Map map = this.Map;
            float distance = Vector3.Distance(startPos, endPos);
            Vector3 direction = (endPos - startPos).normalized;

            for (float i = 0; i < distance; i += 0.8f) 
            {
                if (!infinitePenetration && hitCounter >= maxHits) break;

                Vector3 checkPos = startPos + direction * i;
                var thingsInCell = new HashSet<Thing>(map.thingGrid.ThingsListAt(checkPos.ToIntVec3()));

                foreach (Thing thing in thingsInCell)
                {
                    if (thing is Pawn pawn && pawn != this.launcher && !alreadyDamaged.Contains(pawn) && GenHostility.HostileTo(pawn, this.launcher.Faction))
                    {
                        ApplyPathDamage(pawn);
                        if (!infinitePenetration && hitCounter >= maxHits) break;
                    }
                }
            }
        }

        private void ApplyPathDamage(Pawn pawn)
        {
            Wula_PathPierce_Extension props = Props;
            float falloff = props?.damageFalloff ?? 0.25f;
            
            // Damage falloff now applies universally, even for infinite penetration.
            float damageMultiplier = Mathf.Pow(1f - falloff, hitCounter);
           
            int damageAmount = (int)(this.DamageAmount * damageMultiplier);
            if (damageAmount <= 0) return;

            var dinfo = new DamageInfo(
                this.def.projectile.damageDef,
                damageAmount,
                this.ArmorPenetration * damageMultiplier,
                this.ExactRotation.eulerAngles.y,
                this.launcher,
                null,
                this.equipmentDef,
                DamageInfo.SourceCategory.ThingOrUnknown,
                this.intendedTarget.Thing);
            
            pawn.TakeDamage(dinfo);
            alreadyDamaged.Add(pawn);
            hitCounter++;
        }
    }
}