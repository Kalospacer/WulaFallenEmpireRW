using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Verse;
using Verse.AI;
using Verse.Sound;
using RimWorld;
using RimWorld.Planet;

namespace WulaFallenEmpire
{
    [StaticConstructorOnStartup]
    public class Building_FlyOverBeacon : Building
    {
        public GlobalTargetInfo flyOverTarget;
        public FlyOverConfig flyOverConfig;
        public static readonly Texture2D CallFlyOverTex = ContentFinder<Texture2D>.Get("UI/Commands/CallFlyOver", true);
        
        private CompPowerTrader powerComp;
        private CompRefuelable refuelableComp;
        private int cooldownTicksLeft;

        public bool IsReady => (powerComp == null || powerComp.PowerOn) && 
                              (refuelableComp == null || refuelableComp.HasFuel) && 
                              cooldownTicksLeft <= 0;

        public override void SpawnSetup(Map map, bool respawningAfterLoad)
        {
            base.SpawnSetup(map, respawningAfterLoad);
            powerComp = this.TryGetComp<CompPowerTrader>();
            refuelableComp = this.TryGetComp<CompRefuelable>();
            
            if (!respawningAfterLoad)
            {
                flyOverTarget = GlobalTargetInfo.Invalid;
                flyOverConfig = new FlyOverConfig();
            }
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_TargetInfo.Look(ref flyOverTarget, "flyOverTarget");
            Scribe_Deep.Look(ref flyOverConfig, "flyOverConfig");
            Scribe_Values.Look(ref cooldownTicksLeft, "cooldownTicksLeft", 0);
        }

        protected override void Tick()
        {
            base.Tick();

            // 冷却计时
            if (cooldownTicksLeft > 0)
                cooldownTicksLeft--;

            // 自动执行已设定的飞越任务
            if (flyOverTarget.IsValid && IsReady)
            {
                ExecuteFlyOverMission();
            }
        }

        public override string GetInspectString()
        {
            StringBuilder sb = new StringBuilder(base.GetInspectString());
            
            if (cooldownTicksLeft > 0)
            {
                if (sb.Length > 0) sb.AppendLine();
                sb.Append("飞越信标冷却中: ".Translate() + cooldownTicksLeft.ToStringTicksToPeriod());
            }

            if (flyOverTarget.IsValid)
            {
                if (sb.Length > 0) sb.AppendLine();
                sb.Append("已锁定目标: ".Translate() + flyOverTarget.Label);
            }

            if (!IsReady)
            {
                if (sb.Length > 0) sb.AppendLine();
                if (powerComp != null && !powerComp.PowerOn)
                    sb.Append("需要电力".Translate());
                else if (refuelableComp != null && !refuelableComp.HasFuel)
                    sb.Append("需要燃料".Translate());
                else if (cooldownTicksLeft > 0)
                    sb.Append("冷却中".Translate());
            }

            return sb.ToString();
        }

        public override IEnumerable<Gizmo> GetGizmos()
        {
            foreach (var g in base.GetGizmos())
            {
                yield return g;
            }

            // 召唤飞越命令
            Command_Action callFlyOver = new Command_Action
            {
                defaultLabel = "召唤跨图飞越",
                defaultDesc = "在世界地图上选择目标位置召唤飞越单位",
                icon = CallFlyOverTex,
                action = StartChoosingFlyOverTarget
            };
            
            if (!IsReady)
            {
                callFlyOver.Disable(GetDisabledReason());
            }
            yield return callFlyOver;

            // 配置飞越参数命令
            if (flyOverTarget.IsValid)
            {
                Command_Action configureFlyOver = new Command_Action
                {
                    defaultLabel = "配置飞越参数",
                    defaultDesc = "调整飞越单位的类型和行为",
                    icon = ContentFinder<Texture2D>.Get("UI/Commands/Configure", true),
                    action = OpenConfigurationDialog
                };
                yield return configureFlyOver;

                // 清除目标命令
                Command_Action clearTarget = new Command_Action
                {
                    defaultLabel = "清除目标",
                    defaultDesc = "清除已锁定的飞越目标",
                    icon = ContentFinder<Texture2D>.Get("UI/Designators/Cancel", true),
                    action = () => flyOverTarget = GlobalTargetInfo.Invalid
                };
                yield return clearTarget;
            }
        }

