using System.Collections.Generic;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace WulaFallenEmpire
{
    public class Building_MapObserver : Building
    {
        public MapParent observedMap;

        private CompPowerTrader compPower;

        // 静态列表跟踪所有活跃的观察者
        public static HashSet<Building_MapObserver> activeObservers = new HashSet<Building_MapObserver>();

        public override void SpawnSetup(Map map, bool respawningAfterLoad)
        {
            base.SpawnSetup(map, respawningAfterLoad);
            compPower = this.TryGetComp<CompPowerTrader>();

            // 如果正在观察地图且建筑正常，注册到活跃列表
            if (observedMap != null && (compPower == null || compPower.PowerOn))
            {
                activeObservers.Add(this);
            }
        }

        public override void DeSpawn(DestroyMode mode = DestroyMode.Vanish)
        {
            // 建筑被销毁时停止监测
            DisposeObservedMapIfEmpty();
            activeObservers.Remove(this);
            base.DeSpawn(mode);
        }

        public override IEnumerable<Gizmo> GetGizmos()
        {
            foreach (Gizmo gizmo in base.GetGizmos())
            {
                yield return gizmo;
            }

            // 只有在有电力且属于玩家时才显示控制按钮
            if (Faction == Faction.OfPlayer && (compPower == null || compPower.PowerOn))
            {
                // 开始监测按钮
                yield return new Command_Action
                {
                    defaultLabel = "开始监测地图",
                    defaultDesc = "选择一个世界位置进行监测",
                    icon = ContentFinder<Texture2D>.Get("UI/Commands/ShowMap"),
                    action = delegate
                    {
                        CameraJumper.TryShowWorld();
                        Find.WorldTargeter.BeginTargeting(ChooseWorldTarget, canTargetTiles: true);
                    }
                };

                // 如果正在监测地图，显示停止按钮
                if (observedMap != null)
                {
                    if (observedMap.Destroyed)
                    {
                        observedMap = null;
                        activeObservers.Remove(this);
                    }
                    else
                    {
                        yield return new Command_Action
                        {
                            defaultLabel = "停止监测",
                            defaultDesc = $"停止监测 {observedMap.Label}",
                            icon = ContentFinder<Texture2D>.Get("UI/Commands/DesirePower"),
                            action = delegate
                            {
                                StopObserving();
                            }
                        };
                    }
                }
            }
        }

        private bool ChooseWorldTarget(GlobalTargetInfo target)
        {
            DisposeObservedMapIfEmpty();

            if (target.WorldObject != null && target.WorldObject is MapParent mapParent)
            {
                // 开始监测选中的地图
                observedMap = mapParent;
                activeObservers.Add(this);

                // 确保地图被生成并取消迷雾
                LongEventHandler.QueueLongEvent(delegate
                {
                    Map map = GetOrGenerateMapUtility.GetOrGenerateMap(target.Tile, null);
                    if (map != null)
                    {
                        // 取消迷雾获得完整视野
                        map.fogGrid.ClearAllFog();

                        // 记录日志以便调试
                        Log.Message($"[MapObserver] 开始监测地图: {mapParent.Label} at tile {target.Tile}");
                    }
                }, "GeneratingMap", doAsynchronously: false, null);

                return true;
            }

            // 在空地创建新监测点
            if (target.WorldObject == null && !Find.World.Impassable(target.Tile))
            {
                LongEventHandler.QueueLongEvent(delegate
                {
                    // 创建新的玩家定居点用于监测
                    SettleUtility.AddNewHome(target.Tile, Faction.OfPlayer);
                    Map map = GetOrGenerateMapUtility.GetOrGenerateMap(target.Tile, Find.World.info.initialMapSize, null);
                    observedMap = map.Parent;
                    activeObservers.Add(this);

                    // 取消迷雾获得完整视野
                    map.fogGrid.ClearAllFog();

                    // 设置监测点名称
                    if (observedMap is Settlement settlement)
                    {
                        settlement.Name = $"监测点-{thingIDNumber}";
                        Log.Message($"[MapObserver] 创建新监测点: {settlement.Name} at tile {target.Tile}");
                    }
                    else
                    {
                        // 如果observedMap不是Settlement，使用Label属性
                        Log.Message($"[MapObserver] 创建新监测点: {observedMap.Label} at tile {target.Tile}");
                    }

                }, "GeneratingMap", doAsynchronously: false, null);

                return true;
            }

            Messages.Message("无法监测该位置", MessageTypeDefOf.RejectInput);
            return false;
        }

        private void StopObserving()
        {
            DisposeObservedMapIfEmpty();
            observedMap = null;
            activeObservers.Remove(this);
        }

        private void DisposeObservedMapIfEmpty()
        {
            if (observedMap != null && observedMap.Map != null &&
                !observedMap.Map.mapPawns.AnyColonistSpawned &&
                !observedMap.Map.listerBuildings.allBuildingsColonist.Any() &&
                observedMap.Faction == Faction.OfPlayer)
            {
                // 只有在没有殖民者、没有玩家建筑的情况下才销毁
                Current.Game.DeinitAndRemoveMap(observedMap.Map, notifyPlayer: false);
                if (!observedMap.Destroyed)
                {
                    Find.World.worldObjects.Remove(observedMap);
                }
                Log.Message($"[MapObserver] 清理空置监测地图: {observedMap.Label}");
            }
        }

        protected override void ReceiveCompSignal(string signal)
        {
            base.ReceiveCompSignal(signal);

            // 断电或被关闭时停止监测
            if (observedMap != null && (signal == "PowerTurnedOff" || signal == "FlickedOff"))
            {
                Log.Message($"[MapObserver] 电力中断，停止监测: {observedMap.Label}");
                StopObserving();
            }
            // 恢复电力时重新注册
            else if (observedMap != null && (signal == "PowerTurnedOn" || signal == "FlickedOn"))
            {
                activeObservers.Add(this);
                Log.Message($"[MapObserver] 电力恢复，继续监测: {observedMap.Label}");
            }
        }

        public override string GetInspectString()
        {
            string text = base.GetInspectString();

            if (observedMap != null)
            {
                if (!text.NullOrEmpty())
                {
                    text += "\n";
                }
                text += $"正在监测: {observedMap.Label}";

                // 显示电力状态
                if (compPower != null && !compPower.PowerOn)
                {
                    text += " (电力中断)";
                }
            }

            return text;
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_References.Look(ref observedMap, "observedMap");

            // 加载后重新注册到活跃列表
            if (Scribe.mode == LoadSaveMode.PostLoadInit && observedMap != null && (compPower == null || compPower.PowerOn))
            {
                activeObservers.Add(this);
            }
        }

        // 检查这个观察者是否正在监测指定的地图
        public bool IsObservingMap(MapParent mapParent)
        {
            return observedMap == mapParent && (compPower == null || compPower.PowerOn);
        }
    }
}