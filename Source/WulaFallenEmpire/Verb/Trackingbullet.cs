using RimWorld;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Verse;

namespace WulaFallenEmpire
{
    public class CruiseMissileProperties : DefModExtension
    {
        public DamageDef customDamageDef;
        public int customDamageAmount = 5;
        public float customExplosionRadius = 1.1f;
        public SoundDef customSoundExplode;

        public bool useSubExplosions = true;
        public int subExplosionCount = 3;
        public float subExplosionRadius = 1.9f;
        public int subExplosionDamage = 30;
        public float subExplosionSpread = 6f;
        public DamageDef subDamageDef;
        public SoundDef subSoundExplode;

    }

    public class Projectile_CruiseMissile : Projectile_Explosive
    {
        private CruiseMissileProperties settings;
        private bool flag2;
        private Vector3 Randdd;
        private Vector3 position2;
        public Vector3 ExPos;

        public override void SpawnSetup(Map map, bool respawningAfterLoad)
        {
            base.SpawnSetup(map, respawningAfterLoad);
            settings = def.GetModExtension<CruiseMissileProperties>() ?? new CruiseMissileProperties();
        }

        private void RandFactor()
        {
            // 减少垂直方向随机性，调整水平随机范围
            Randdd = new Vector3(
                Rand.Range(-3f, 3f),  // 减小水平随机范围
                Rand.Range(8f, 12f), // 降低基础高度
                Rand.Range(-3f, 3f)
            );
            flag2 = true;
        }

        public Vector3 BPos(float t)
        {
            if (!flag2) RandFactor();

            // 计算水平距离
            float horizontalDistance = Vector3.Distance(new Vector3(origin.x, 0, origin.z),
                new Vector3(destination.x, 0, destination.z));

            // 动态调整控制点高度
            float arcHeight = Mathf.Clamp(horizontalDistance * 0.2f, 8f, 15f);

            Vector3 a = origin + Vector3.forward * horizontalDistance * 0.2f + new Vector3(0f, arcHeight, 0f);
            Vector3 a2 = destination - Vector3.forward * horizontalDistance * 0.2f + new Vector3(0f, arcHeight, 0f);

            return BezierCurve(origin, a, a2, destination, t);
        }

        private Vector3 BezierCurve(Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3, float t)
        {
            float u = 1 - t;
            return u * u * u * p0
                + 3 * u * u * t * p1
                + 3 * u * t * t * p2
                + t * t * t * p3;
        }

        private IEnumerable<IntVec3> GetValidCells(Map map)
        {
            if (map == null || settings == null) yield break;

            var cells = GenRadial.RadialCellsAround(
                base.Position,
                settings.subExplosionSpread,
                false
            ).Where(c => c.InBounds(map));

            var randomizedCells = cells.InRandomOrder().Take(settings.subExplosionCount);

            foreach (var cell in randomizedCells)
            {
                yield return cell;
            }
        }

        protected override void Impact(Thing hitThing, bool blockedByShield = false)
        {
            var map = base.Map;
            base.Impact(hitThing, blockedByShield);

            DoExplosion(
                base.Position,
                map,
                settings.customExplosionRadius,
                settings.customDamageDef,
                settings.customDamageAmount,
                settings.customSoundExplode
            );

            if (settings.useSubExplosions)
            {
                foreach (var cell in GetValidCells(map))
                {
                    DoExplosion(
                        cell,
                        map,
                        settings.subExplosionRadius,
                        settings.subDamageDef,
                        settings.subExplosionDamage,
                        settings.subSoundExplode
                    );
                }
            }
        }

        private void DoExplosion(IntVec3 pos, Map map, float radius, DamageDef dmgDef, int dmgAmount, SoundDef sound)
        {
            GenExplosion.DoExplosion(
                pos,
                map,
                radius,
                dmgDef,
                launcher,
                dmgAmount,
                ArmorPenetration,
                sound
            );
        }

        protected override void DrawAt(Vector3 position, bool flip = false)
        {
            position2 = BPos(DistanceCoveredFraction - 0.01f);
            ExPos = position = BPos(DistanceCoveredFraction);
            base.DrawAt(position, flip);
        }

        protected override void Tick()
        {
            if (intendedTarget.Thing is Pawn pawn && pawn.Spawned && !pawn.Destroyed)
            {
                if ((pawn.Dead || pawn.Downed) && DistanceCoveredFraction < 0.6f)
                {
                    FindNextTarget(pawn.DrawPos);
                }
                destination = pawn.DrawPos;
            }
            base.Tick();
        }

        private void FindNextTarget(Vector3 center)
        {
            var map = base.Map;
            if (map == null) return;

            foreach (IntVec3 cell in GenRadial.RadialCellsAround(IntVec3.FromVector3(center), 7f, true))
            {
                if (!cell.InBounds(map)) continue;

                Pawn target = cell.GetFirstPawn(map);
                if (target != null && target.Faction.HostileTo(launcher?.Faction))
                {
                    intendedTarget = target;
                    return;
                }
            }
            intendedTarget = CellRect.CenteredOn(IntVec3.FromVector3(center), 7).RandomCell;
        }
    }
}