        private string GetDisabledReason()
        {
            if (powerComp != null && !powerComp.PowerOn)
                return "需要电力";
            if (refuelableComp != null && !refuelableComp.HasFuel)
                return "需要燃料";
            if (cooldownTicksLeft > 0)
                return "冷却中";
            return "无法使用";
        }

        private void StartChoosingFlyOverTarget()
        {
            CameraJumper.TryJump(CameraJumper.GetWorldTarget(this));
            Find.WorldSelector.ClearSelection();
            
            Find.WorldTargeter.BeginTargeting(
                ChooseWorldTarget,
                canTargetTiles: true,
                targeterMouseAttachment: CallFlyOverTex,
                extraLabelGetter: GetExtraTargetingLabel
            );
        }

        private string GetExtraTargetingLabel(GlobalTargetInfo target)
        {
            if (target.IsValid)
            {
                return "召唤飞越至: " + target.Label;
            }
            return "选择飞越目标位置";
        }

        private bool ChooseWorldTarget(GlobalTargetInfo target)
        {
            if (!target.IsValid)
            {
                Messages.Message("无效的目标位置", MessageTypeDefOf.RejectInput);
                return false;
            }

            if (target.Tile == this.Map.Tile)
            {
                Messages.Message("无法在同一地图召唤飞越", MessageTypeDefOf.RejectInput);
                return false;
            }

            // 处理不同类型的目标
            if (target.WorldObject is MapParent mapParent && mapParent.HasMap)
            {
                // 切换到目标地图选择具体飞越路径
                var originalMap = this.Map;
                Action onFinished = () => {
                    if (Current.Game.CurrentMap != originalMap) 
                        Current.Game.CurrentMap = originalMap;
                };

                Current.Game.CurrentMap = mapParent.Map;
                Find.Targeter.BeginTargeting(
                    new TargetingParameters 
                    { 
                        canTargetLocations = true,
                        canTargetPawns = false,
                        canTargetBuildings = false,
                        mapObjectTargetsMustBeAutoAttackable = false
                    },
                    (LocalTargetInfo localTarget) =>
                    {
                        // 设置飞越配置
                        flyOverTarget = new GlobalTargetInfo(localTarget.Cell, mapParent.Map);
                        flyOverConfig.targetCell = localTarget.Cell;
                        flyOverConfig.targetMap = mapParent.Map;
                        
                        // 自动计算飞越路径
                        CalculateFlyOverPath(mapParent.Map, localTarget.Cell);
                    },
                    null, onFinished, CallFlyOverTex, true);
            }
            else
            {
                // 空地目标，生成临时地图
                flyOverTarget = target;
                flyOverConfig.targetTile = target.Tile;
                CalculateFlyOverPathForTile(target.Tile);
            }

            return true;
        }

        private void CalculateFlyOverPath(Map targetMap, IntVec3 targetCell)
        {
            // 计算从地图边缘到目标点的飞越路径
            Vector3 targetPos = targetCell.ToVector3();
            
            // 随机选择进入方向
            Vector3[] approachDirections = {
                new Vector3(1, 0, 0),   // 从右边进入
                new Vector3(-1, 0, 0),  // 从左边进入  
                new Vector3(0, 0, 1),   // 从上边进入
                new Vector3(0, 0, -1)   // 从下边进入
            };
            
            Vector3 approachDir = approachDirections.RandomElement();
            Vector3 startPos = FindMapEdgePosition(targetMap, approachDir);
            Vector3 endPos = FindMapEdgePosition(targetMap, -approachDir); // 从对面飞出

            flyOverConfig.startPos = startPos.ToIntVec3();
            flyOverConfig.endPos = endPos.ToIntVec3();
            flyOverConfig.approachType = FlyOverApproachType.StraightLine;
            
            Log.Message($"Calculated flyover path: {flyOverConfig.startPos} -> {targetCell} -> {flyOverConfig.endPos}");
        }

        private void CalculateFlyOverPathForTile(int tile)
        {
            // 为地图瓦片计算默认飞越路径（穿越地图中心）
            Map targetMap = GetOrGenerateMapUtility.GetOrGenerateMap(tile, Find.World.info.initialMapSize, null);
            if (targetMap != null)
            {
                targetMap.fogGrid.ClearAllFog(); // 获得视野
                
                IntVec3 center = targetMap.Center;
                CalculateFlyOverPath(targetMap, center);
            }
        }

