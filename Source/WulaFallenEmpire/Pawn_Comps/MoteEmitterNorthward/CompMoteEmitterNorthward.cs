// File: CompMoteEmitterNorthward.cs
using RimWorld;
using System;
using UnityEngine;
using System.Collections.Generic;
using Verse;
using Verse.Sound;

namespace WulaFallenEmpire
{
    /// <summary>
    /// 组件：持续产生向上（北向）移动的Mote
    /// </summary>
    public class CompMoteEmitterNorthward : ThingComp
    {
        private CompProperties_MoteEmitterNorthward Props =>
            (CompProperties_MoteEmitterNorthward)props;

        private int ticksUntilNextEmit;
        
        // 移动状态跟踪
        private Vector3 lastPosition;
        private bool isMoving;
        private int positionUpdateCooldown = 0;

        // 缓存引用
        private CompMechPilotHolder pilotHolder;
        
        // Pawn引用
        private Pawn pawnParent;
        
        // 是否已销毁标记
        private bool isDestroyed = false;

        public override void Initialize(CompProperties props)
        {
            base.Initialize(props);
            // 随机化初始计时器，避免所有发射器同时发射
            ticksUntilNextEmit = Rand.Range(0, Props.emitIntervalTicks);
            
            // 获取Pawn引用
            pawnParent = parent as Pawn;
        }

        public override void PostSpawnSetup(bool respawningAfterLoad)
        {
            base.PostSpawnSetup(respawningAfterLoad);
            
            // 重置销毁标记
            isDestroyed = false;

            // 获取驾驶员容器组件
            pilotHolder = parent.TryGetComp<CompMechPilotHolder>();

            // 如果需要驾驶员但组件不存在，发出警告
            if (Props.requirePilot && pilotHolder == null)
            {
                Log.Warning($"[WULA] CompMoteEmitterNorthward on {parent} requires pilot but no CompMechPilotHolder found");
            }
            
            // 初始化位置
            if (parent.Spawned)
            {
                lastPosition = GetSafePosition();
            }
        }

        public override void CompTick()
        {
            base.CompTick();
            
            // 如果已标记销毁，跳过所有处理
            if (isDestroyed || parent == null)
                return;

            if (!parent.Spawned || parent.Map == null)
                return;

            // 更新移动状态
            if (positionUpdateCooldown <= 0)
            {
                UpdateMovementState();
                positionUpdateCooldown = 10; // 每10ticks更新一次位置，减少开销
            }
            else
            {
                positionUpdateCooldown--;
            }

            // 检查是否满足发射条件
            if (!CanEmit())
                return;

            ticksUntilNextEmit--;

            if (ticksUntilNextEmit <= 0)
            {
                EmitMote();
                
                // 根据移动状态设置下次发射间隔
                ticksUntilNextEmit = isMoving ? 
                    Props.emitIntervalMovingTicks : 
                    Props.emitIntervalTicks;
            }
        }
        
        /// <summary>
        /// 安全获取当前位置
        /// </summary>
        private Vector3 GetSafePosition()
        {
            try
            {
                if (parent == null || !parent.Spawned)
                    return parent?.Position.ToVector3Shifted() ?? Vector3.zero;
                
                // 如果是Pawn且绘制器可用，使用DrawPos
                if (pawnParent != null && pawnParent.Drawer != null)
                {
                    return pawnParent.DrawPos;
                }
                
                // 否则使用网格位置
                return parent.Position.ToVector3Shifted();
            }
            catch (NullReferenceException)
            {
                // 发生异常时返回网格位置
                return parent?.Position.ToVector3Shifted() ?? Vector3.zero;
            }
        }
        
        /// <summary>
        /// 更新移动状态
        /// </summary>
        private void UpdateMovementState()
        {
            if (!parent.Spawned || parent.Destroyed)
            {
                isMoving = false;
                return;
            }
            
            try
            {
                Vector3 currentPos = GetSafePosition();
                float distanceMoved = Vector3.Distance(currentPos, lastPosition);
                
                // 简单移动检测：如果位置有变化就算移动
                isMoving = distanceMoved > 0.01f;
                
                lastPosition = currentPos;
            }
            catch (NullReferenceException ex)
            {
                // 发生异常时重置状态
                Log.Warning($"[WULA] Error updating movement state for {parent}: {ex.Message}");
                isMoving = false;
            }
        }

