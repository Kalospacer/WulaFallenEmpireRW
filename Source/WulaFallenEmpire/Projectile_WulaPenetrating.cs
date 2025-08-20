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
        // If true, this projectile will never cause friendly fire, regardless of game settings.
        public bool preventFriendlyFire = false;
        public FleckDef tailFleckDef; // 用于配置拖尾特效的 FleckDef
    }

    public class Projectile_WulaLineAttack : Projectile
    {
        private int hitCounter = 0;
        private List<Thing> alreadyDamaged = new List<Thing>();
        private Vector3 lastTickPosition;
        private int Fleck_MakeFleckTick; // 拖尾特效的计时器
        public int Fleck_MakeFleckTickMax = 1; // 拖尾特效的生成频率
        public IntRange Fleck_MakeFleckNum = new IntRange(1, 1); // 每次生成的粒子数量
        public FloatRange Fleck_Angle = new FloatRange(-180f, 180f); // 粒子角度
        public FloatRange Fleck_Scale = new FloatRange(1f, 1f); // 粒子大小
        public FloatRange Fleck_Speed = new FloatRange(0f, 0f); // 粒子速度
        public FloatRange Fleck_Rotation = new FloatRange(-180f, 180f); // 粒子旋转

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
            // Friendly fire is prevented if EITHER the game setting is true OR the XML extension is true.
            this.preventFriendlyFire = preventFriendlyFire || (Props?.preventFriendlyFire ?? false);
        }

        protected override void Tick()
        {
            Vector3 startPos = this.lastTickPosition;
            base.Tick();
            
            if (this.Destroyed) return;

            this.Fleck_MakeFleckTick++;
            bool flag = this.Fleck_MakeFleckTick >= this.Fleck_MakeFleckTickMax;
            if (flag)
            {
                this.Fleck_MakeFleckTick = 0;
                Map map = base.Map;
                int randomInRange = this.Fleck_MakeFleckNum.RandomInRange;
                Vector3 vector = this.ExactPosition; // Current position of the bullet
                Vector3 vector2 = this.lastTickPosition; // Previous position of the bullet

                for (int i = 0; i < randomInRange; i++)
                {
                    float num = (vector - vector2).AngleFlat(); // Angle based on movement direction
                    float velocityAngle = this.Fleck_Angle.RandomInRange + num;
                    float randomInRange2 = this.Fleck_Scale.RandomInRange;
                    float randomInRange3 = this.Fleck_Speed.RandomInRange;
 
                    if (Props?.tailFleckDef != null)
                    {
                        FleckCreationData dataStatic = FleckMaker.GetDataStatic(vector, map, Props.tailFleckDef, randomInRange2);
                        dataStatic.rotation = (vector - vector2).AngleFlat();
                        dataStatic.rotationRate = this.Fleck_Rotation.RandomInRange;
                        dataStatic.velocityAngle = velocityAngle;
                        dataStatic.velocitySpeed = randomInRange3;
                        map.flecks.CreateFleck(dataStatic);
                    }
                }
            }

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
                   if (thing is Pawn pawn && pawn != this.launcher && !alreadyDamaged.Contains(pawn))
                   {
                       bool shouldDamage = false;

                       // Case 1: Always damage the intended target if it's a pawn. This allows hunting.
                       if (this.intendedTarget.Thing == pawn)
                       {
                           shouldDamage = true;
                       }
                       // Case 2: Always damage hostile pawns in the path.
                       else if (pawn.HostileTo(this.launcher))
                       {
                           shouldDamage = true;
                       }
                       // Case 3: Damage non-hostiles (friendlies, neutrals) if the shot itself isn't marked to prevent friendly fire.
                       else if (!this.preventFriendlyFire)
                       {
                           shouldDamage = true;
                       }

                       if (shouldDamage)
                       {
                           ApplyPathDamage(pawn);
                           if (!infinitePenetration && hitCounter >= maxHits) break;
                       }
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