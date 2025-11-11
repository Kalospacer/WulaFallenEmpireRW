using RimWorld.Planet;
using UnityEngine;
using Verse;
using RimWorld;
using System.Collections.Generic;

namespace WulaFallenEmpire
{
    public class WorldObject_FlyOverCarrier : WorldObject
    {
        public int destinationTile = -1;
        public FlyOverConfig flyOverConfig;
        public Building_FlyOverBeacon sourceBeacon;
        
        private int initialTile = -1;
        private float traveledPct;
        private const float TravelSpeed = 0.0001f; // 比导弹慢，适合侦察

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref destinationTile, "destinationTile", -1);
            Scribe_Deep.Look(ref flyOverConfig, "flyOverConfig");
            Scribe_References.Look(ref sourceBeacon, "sourceBeacon");
            Scribe_Values.Look(ref initialTile, "initialTile", -1);
            Scribe_Values.Look(ref traveledPct, "traveledPct", 0f);
        }

        public override void PostAdd()
        {
            base.PostAdd();
            this.initialTile = this.Tile;
            Log.Message($"[FlyOverCarrier] Launched from tile {initialTile} to {destinationTile}");
        }
        
        private Vector3 StartPos => Find.WorldGrid.GetTileCenter(this.initialTile);
        private Vector3 EndPos => Find.WorldGrid.GetTileCenter(this.destinationTile);

        public override Vector3 DrawPos => Vector3.Slerp(StartPos, EndPos, traveledPct);

        protected override void Tick()
        {
            base.Tick();

            if (this.destinationTile < 0)
            {
                Log.Error("FlyOverCarrier has invalid destination tile");
                Find.WorldObjects.Remove(this);
                return;
            }

            float distance = GenMath.SphericalDistance(StartPos.normalized, EndPos.normalized);
            if (distance > 0)
            {
                traveledPct += TravelSpeed / distance;
            }
            else
            {
                traveledPct = 1;
            }

            // 更新世界图标位置
            if (Find.WorldRenderer != null)
            {
                Find.WorldRenderer.Notify_WorldObjectPosChanged(this);
            }

            if (traveledPct >= 1f)
            {
                Arrived();
            }
        }

        private void Arrived()
        {
            Log.Message($"[FlyOverCarrier] Arrived at destination tile {destinationTile}");

            Map targetMap = GetTargetMap();
            if (targetMap != null)
            {
                CreateFlyOverInTargetMap(targetMap);
            }
            else
            {
                Log.Warning($"[FlyOverCarrier] Could not find or generate map for tile {destinationTile}");
            }

            // 通知源信标任务完成
            if (sourceBeacon != null && !sourceBeacon.Destroyed)
            {
                Messages.Message($"飞越单位已到达 {flyOverConfig.targetMap?.Parent?.Label ?? "目标区域"}", 
                    sourceBeacon, MessageTypeDefOf.PositiveEvent);
            }

            Find.WorldObjects.Remove(this);
        }

        private Map GetTargetMap()
        {
            // 优先使用配置中的目标地图
            if (flyOverConfig.targetMap != null && !flyOverConfig.targetMap.Destroyed)
            {
                return flyOverConfig.targetMap;
            }

            // 生成临时地图
            return GetOrGenerateMapUtility.GetOrGenerateMap(destinationTile, Find.World.info.initialMapSize, null);
        }

        private void CreateFlyOverInTargetMap(Map targetMap)
        {
            if (flyOverConfig.flyOverDef == null)
            {
                Log.Warning("[FlyOverCarrier] No fly over def specified, using default");
                flyOverConfig.flyOverDef = DefDatabase<ThingDef>.GetNamedSilentFail("ARA_HiveScout");
            }

            // 确保目标地图有视野
            targetMap.fogGrid.ClearAllFog();

            // 验证并调整飞越路径
            IntVec3 startPos = ValidateAndAdjustPosition(flyOverConfig.startPos, targetMap);
            IntVec3 endPos = ValidateAndAdjustPosition(flyOverConfig.endPos, targetMap);

            Log.Message($"[FlyOverCarrier] Creating flyover: {startPos} -> {endPos} in {targetMap}");

            // 创建飞越物体
            FlyOver flyOver = FlyOver.MakeFlyOver(
                flyOverConfig.flyOverDef,
                startPos,
                endPos,
                targetMap,
                flyOverConfig.flightSpeed,
                flyOverConfig.altitude,
                casterPawn: null
            );

            // 配置飞越属性
            flyOver.spawnContentsOnImpact = flyOverConfig.dropContentsOnImpact;
            flyOver.playFlyOverSound = true;
            flyOver.faction = sourceBeacon?.Faction;

            // 配置特殊组件
            ConfigureFlyOverComponents(flyOver);

            // 创建到达视觉效果
            CreateArrivalEffects(targetMap, startPos);
        }

        private IntVec3 ValidateAndAdjustPosition(IntVec3 pos, Map map)
        {
            if (pos.IsValid && pos.InBounds(map))
                return pos;

            // 如果位置无效，使用地图边缘位置
            return CellFinder.RandomEdgeCell(Rand.Range(0, 4), map);
        }

        private void ConfigureFlyOverComponents(FlyOver flyOver)
        {
            // 地面扫射配置
            if (flyOverConfig.enableStrafing)
            {
                CompGroundStrafing strafingComp = flyOver.GetComp<CompGroundStrafing>();
                if (strafingComp != null)
                {
                    // 计算扫射区域
                    Vector3 flightDirection = (flyOverConfig.endPos.ToVector3() - flyOverConfig.startPos.ToVector3()).normalized;
                    List<IntVec3> impactCells = CalculateStrafingImpactCells(flyOverConfig.targetCell, flightDirection);
                    List<IntVec3> confirmedTargets = PreprocessStrafingTargets(impactCells, 0.7f);
                    
                    strafingComp.SetConfirmedTargets(confirmedTargets);
                }
            }

            // 监视功能配置
            if (flyOverConfig.enableSurveillance)
            {
                // 可以在这里添加监视组件的配置
                Log.Message("[FlyOverCarrier] Surveillance mode configured");
            }
        }

        private List<IntVec3> CalculateStrafingImpactCells(IntVec3 targetCell, Vector3 flightDirection)
        {
            // 简化的扫射区域计算
            List<IntVec3> cells = new List<IntVec3>();
            Map map = Find.CurrentMap;

            if (map != null)
            {
                Vector3 perpendicular = new Vector3(-flightDirection.z, 0f, flightDirection.x).normalized;
                
                for (int i = -2; i <= 2; i++)
                {
                    for (int j = -5; j <= 5; j++)
                    {
                        Vector3 offset = perpendicular * i + flightDirection * j;
                        IntVec3 cell = targetCell + new IntVec3((int)offset.x, 0, (int)offset.z);
                        
                        if (cell.InBounds(map))
                        {
                            cells.Add(cell);
                        }
                    }
                }
            }

            return cells;
        }

        private List<IntVec3> PreprocessStrafingTargets(List<IntVec3> potentialTargets, float fireChance)
        {
            List<IntVec3> confirmedTargets = new List<IntVec3>();
            
            foreach (IntVec3 cell in potentialTargets)
            {
                if (Rand.Value <= fireChance)
                {
                    confirmedTargets.Add(cell);
                }
            }

            return confirmedTargets;
        }

        private void CreateArrivalEffects(Map targetMap, IntVec3 entryPos)
        {
            // 进入视觉效果
            MoteMaker.MakeStaticMote(entryPos.ToVector3Shifted(), targetMap, ThingDefOf.Mote_PsycastAreaEffect, 2f);
            
            for (int i = 0; i < 5; i++)
            {
                FleckMaker.ThrowAirPuffUp(entryPos.ToVector3Shifted(), targetMap);
            }

            // 进入音效
            SoundDefOf.PsychicPulse.PlayOneShot(new TargetInfo(entryPos, targetMap));
        }
    }
}
