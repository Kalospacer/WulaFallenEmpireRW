using RimWorld;
using Verse;
using UnityEngine;
using System.Collections.Generic;
using System.Linq;
using Verse.Sound;

namespace WulaFallenEmpire
{
    [StaticConstructorOnStartup]
    public class EnergyLance : ThingWithComps
    {
        // 移动相关属性
        public IntVec3 startPosition;
        public IntVec3 endPosition;
        public float moveDistance;
        public bool useFixedDistance;
        public float flightSpeed = 0.5f; // 提高移动速度
        public float currentProgress = 0f;
        public float altitude = 20f;

        // 伤害配置
        public int firesPerTick = 4;
        public float effectRadius = 15f;
        public int durationTicks = 600;
        private int ticksPassed = 0;

        // 移动状态
        private Vector3 exactPosition;
        private Vector3 moveDirection;
        private bool hasStarted = false;
        private bool hasCompleted = false;

        // 视觉效果
        private CompOrbitalBeam orbitalBeamComp;
        private Sustainer sustainer;

        // 伤害相关
        private static List<Thing> tmpThings = new List<Thing>();
        private static readonly IntRange FlameDamageAmountRange = new IntRange(65, 100);
        private static readonly IntRange CorpseFlameDamageAmountRange = new IntRange(5, 10);
        public Thing instigator;
        public ThingDef weaponDef;

        // 动态目标追踪支持
        private IntVec3 currentTargetPosition = IntVec3.Invalid;
        private bool hasValidTarget = false;
        private int lastTargetUpdateTick = 0;
        private int maxIdleTicks = 180; // 3秒无目标更新后自毁

        // 移动模式
        private bool useDynamicMovement = true; // 使用动态追踪模式

        // 更新目标位置
        public void UpdateTargetPosition(IntVec3 targetPos)
        {
            if (targetPos.IsValid)
            {
                currentTargetPosition = targetPos;
                hasValidTarget = true;
                lastTargetUpdateTick = Find.TickManager.TicksGame;

                // 如果是首次设置目标，立即移动到目标位置
                if (!hasStarted)
                {
                    exactPosition = targetPos.ToVector3();
                    exactPosition.y = altitude;
                    base.Position = targetPos;
                    hasStarted = true;
                }

                Log.Message($"[EnergyLance] Target updated to: {targetPos}, current position: {base.Position}");
            }
            else
            {
                hasValidTarget = false;
                Log.Message("[EnergyLance] Target cleared");
            }
        }

        // 动态移动逻辑 - 直接追踪目标
        private void UpdateDynamicMovement()
        {
            if (hasValidTarget && currentTargetPosition.IsValid)
            {
                Vector3 targetVector = currentTargetPosition.ToVector3();
                targetVector.y = altitude; // 保持高度

                Vector3 currentVector = exactPosition;

                // 计算移动方向
                Vector3 direction = (targetVector - currentVector).normalized;

                // 计算移动距离（基于速度）
                float moveThisTick = flightSpeed * 0.1f;

                // 更新位置
                exactPosition += direction * moveThisTick;

                // 更新格子位置
                IntVec3 newCell = new IntVec3(
                    Mathf.RoundToInt(exactPosition.x),
                    Mathf.RoundToInt(exactPosition.y),
                    Mathf.RoundToInt(exactPosition.z)
                );

                if (newCell != base.Position && newCell.InBounds(base.Map))
                {
                    base.Position = newCell;
                    Log.Message($"[EnergyLance] Moved to new cell: {newCell}");
                }

                // 检查是否接近目标
                float distanceToTarget = Vector3.Distance(currentVector, targetVector);
                if (distanceToTarget < 0.5f)
                {
                    // 非常接近目标，直接设置到目标位置
                    exactPosition = targetVector;
                    base.Position = currentTargetPosition;
                    Log.Message($"[EnergyLance] Reached target position: {currentTargetPosition}");
                }
            }
            else
            {
                // 没有有效目标，使用原始移动逻辑
                UpdateOriginalMovement();
            }
        }

