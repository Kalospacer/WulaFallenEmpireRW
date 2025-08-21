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

        // 新增的弹道配置参数
        public float bezierArcHeightFactor = 0.05f; // 贝塞尔曲线高度因子
        public float bezierMinArcHeight = 2f;       // 贝塞尔曲线最小高度
        public float bezierMaxArcHeight = 6f;       // 贝塞尔曲线最大高度
        public float bezierHorizontalOffsetFactor = 0.1f; // 贝塞尔曲线水平偏移因子
        public float bezierSideOffsetFactor = 0.2f;   // 贝塞尔曲线侧向偏移因子
        public float bezierRandomOffsetScale = 0.5f;  // 贝塞尔曲线随机偏移缩放

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
            // 调整随机范围，用于控制C形弹道的随机偏移
            Randdd = new Vector3(
                Rand.Range(-settings.bezierRandomOffsetScale, settings.bezierRandomOffsetScale), // X轴的随机偏移
                Rand.Range(0f, 0f),      // Y轴（高度）不进行随机，保持平稳
                Rand.Range(-settings.bezierRandomOffsetScale, settings.bezierRandomOffsetScale)  // Z轴的随机偏移
            );
            flag2 = true;
        }

        public Vector3 BPos(float t)
        {
            if (!flag2) RandFactor();

            // 计算水平距离
            float horizontalDistance = Vector3.Distance(new Vector3(origin.x, 0, origin.z),
                new Vector3(destination.x, 0, destination.z));

            // 动态调整控制点高度，使其更扁平，使用XML配置的高度因子
            float arcHeight = Mathf.Clamp(horizontalDistance * settings.bezierArcHeightFactor, settings.bezierMinArcHeight, settings.bezierMaxArcHeight);

            // 计算从起点到终点的方向向量
            Vector3 direction = (destination - origin).normalized;
            // 计算垂直于方向向量的水平向量（用于侧向偏移），确保C形弯曲方向一致
            Vector3 perpendicularDirection = Vector3.Cross(direction, Vector3.up).normalized;

            // 调整控制点以形成扁平 C 形，使用XML配置的偏移因子
            // P1: 在起点附近，向前偏移，向上偏移，并向一侧偏移
            Vector3 p1 = origin + direction * horizontalDistance * settings.bezierHorizontalOffsetFactor + Vector3.up * arcHeight + perpendicularDirection * horizontalDistance * settings.bezierSideOffsetFactor + Randdd;
            // P2: 在终点附近，向后偏移，向上偏移，并向同一侧偏移
            Vector3 p2 = destination - direction * horizontalDistance * settings.bezierHorizontalOffsetFactor + Vector3.up * arcHeight + perpendicularDirection * horizontalDistance * settings.bezierSideOffsetFactor + Randdd;

            return BezierCurve(origin, p1, p2, destination, t);
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