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
        public float flightSpeed = 1f;
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

        // 精确位置计算（基于FlyOver的逻辑）
        public override Vector3 DrawPos
        {
            get
            {
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
                Vector3 direction = (CalculateEndPosition() - startPosition.ToVector3()).normalized;
                return Quaternion.LookRotation(direction.Yto0());
            }
        }

        public override void SpawnSetup(Map map, bool respawningAfterLoad)
        {
            base.SpawnSetup(map, respawningAfterLoad);
            
            orbitalBeamComp = GetComp<CompOrbitalBeam>();
            
            if (!respawningAfterLoad)
            {
                base.Position = startPosition;
                hasStarted = true;
                
                // 计算移动方向
                Vector3 endPos = CalculateEndPosition();
                moveDirection = (endPos - startPosition.ToVector3()).normalized;
                
                // 初始化光束组件
                if (orbitalBeamComp != null)
                {
                    // 使用反射调用StartAnimation方法
                    StartOrbitalBeamAnimation();
                }
                
                // 开始音效
                StartSound();
                
                Log.Message($"[EnergyLance] Spawned at {startPosition}, moving to {endPosition}, " +
                           $"distance: {moveDistance}, fixed: {useFixedDistance}");
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

        protected override void Tick()
        {
            base.Tick();
            
            if (!hasStarted || hasCompleted)
                return;

            ticksPassed++;
            
            // 更新移动进度
            UpdateMovement();
            
            // 造成伤害
            for (int i = 0; i < firesPerTick; i++)
            {
                StartRandomFireAndDoFlameDamage();
            }
            
            // 更新音效
            sustainer?.Maintain();
            
            // 检查是否完成
            if (ticksPassed >= durationTicks || currentProgress >= 1f)
            {
                CompleteEnergyLance();
            }
        }

        private void UpdateMovement()
        {
            // 计算总距离
            float totalDistance = useFixedDistance ? moveDistance : Vector3.Distance(startPosition.ToVector3(), endPosition.ToVector3());
            
            // 计算移动速度（基于持续时间和总距离）
            float progressPerTick = 1f / durationTicks;
            currentProgress += progressPerTick;
            currentProgress = Mathf.Clamp01(currentProgress);
            
            // 更新精确位置
            exactPosition = Vector3.Lerp(startPosition.ToVector3(), CalculateEndPosition(), currentProgress);
            
            // 更新格子位置
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
                
                DamageInfo damageInfo = new DamageInfo(DamageDefOf.Flame, num, 0f, -1f, instigator, null, weaponDef);
                tmpThings[i].TakeDamage(damageInfo).AssociateWithLog(battleLogEntry_DamageTaken);
            }
            
            tmpThings.Clear();
        }

        private void CompleteEnergyLance()
        {
            hasCompleted = true;
            
            // 停止音效
            sustainer?.End();
            sustainer = null;
            
            Log.Message($"[EnergyLance] Completed at position {base.Position}");
            
            // 销毁自身
            Destroy();
        }

        // 重写绘制方法，确保光束正确显示
        protected override void DrawAt(Vector3 drawLoc, bool flip = false)
        {
            // 让CompOrbitalBeam处理绘制
            Comps_PostDraw();
        }

        public override void ExposeData()
        {
            base.ExposeData();
            
            Scribe_Values.Look(ref startPosition, "startPosition");
            Scribe_Values.Look(ref endPosition, "endPosition");
            Scribe_Values.Look(ref moveDistance, "moveDistance");
            Scribe_Values.Look(ref useFixedDistance, "useFixedDistance");
            Scribe_Values.Look(ref flightSpeed, "flightSpeed", 1f);
            Scribe_Values.Look(ref currentProgress, "currentProgress", 0f);
            Scribe_Values.Look(ref altitude, "altitude", 20f);
            Scribe_Values.Look(ref firesPerTick, "firesPerTick", 4);
            Scribe_Values.Look(ref effectRadius, "effectRadius", 15f);
            Scribe_Values.Look(ref durationTicks, "durationTicks", 600);
            Scribe_Values.Look(ref ticksPassed, "ticksPassed", 0);
            Scribe_Values.Look(ref hasStarted, "hasStarted", false);
            Scribe_Values.Look(ref hasCompleted, "hasCompleted", false);
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
            
            GenSpawn.Spawn(energyLance, start, map);
            
            Log.Message($"[EnergyLance] Created {energyLanceDef.defName} from {start} to {end}");
            return energyLance;
        }
    }
}