        private IntVec3 FindMapEdgePosition(Map map, Vector3 direction)
        {
            // 找到地图边缘位置
            Vector3 center = map.Center.ToVector3();
            Vector3 edgePos = center;
            float maxDistance = Mathf.Max(map.Size.x, map.Size.z) * 0.6f;

            for (int i = 1; i <= maxDistance; i++)
            {
                Vector3 testPos = center + direction.normalized * i;
                IntVec3 testCell = new IntVec3((int)testPos.x, 0, (int)testPos.z);
                
                if (!testCell.InBounds(map))
                {
                    // 找到最近的边界内单元格
                    return FindClosestValidPosition(testCell, map);
                }
            }

            return map.Center;
        }

        private IntVec3 FindClosestValidPosition(IntVec3 invalidPos, Map map)
        {
            for (int radius = 1; radius <= 10; radius++)
            {
                foreach (IntVec3 offset in GenRadial.RadialPatternInRadius(radius))
                {
                    IntVec3 testPos = invalidPos + offset;
                    if (testPos.InBounds(map))
                        return testPos;
                }
            }
            return map.Center;
        }

        private void OpenConfigurationDialog()
        {
            // 打开飞越配置对话框
            List<FloatMenuOption> options = new List<FloatMenuOption>
            {
                new FloatMenuOption("标准侦察飞越", () => ConfigureStandardRecon()),
                new FloatMenuOption("地面扫射飞越", () => ConfigureGroundStrafing()),
                new FloatMenuOption("监视巡逻飞越", () => ConfigureSurveillance()),
                new FloatMenuOption("货运投送飞越", () => ConfigureCargoDrop())
            };

            Find.WindowStack.Add(new FloatMenu(options));
        }

        private void ConfigureStandardRecon()
        {
            flyOverConfig.flyOverType = FlyOverType.Standard;
            flyOverConfig.flyOverDef = DefDatabase<ThingDef>.GetNamedSilentFail("ARA_HiveScout");
            flyOverConfig.altitude = 20f;
            flyOverConfig.flightSpeed = 1.2f;
            Messages.Message("已配置为标准侦察飞越", MessageTypeDefOf.TaskCompletion);
        }

        private void ConfigureGroundStrafing()
        {
            flyOverConfig.flyOverType = FlyOverType.GroundStrafing;
            flyOverConfig.flyOverDef = DefDatabase<ThingDef>.GetNamedSilentFail("ARA_HiveGunship");
            flyOverConfig.altitude = 10f;
            flyOverConfig.flightSpeed = 0.8f;
            flyOverConfig.enableStrafing = true;
            Messages.Message("已配置为地面扫射飞越", MessageTypeDefOf.TaskCompletion);
        }

        private void ConfigureSurveillance()
        {
            flyOverConfig.flyOverType = FlyOverType.Surveillance;
            flyOverConfig.flyOverDef = DefDatabase<ThingDef>.GetNamedSilentFail("ARA_HiveObserver");
            flyOverConfig.altitude = 25f;
            flyOverConfig.flightSpeed = 0.6f;
            flyOverConfig.enableSurveillance = true;
            Messages.Message("已配置为监视巡逻飞越", MessageTypeDefOf.TaskCompletion);
        }

        private void ConfigureCargoDrop()
        {
            flyOverConfig.flyOverType = FlyOverType.CargoDrop;
            flyOverConfig.flyOverDef = DefDatabase<ThingDef>.GetNamedSilentFail("ARA_HiveTransport");
            flyOverConfig.altitude = 15f;
            flyOverConfig.flightSpeed = 1.0f;
            flyOverConfig.dropContentsOnImpact = true;
            Messages.Message("已配置为货运投送飞越", MessageTypeDefOf.TaskCompletion);
        }

