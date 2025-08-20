using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace WulaFallenEmpire
{
    // A new, dedicated extension class for the penetrating beam.
    public class Wula_BeamPierce_Extension : DefModExtension
    {
        public int maxHits = 3; 
        public float damageFalloff = 0.25f; 
        public bool preventFriendlyFire = false;
        public ThingDef beamMoteDef;
        public float beamWidth = 1f;
        public float beamStartOffset = 0f;
    }

    public class Projectile_WulaBeam : Bullet
    {
        private int hitCounter = 0;
        private List<Thing> alreadyDamaged = new List<Thing>();

        // It now gets its properties from the new, dedicated extension.
        private Wula_BeamPierce_Extension Props => def.GetModExtension<Wula_BeamPierce_Extension>();
        
        public override Vector3 ExactPosition => destination + Vector3.up * def.Altitude;

        public override void Launch(Thing launcher, Vector3 origin, LocalTargetInfo usedTarget, LocalTargetInfo intendedTarget, ProjectileHitFlags hitFlags, bool preventFriendlyFire = false, Thing equipment = null, ThingDef targetCoverDef = null)
        {
            base.Launch(launcher, origin, usedTarget, intendedTarget, hitFlags, preventFriendlyFire, equipment, targetCoverDef);
            
            Wula_BeamPierce_Extension props = Props;
            if (props == null)
            {
                Log.Error("Projectile_WulaBeam requires a Wula_BeamPierce_Extension in its def.");
                Destroy(DestroyMode.Vanish);
                return;
            }

            this.hitCounter = 0;
            this.alreadyDamaged.Clear();
            
            bool shouldPreventFriendlyFire = preventFriendlyFire || props.preventFriendlyFire;

            Map map = this.Map;
            // --- Corrected Start Position Calculation ---
            // The beam should start from the gun's muzzle, not the pawn's center.
            Vector3 endPosition = usedTarget.Cell.ToVector3Shifted();
            Vector3 castPosition = origin + (endPosition - origin).Yto0().normalized * props.beamStartOffset;

            // --- Vanilla Beam Drawing Logic ---
            if (props.beamMoteDef != null)
            {
                // Calculate the offset exactly like the vanilla Beam class does.
                // The offset for the mote is calculated from the launcher's true position, not the cast position.
                Vector3 moteOffset = (endPosition - launcher.Position.ToVector3Shifted()).Yto0().normalized * props.beamStartOffset;
                MoteMaker.MakeInteractionOverlay(props.beamMoteDef, launcher, usedTarget.ToTargetInfo(map), moteOffset, Vector3.zero);
            }

            float distance = Vector3.Distance(castPosition, endPosition);
            Vector3 direction = (endPosition - castPosition).normalized;

            var thingsOnPath = new HashSet<Thing>();
            for (float i = 0; i < distance; i += 1.0f)
            {
                IntVec3 cell = (castPosition + direction * i).ToIntVec3();
                if (cell.InBounds(map))
                {
                    thingsOnPath.AddRange(map.thingGrid.ThingsListAt(cell));
                }
            }
            // CRITICAL FIX: Manually add the intended target to the list.
            // This guarantees the primary target is always processed, even if the loop sampling misses its exact cell.
            if (intendedTarget.HasThing)
            {
                thingsOnPath.Add(intendedTarget.Thing);
            }

            int maxHits = props.maxHits;
            bool infinitePenetration = maxHits < 0;

            foreach (Thing thing in thingsOnPath)
            {
                if (!infinitePenetration && hitCounter >= maxHits) break;

                // 统一处理 Pawn 和 Building 的伤害逻辑
                // 确保 Thing 未被伤害过且不是发射者
                if (thing != launcher && !alreadyDamaged.Contains(thing))
                {
                    bool shouldDamage = false;
                    Pawn pawn = thing as Pawn;
                    Building building = thing as Building;

                    if (pawn != null) // 如果是 Pawn
                    {
                        if (intendedTarget.Thing == pawn) shouldDamage = true;
                        else if (pawn.HostileTo(launcher)) shouldDamage = true;
                        else if (!shouldPreventFriendlyFire) shouldDamage = true;
                    }
                    else if (building != null) // 如果是 Building
                    {
                        shouldDamage = true; // 默认对 Building 造成伤害
                    }

                    if (shouldDamage)
                    {
                        ApplyPathDamage(thing, props); // 传递 Thing
                    }
                }
                // 只有当遇到完全阻挡的 Thing 且不是 Pawn 或 Building 时才停止穿透
                else if (thing.def.Fillage == FillCategory.Full && thing.def.blockLight && !(thing is Pawn) && !(thing is Building))
                {
                    break;
                }
            }
            
            this.Destroy(DestroyMode.Vanish);
        }

        private void ApplyPathDamage(Thing targetThing, Wula_BeamPierce_Extension props) // 接受 Thing 参数
        {
            float damageMultiplier = 1f;
            if (targetThing is Pawn) // 只有 Pawn 才计算穿透衰减
            {
                damageMultiplier = Mathf.Pow(1f - props.damageFalloff, hitCounter);
            }
            // Building 不受穿透衰减影响，或者 Building 的穿透衰减始终为 1 (不衰减)

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
            
            targetThing.TakeDamage(dinfo); // 对 targetThing 造成伤害
            alreadyDamaged.Add(targetThing);

            if (targetThing is Pawn) // 只有 Pawn 才增加 hitCounter
            {
                hitCounter++;
            }
        }
        
        protected override void Tick() { }
        protected override void Impact(Thing hitThing, bool blockedByShield = false) { }
    }
}