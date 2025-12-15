using System;
using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace WulaFallenEmpire
{
    public class CompAbilityEffect_DRM_LightningBombardment : CompAbilityEffect
    {
        public new CompProperties_AbilityDRM_LightningBombardment Props
        {
            get => (CompProperties_AbilityDRM_LightningBombardment)props;
        }

        public override void Apply(LocalTargetInfo target, LocalTargetInfo dest)
        {
            base.Apply(target, dest);
            Map map = parent.pawn.MapHeld;

            // 获取或创建地图组件
            MapComponent_LightningBombardment comp = GetOrCreateMapComponent(map);

            // 启动轰炸任务
            comp.StartBombardment(
                target: target.Cell,
                instigator: parent.pawn,
                explosionCount: Props.explosionCount,
                bombIntervalTicks: Props.bombIntervalTicks,
                impactAreaRadius: Props.impactAreaRadius,
                explosionRadiusRange: Props.explosionRadiusRange,
                damageDef: Props.damageDef,
                damageAmount: Props.damageAmount,
                armorPenetration: Props.armorPenetration,
                postExplosionSpawnThingDef: Props.postExplosionSpawnThingDef,
                postExplosionSpawnChance: Props.postExplosionSpawnChance,
                postExplosionSpawnThingCount: Props.postExplosionSpawnThingCount
            );

            // 播放启动音效
            SoundDefOf.Thunder_OffMap.PlayOneShotOnCamera(map);
        }

        private MapComponent_LightningBombardment GetOrCreateMapComponent(Map map)
        {
            MapComponent_LightningBombardment comp = map.GetComponent<MapComponent_LightningBombardment>();
            if (comp == null)
            {
                comp = new MapComponent_LightningBombardment(map);
                map.components.Add(comp);
            }
            return comp;
        }

        public override void DrawEffectPreview(LocalTargetInfo target)
        {
            // 显示施法范围
            float castingRange = parent.verb.verbProps.range;
            GenDraw.DrawRadiusRing(parent.pawn.Position, castingRange, Color.white);

            // 显示爆炸作用范围
            GenDraw.DrawRadiusRing(target.Cell, Props.impactAreaRadius, Color.white);
            if (target.IsValid) GenDraw.DrawTargetHighlight(target);
        }
    }

    public class CompProperties_AbilityDRM_LightningBombardment : CompProperties_AbilityEffect
    {
        public CompProperties_AbilityDRM_LightningBombardment()
        {
            compClass = typeof(CompAbilityEffect_DRM_LightningBombardment);
        }

        public float impactAreaRadius = 10f;
        public FloatRange explosionRadiusRange = new FloatRange(3f, 4f);
        public int bombIntervalTicks = 30;
        public int explosionCount = 3;

        public DamageDef damageDef;
        public int damageAmount = 30;
        public float armorPenetration = 0.8f;

        public ThingDef postExplosionSpawnThingDef;
        public float postExplosionSpawnChance;
        public int postExplosionSpawnThingCount;
    }

    // 地图组件管理轰炸任务
    public class MapComponent_LightningBombardment : MapComponent
    {
        private readonly List<BombardmentCoroutine> activeCoroutines = new List<BombardmentCoroutine>();
        private const int MAX_CONCURRENT_BOMBARDMENTS = 5;

        public MapComponent_LightningBombardment(Map map) : base(map) { }

        public void StartBombardment(
            IntVec3 target,
            Pawn instigator,
            int explosionCount,
            int bombIntervalTicks,
            float impactAreaRadius,
            FloatRange explosionRadiusRange,
            DamageDef damageDef,
            int damageAmount,
            float armorPenetration,
            ThingDef postExplosionSpawnThingDef,
            float postExplosionSpawnChance,
            int postExplosionSpawnThingCount)
        {
            // 防止过多任务影响性能
            if (activeCoroutines.Count >= MAX_CONCURRENT_BOMBARDMENTS)
            {
                WulaLog.Debug($"Too many concurrent bombardments on map {map}, max is {MAX_CONCURRENT_BOMBARDMENTS}");
                return;
            }

            activeCoroutines.Add(new BombardmentCoroutine(
                target: target,
                map: map,
                instigator: instigator,
                explosionCount: explosionCount,
                bombIntervalTicks: bombIntervalTicks,
                impactAreaRadius: impactAreaRadius,
                explosionRadiusRange: explosionRadiusRange,
                damageDef: damageDef,
                damageAmount: damageAmount,
                armorPenetration: armorPenetration,
                postExplosionSpawnThingDef: postExplosionSpawnThingDef,
                postExplosionSpawnChance: postExplosionSpawnChance,
                postExplosionSpawnThingCount: postExplosionSpawnThingCount
            ));
        }

        public override void MapComponentTick()
        {
            try
            {
                for (int i = activeCoroutines.Count - 1; i >= 0; i--)
                {
                    if (!activeCoroutines[i].Tick())
                    {
                        activeCoroutines.RemoveAt(i);
                    }
                }
            }
            catch (Exception ex)
            {
                WulaLog.Debug($"Lightning bombardment error: {ex}");
                activeCoroutines.Clear();
            }
        }
    }

    [StaticConstructorOnStartup]
    // 轰炸任务协程
    public class BombardmentCoroutine
    {
        private readonly IntVec3 target;
        private readonly Map map;
        private readonly Pawn instigator;
        private readonly int explosionCount;
        private readonly FloatRange explosionRadiusRange;
        private readonly float impactAreaRadius;
        private readonly int bombIntervalTicks;

        public DamageDef damageDef;
        public int damageAmount;
        public float armorPenetration;
        public ThingDef postExplosionSpawnThingDef;
        public float postExplosionSpawnChance;
        public int postExplosionSpawnThingCount;

        private int nextBombTick;
        private int explosionsRemaining;

        private static readonly Material LightningMat = MatLoader.LoadMat("Weather/LightningBolt", -1);

        public BombardmentCoroutine(
            IntVec3 target,
            Map map,
            Pawn instigator,
            int explosionCount,
            int bombIntervalTicks,
            float impactAreaRadius,
            FloatRange explosionRadiusRange,
            DamageDef damageDef,
            int damageAmount,
            float armorPenetration,
            ThingDef postExplosionSpawnThingDef,
            float postExplosionSpawnChance,
            int postExplosionSpawnThingCount)
        {
            this.target = target;
            this.map = map;
            this.instigator = instigator;
            this.explosionCount = explosionCount;
            this.bombIntervalTicks = bombIntervalTicks;
            this.impactAreaRadius = impactAreaRadius;
            this.explosionRadiusRange = explosionRadiusRange;
            this.damageDef = damageDef;
            this.damageAmount = damageAmount;
            this.armorPenetration = armorPenetration;
            this.postExplosionSpawnThingDef = postExplosionSpawnThingDef;
            this.postExplosionSpawnChance = postExplosionSpawnChance;
            this.postExplosionSpawnThingCount = postExplosionSpawnThingCount;

            explosionsRemaining = explosionCount;
            nextBombTick = Find.TickManager.TicksGame + bombIntervalTicks;
        }

        public bool Tick()
        {
            if (Find.TickManager.TicksGame >= nextBombTick)
            {
                ExecuteBomb();
                explosionsRemaining--;

                if (explosionsRemaining > 0)
                {
                    nextBombTick += bombIntervalTicks;
                }
            }
            return explosionsRemaining > 0;
        }

        private void ExecuteBomb()
        {
            // 在轰炸区域内随机选择目标点
            IntVec3 bombTarget = GetRandomCellInRadius(target, map, impactAreaRadius);

            // 创建闪电视觉效果
            CreateLightningEffect(bombTarget);

            // 执行爆炸
            GenExplosion.DoExplosion(
                center: bombTarget,
                map: map,
                radius: explosionRadiusRange.RandomInRange,
                damType: damageDef,
                instigator: instigator,
                damAmount: damageAmount,
                armorPenetration: armorPenetration,
                postExplosionSpawnThingDef: postExplosionSpawnThingDef,
                postExplosionSpawnChance: postExplosionSpawnChance,
                postExplosionSpawnThingCount: postExplosionSpawnThingCount,
                applyDamageToExplosionCellsNeighbors: true,
                chanceToStartFire: 0.4f,
                damageFalloff: true
            );
        }

        private IntVec3 GetRandomCellInRadius(IntVec3 center, Map map, float radius)
        {
            if (radius <= 0.0f) return center;

            if (CellFinder.TryFindRandomCellNear(
                center,
                map,
                Mathf.CeilToInt(radius),
                c => c.DistanceTo(center) <= radius && c.Standable(map),
                out IntVec3 result))
            {
                return result;
            }
            return center; // 备用方案
        }

        private void CreateLightningEffect(IntVec3 strikeLoc)
        {
            if (strikeLoc.Fogged(map)) return;

            Vector3 position = strikeLoc.ToVector3Shifted();

            // 1. 播放声音
            SoundDefOf.Thunder_OffMap.PlayOneShotOnCamera(map);

            // 2. 生成粒子效果
            for (int i = 0; i < 4; i++)
            {
                FleckMaker.ThrowSmoke(position, map, 1.5f);
                FleckMaker.ThrowMicroSparks(position, map);
                FleckMaker.ThrowLightningGlow(position, map, 1.5f);
            }

            // 3. 绘制闪电网格
            Mesh boltMesh = LightningBoltMeshPool.RandomBoltMesh;
            Graphics.DrawMesh(
                boltMesh,
                strikeLoc.ToVector3ShiftedWithAltitude(AltitudeLayer.Weather),
                Quaternion.identity,
                FadedMaterialPool.FadedVersionOf(LightningMat, 1f),
                0
            );

            // 4. 播放局部雷声
            SoundInfo soundInfo = SoundInfo.InMap(new TargetInfo(strikeLoc, map));
            SoundDefOf.Thunder_OnMap.PlayOneShot(soundInfo);
        }
    }
}