        /// <summary>
        /// 检查是否可以发射Mote
        /// </summary>
        private bool CanEmit()
        {
            // 基础检查
            if (parent == null || !parent.Spawned || parent.Map == null || Props.moteDef == null)
                return false;
                
            // 如果Pawn状态异常，不发射
            if (pawnParent != null)
            {
                if (pawnParent.Dead || pawnParent.Downed || pawnParent.InMentalState)
                    return false;
                    
                // 检查绘制器是否可用
                if (pawnParent.Drawer == null)
                    return false;
            }

            // 新增：检查驾驶员条件
            if (Props.requirePilot)
            {
                // 需要至少一个驾驶员
                if (pilotHolder == null || !pilotHolder.HasPilots)
                    return false;

                // 可选：检查驾驶员是否存活
                if (Props.requirePilotAlive)
                {
                    foreach (var pilot in pilotHolder.GetPilots())
                    {
                        if (pilot.Dead || pilot.Downed)
                            return false;
                    }
                }
            }

            // 检查电源条件
            if (Props.onlyWhenPowered)
            {
                var powerComp = parent.TryGetComp<CompPowerTrader>();
                if (powerComp != null && !powerComp.PowerOn)
                    return false;
            }

            // 检查天气条件
            if (!string.IsNullOrEmpty(Props.onlyInWeather))
            {
                var currentWeather = parent.Map.weatherManager.curWeather;
                if (currentWeather == null || currentWeather.defName != Props.onlyInWeather)
                    return false;
            }

            // 检查地形条件
            if (Props.onlyOnTerrain != null)
            {
                var terrain = parent.Position.GetTerrain(parent.Map);
                if (terrain != Props.onlyOnTerrain)
                    return false;
            }

            return true;
        }

        private void EmitMote()
        {
            try
            {
                // 计算发射位置（根据朝向调整偏移）
                Vector3 emitPos = GetSafePosition() + GetOffsetForFacing();
                
                // 如果父物体是Pawn，可以添加一些随机偏移
                if (pawnParent != null && Props.randomOffsetRadius > 0f)
                {
                    emitPos += new Vector3(
                        Rand.Range(-Props.randomOffsetRadius, Props.randomOffsetRadius),
                        0f,
                        Rand.Range(-Props.randomOffsetRadius, Props.randomOffsetRadius)
                    );
                }

                // 创建Mote
                Mote mote = (Mote)ThingMaker.MakeThing(Props.moteDef);

                if (mote is MoteThrown moteThrown)
                {
                    // 设置初始位置
                    moteThrown.exactPosition = emitPos;

                    // 设置向北移动的速度
                    moteThrown.SetVelocity(
                        angle: 0f, // 0度 = 北向
                        speed: Props.moveSpeed
                    );

                    // 设置旋转
                    moteThrown.exactRotation = Props.rotation;
                    moteThrown.rotationRate = Props.rotationRate;

                    // 设置缩放
                    moteThrown.Scale = Props.scale;

                    // 设置存活时间
                    moteThrown.airTimeLeft = Props.lifetimeTicks;

                    // 添加到地图
                    GenSpawn.Spawn(mote, parent.Position, parent.Map);
                }
                else
                {
                    // 不是MoteThrown类型，使用基础设置
                    mote.exactPosition = emitPos;
                    mote.Scale = Props.scale;
                    GenSpawn.Spawn(mote, parent.Position, parent.Map);
                }

                // 播放发射音效
                if (Props.soundOnEmit != null)
                {
                    Props.soundOnEmit.PlayOneShot(parent);
                }
            }
            catch (Exception ex)
            {
                Log.Error($"[WULA] Error emitting mote: {ex}");
            }
        }
        
        /// <summary>
        /// 根据朝向获取偏移位置
        /// </summary>
        private Vector3 GetOffsetForFacing()
        {
            Vector3 offset = Props.offset;
            
            // 如果不是Pawn，返回基础偏移
            if (pawnParent == null)
                return offset;
            
            // 检查Pawn是否可用
            if (pawnParent.Destroyed || !pawnParent.Spawned)
                return offset;
            
            try
            {
                // 根据朝向调整偏移
                switch (pawnParent.Rotation.AsInt)
                {
                    case 0: // 北
                        return offset;
                    case 1: // 东
                        return new Vector3(-offset.z, offset.y, offset.x);
                    case 2: // 南
                        return new Vector3(-offset.x, offset.y, offset.z);
                    case 3: // 西
                        return new Vector3(offset.z, offset.y, -offset.x);
                    default:
                        return offset;
                }
            }
            catch (NullReferenceException)
            {
                // 如果访问Rotation失败，返回基础偏移
                return offset;
            }
        }

        public override void PostExposeData()
        {
            base.PostExposeData();
            Scribe_Values.Look(ref ticksUntilNextEmit, "ticksUntilNextEmit", 0);
            Scribe_Values.Look(ref lastPosition, "lastPosition", Vector3.zero);
            Scribe_Values.Look(ref isMoving, "isMoving", false);
        }
        