        // 原始移动逻辑（从起点到终点的线性移动）
        private void UpdateOriginalMovement()
        {
            float totalDistance = useFixedDistance ? moveDistance : Vector3.Distance(startPosition.ToVector3(), endPosition.ToVector3());
            float progressPerTick = 1f / durationTicks;
            currentProgress += progressPerTick;
            currentProgress = Mathf.Clamp01(currentProgress);

            exactPosition = Vector3.Lerp(startPosition.ToVector3(), CalculateEndPosition(), currentProgress);
            exactPosition.y = altitude;

            IntVec3 newCell = new IntVec3(
                Mathf.RoundToInt(exactPosition.x),
                Mathf.RoundToInt(exactPosition.y),
                Mathf.RoundToInt(exactPosition.z)
            );

            if (newCell != base.Position && newCell.InBounds(base.Map))
            {
                base.Position = newCell;
            }
        }

        protected override void Tick()
        {
            base.Tick();

            if (!hasStarted || hasCompleted)
                return;

            ticksPassed++;

            // 添加保护期检查 - 防止光束立即销毁
            if (ticksPassed < 5)
            {
                // 只更新移动，不检查销毁
                UpdateDynamicMovement();
                return;
            }

            // 检查是否长时间没有收到目标更新
            if (hasValidTarget && Find.TickManager.TicksGame - lastTargetUpdateTick > maxIdleTicks)
            {
                Log.Message("[EnergyLance] No target updates received, self-destructing");
                CompleteEnergyLance();
                return;
            }

            // 更新移动
            UpdateDynamicMovement();

            // 造成伤害
            for (int i = 0; i < firesPerTick; i++)
            {
                StartRandomFireAndDoFlameDamage();
            }

            // 更新音效
            sustainer?.Maintain();

            // 检查是否完成
            if (ticksPassed >= durationTicks || (!hasValidTarget && currentProgress >= 1f))
            {
                CompleteEnergyLance();
            }
        }

        private void CompleteEnergyLance()
        {
            hasCompleted = true;

            // 停止音效
            sustainer?.End();
            sustainer = null;

            Log.Message($"[EnergyLance] 光束完成 at position {base.Position}, ticksPassed: {ticksPassed}, durationTicks: {durationTicks}");

            // 销毁自身
            Destroy();
        }

        public override void ExposeData()
        {
            base.ExposeData();

            Scribe_Values.Look(ref currentTargetPosition, "currentTargetPosition");
            Scribe_Values.Look(ref hasValidTarget, "hasValidTarget", false);
            Scribe_Values.Look(ref lastTargetUpdateTick, "lastTargetUpdateTick", 0);
            Scribe_Values.Look(ref maxIdleTicks, "maxIdleTicks", 180);
            Scribe_Values.Look(ref useDynamicMovement, "useDynamicMovement", true);
        }

        // 精确位置计算
        public override Vector3 DrawPos
        {
            get
            {
                if (exactPosition != Vector3.zero)
                {
                    return exactPosition;
                }

                // 备用计算
                Vector3 start = startPosition.ToVector3();
                Vector3 end = CalculateEndPosition();
                Vector3 basePos = Vector3.Lerp(start, end, currentProgress);
                basePos.y = altitude;
                return basePos;
            }
        }

        // 计算实际终点位置
        private Vector3 CalculateEndPosition()
        {
            if (useFixedDistance)
            {
                Vector3 direction = (endPosition.ToVector3() - startPosition.ToVector3()).normalized;
                return startPosition.ToVector3() + direction * moveDistance;
            }
            else
            {
                return endPosition.ToVector3();
            }
        }

        // 精确旋转
        public virtual Quaternion ExactRotation
        {
            get
            {
                Vector3 direction;
                if (hasValidTarget && currentTargetPosition.IsValid)
                {
                    direction = (currentTargetPosition.ToVector3() - exactPosition).normalized;
                }
                else
                {
                    direction = (CalculateEndPosition() - startPosition.ToVector3()).normalized;
                }
                return Quaternion.LookRotation(direction.Yto0());
            }
        }

        public override void SpawnSetup(Map map, bool respawningAfterLoad)
        {
            base.SpawnSetup(map, respawningAfterLoad);

            orbitalBeamComp = GetComp<CompOrbitalBeam>();

            if (!respawningAfterLoad)
            {
                // 初始位置设置为目标位置（如果有效），否则使用起始位置
                if (endPosition.IsValid)
                {
                    base.Position = endPosition;
                    exactPosition = endPosition.ToVector3();
                    exactPosition.y = altitude;
                }
                else
                {
                    base.Position = startPosition;
                    exactPosition = startPosition.ToVector3();
                    exactPosition.y = altitude;
                }

                hasStarted = true;

                // 计算移动方向
                Vector3 endPos = CalculateEndPosition();
                moveDirection = (endPos - startPosition.ToVector3()).normalized;

                // 初始化光束组件
                if (orbitalBeamComp != null)
                {
                    StartOrbitalBeamAnimation();
                }

                // 开始音效
                StartSound();

                Log.Message($"[EnergyLance] Spawned at {base.Position}, target: {endPosition}, " +
                           $"exact position: {exactPosition}");
            }
        }