        public void ExecuteFlyOverMission()
        {
            if (!IsReady) return;

            try
            {
                Log.Message($"[FlyOverBeacon] Executing flyover mission to {flyOverTarget.Label}");

                // 创建世界飞越载体
                WorldObject_FlyOverCarrier carrier = (WorldObject_FlyOverCarrier)WorldObjectMaker.MakeWorldObject(
                    DefDatabase<WorldObjectDef>.GetNamed("FlyOverCarrier")
                );

                carrier.Tile = this.Map.Tile;
                carrier.destinationTile = flyOverTarget.Tile;
                carrier.flyOverConfig = flyOverConfig.Clone();
                carrier.sourceBeacon = this;
                
                Find.WorldObjects.Add(carrier);

                // 本地视觉效果
                CreateLocalLaunchEffects();

                // 资源消耗
                if (refuelableComp != null)
                    refuelableComp.ConsumeFuel(1);

                // 进入冷却
                cooldownTicksLeft = 6000; // 1分钟冷却

                Messages.Message($"飞越单位已派遣至 {flyOverTarget.Label}", MessageTypeDefOf.PositiveEvent);

            }
            catch (Exception ex)
            {
                Log.Error($"Error executing flyover mission: {ex}");
                Messages.Message("飞越任务执行失败", MessageTypeDefOf.NegativeEvent);
            }
        }

        private void CreateLocalLaunchEffects()
        {
            // 发射视觉效果
            MoteMaker.MakeStaticMote(this.DrawPos, this.Map, ThingDefOf.Mote_ExplosionFlash, 3f);
            
            for (int i = 0; i < 3; i++)
            {
                FleckMaker.ThrowSmoke(this.DrawPos, this.Map, 2f);
                FleckMaker.ThrowLightningGlow(this.DrawPos, this.Map, 1.5f);
            }

            // 发射音效
            SoundDefOf.PsychicPulseGlobal.PlayOneShot(new TargetInfo(this.Position, this.Map));
        }
    }

    // 飞越配置类
    public class FlyOverConfig : IExposable
    {
        public ThingDef flyOverDef;
        public FlyOverType flyOverType = FlyOverType.Standard;
        public IntVec3 startPos;
        public IntVec3 endPos;
        public IntVec3 targetCell;
        public Map targetMap;
        public int targetTile = -1;
        public float flightSpeed = 1f;
        public float altitude = 15f;
        public bool dropContentsOnImpact = false;
        public bool enableStrafing = false;
        public bool enableSurveillance = false;
        public FlyOverApproachType approachType = FlyOverApproachType.StraightLine;

        public void ExposeData()
        {
            Scribe_Defs.Look(ref flyOverDef, "flyOverDef");
            Scribe_Values.Look(ref flyOverType, "flyOverType", FlyOverType.Standard);
            Scribe_Values.Look(ref startPos, "startPos");
            Scribe_Values.Look(ref endPos, "endPos");
            Scribe_Values.Look(ref targetCell, "targetCell");
            Scribe_References.Look(ref targetMap, "targetMap");
            Scribe_Values.Look(ref targetTile, "targetTile", -1);
            Scribe_Values.Look(ref flightSpeed, "flightSpeed", 1f);
            Scribe_Values.Look(ref altitude, "altitude", 15f);
            Scribe_Values.Look(ref dropContentsOnImpact, "dropContentsOnImpact", false);
            Scribe_Values.Look(ref enableStrafing, "enableStrafing", false);
            Scribe_Values.Look(ref enableSurveillance, "enableSurveillance", false);
            Scribe_Values.Look(ref approachType, "approachType", FlyOverApproachType.StraightLine);
        }

        public FlyOverConfig Clone()
        {
            return new FlyOverConfig
            {
                flyOverDef = this.flyOverDef,
                flyOverType = this.flyOverType,
                startPos = this.startPos,
                endPos = this.endPos,
                targetCell = this.targetCell,
                targetMap = this.targetMap,
                targetTile = this.targetTile,
                flightSpeed = this.flightSpeed,
                altitude = this.altitude,
                dropContentsOnImpact = this.dropContentsOnImpact,
                enableStrafing = this.enableStrafing,
                enableSurveillance = this.enableSurveillance,
                approachType = this.approachType
            };
        }
    }

    public enum FlyOverType
    {
        Standard,
        GroundStrafing,
        Surveillance,
        CargoDrop
    }

    public enum FlyOverApproachType
    {
        StraightLine,
        CirclePattern,
        FigureEight
    }
}