        // 添加销毁相关的清理
        public override void PostDestroy(DestroyMode mode, Map previousMap)
        {
            base.PostDestroy(mode, previousMap);
            isDestroyed = true;
        }
        
        public void PostDeSpawn(Map map)
        {
            base.PostDeSpawn(map);
            isDestroyed = true;
        }

        /// <summary>
        /// 获取组件状态信息（用于调试）
        /// </summary>
        public string GetStatusInfo()
        {
            if (parent == null || isDestroyed)
                return "Component destroyed";
                
            string pilotStatus = "N/A";
            if (pilotHolder != null)
            {
                pilotStatus = pilotHolder.HasPilots ?
                    $"Has {pilotHolder.CurrentPilotCount} pilot(s)" :
                    "No pilots";
            }

            return $"Mote Emitter Status:\n" +
                   $"  Active: {!isDestroyed}\n" +
                   $"  Can Emit: {CanEmit()}\n" +
                   $"  Moving: {isMoving}\n" +
                   $"  Pilot Status: {pilotStatus}\n" +
                   $"  Next Emit: {ticksUntilNextEmit} ticks\n" +
                   $"  Powered: {(Props.onlyWhenPowered ? CheckPowerStatus() : "N/A")}";
        }

        private string CheckPowerStatus()
        {
            var powerComp = parent.TryGetComp<CompPowerTrader>();
            if (powerComp == null)
                return "No power comp";
            return powerComp.PowerOn ? "Powered" : "No power";
        }
    }

    /// <summary>
    /// 组件属性（更新版）
    /// </summary>
    public class CompProperties_MoteEmitterNorthward : CompProperties
    {
        /// <summary>Mote定义</summary>
        public ThingDef moteDef;

        /// <summary>发射间隔（ticks）- 静止时</summary>
        public int emitIntervalTicks = 60; // 默认1秒
        
        /// <summary>发射间隔（ticks）- 移动时</summary>
        public int emitIntervalMovingTicks = 30; // 移动时默认0.5秒

        /// <summary>移动速度</summary>
        public float moveSpeed = 1f;

        /// <summary>Mote生命周期（ticks）</summary>
        public float lifetimeTicks = 120f; // 默认2秒

        /// <summary>初始旋转角度</summary>
        public float rotation = 0f;

        /// <summary>旋转速度（度/秒）</summary>
        public float rotationRate = 0f;

        /// <summary>缩放大小</summary>
        public float scale = 1f;

        /// <summary>偏移位置（相对于父物体）- 默认朝北时的偏移</summary>
        public Vector3 offset = Vector3.zero;
        
        /// <summary>随机偏移半径</summary>
        public float randomOffsetRadius = 0f;

        /// <summary>发射时的音效</summary>
        public SoundDef soundOnEmit;

        /// <summary>是否只在启用的状态发射</summary>
        public bool onlyWhenPowered = false;

        /// <summary>是否只在至少有一个驾驶员时发射</summary>
        public bool requirePilot = true; // 新增：驾驶员条件

        /// <summary>天气条件：只在指定天气发射（用逗号分隔）</summary>
        public string onlyInWeather;

        /// <summary>地形条件：只在指定地形发射</summary>
        public TerrainDef onlyOnTerrain;

        /// <summary>驾驶员条件：只在驾驶员存活时发射</summary>
        public bool requirePilotAlive = true; // 新增：要求驾驶员存活

        public CompProperties_MoteEmitterNorthward()
        {
            compClass = typeof(CompMoteEmitterNorthward);
        }

        public override IEnumerable<string> ConfigErrors(ThingDef parentDef)
        {
            foreach (string error in base.ConfigErrors(parentDef))
            {
                yield return error;
            }

            if (moteDef == null)
            {
                yield return $"moteDef is not defined for {parentDef.defName}";
            }

            if (emitIntervalTicks <= 0)
            {
                yield return $"emitIntervalTicks must be greater than 0 for {parentDef.defName}";
            }
            
            if (emitIntervalMovingTicks <= 0)
            {
                yield return $"emitIntervalMovingTicks must be greater than 0 for {parentDef.defName}";
            }

            if (lifetimeTicks <= 0)
            {
                yield return $"lifetimeTicks must be greater than 0 for {parentDef.defName}";
            }

            if (requirePilot && parentDef.GetCompProperties<CompProperties_MechPilotHolder>() == null)
            {
                yield return $"requirePilot is true but no CompProperties_MechPilotHolder found for {parentDef.defName}";
            }
        }
    }
}