        // 使用反射调用StartAnimation方法
        private void StartOrbitalBeamAnimation()
        {
            var startAnimationMethod = orbitalBeamComp.GetType().GetMethod("StartAnimation");
            if (startAnimationMethod != null)
            {
                startAnimationMethod.Invoke(orbitalBeamComp, new object[] { durationTicks, 10, 0f });
                Log.Message("[EnergyLance] Orbital beam animation started");
            }
            else
            {
                Log.Warning("[EnergyLance] Could not find StartAnimation method on CompOrbitalBeam");
            }
        }

        private void StartSound()
        {
            var soundProp = orbitalBeamComp?.GetType().GetProperty("Props")?.GetValue(orbitalBeamComp);
            if (soundProp != null)
            {
                var soundField = soundProp.GetType().GetField("sound");
                if (soundField != null)
                {
                    SoundDef soundDef = soundField.GetValue(soundProp) as SoundDef;
                    if (soundDef != null)
                    {
                        sustainer = soundDef.TrySpawnSustainer(SoundInfo.InMap(this, MaintenanceType.PerTick));
                    }
                }
            }
        }

        private void StartRandomFireAndDoFlameDamage()
        {
            IntVec3 targetCell = (from x in GenRadial.RadialCellsAround(base.Position, effectRadius, useCenter: true)
                                  where x.InBounds(base.Map)
                                  select x).RandomElementByWeight((IntVec3 x) => 1f - Mathf.Min(x.DistanceTo(base.Position) / effectRadius, 1f) + 0.05f);

            FireUtility.TryStartFireIn(targetCell, base.Map, Rand.Range(0.1f, 0.925f), instigator);

            tmpThings.Clear();
            tmpThings.AddRange(targetCell.GetThingList(base.Map));

            for (int i = 0; i < tmpThings.Count; i++)
            {
                int num = ((tmpThings[i] is Corpse) ? CorpseFlameDamageAmountRange.RandomInRange : FlameDamageAmountRange.RandomInRange);
                Pawn pawn = tmpThings[i] as Pawn;
                BattleLogEntry_DamageTaken battleLogEntry_DamageTaken = null;

                if (pawn != null)
                {
                    battleLogEntry_DamageTaken = new BattleLogEntry_DamageTaken(pawn, RulePackDefOf.DamageEvent_PowerBeam, instigator as Pawn);
                    Find.BattleLog.Add(battleLogEntry_DamageTaken);
                }

                DamageInfo damageInfo = new DamageInfo(WulaDamageDefOf.Wula_Dark_Matter_Flame, num, 2f, -1f, instigator, null, weaponDef);
                tmpThings[i].TakeDamage(damageInfo).AssociateWithLog(battleLogEntry_DamageTaken);
            }

            tmpThings.Clear();
        }

        // 重写绘制方法，确保光束正确显示
        protected override void DrawAt(Vector3 drawLoc, bool flip = false)
        {
            // 让CompOrbitalBeam处理绘制
            Comps_PostDraw();
        }

        // 创建EnergyLance的静态方法
        public static EnergyLance MakeEnergyLance(ThingDef energyLanceDef, IntVec3 start, IntVec3 end, Map map,
            float distance = 15f, bool fixedDistance = true, int duration = 600, Pawn instigatorPawn = null)
        {
            EnergyLance energyLance = (EnergyLance)ThingMaker.MakeThing(energyLanceDef);
            energyLance.startPosition = start;
            energyLance.endPosition = end;
            energyLance.moveDistance = distance;
            energyLance.useFixedDistance = fixedDistance;
            energyLance.durationTicks = duration;
            energyLance.instigator = instigatorPawn;

            // 直接在目标位置生成光束
            IntVec3 spawnPosition = end.IsValid ? end : start;
            GenSpawn.Spawn(energyLance, spawnPosition, map);

            Log.Message($"[EnergyLance] Created {energyLanceDef.defName} at {spawnPosition}, target: {end}");
            return energyLance;
        }
    }
}
