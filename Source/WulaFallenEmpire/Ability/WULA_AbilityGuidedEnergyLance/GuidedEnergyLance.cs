using RimWorld;
using Verse;
using UnityEngine;
using System.Collections.Generic;
using System.Linq;

namespace WulaFallenEmpire
{
    public class GuidedEnergyLance : OrbitalStrike
    {
        // 引导相关属性
        public Thing controlBuilding;                      // 控制建筑
        private IntVec3 currentTarget = IntVec3.Invalid;  // 当前目标位置
        private int lastTargetUpdateTick = -1;            // 最后收到目标更新的刻
        private int maxNoUpdateTicks = 60;                // 无更新时的最大存活时间
        
        // 移动配置
        public float moveSpeed = 2.0f;                    // 移动速度（格/秒）
        public float turnSpeed = 90f;                     // 转向速度（度/秒）
        
        // 状态
        private Vector3 currentVelocity;
        private bool hasValidTarget = false;
        
        // ModExtension引用
        private EnergyLanceExtension extension;
        
        private static List<Thing> tmpThings = new List<Thing>();

        public override void StartStrike()
        {
            base.StartStrike();
            
            // 获取ModExtension
            extension = def.GetModExtension<EnergyLanceExtension>();
            if (extension == null)
            {
                Log.Error($"[GuidedEnergyLance] No EnergyLanceExtension found on {def.defName}");
                return;
            }
            
            lastTargetUpdateTick = Find.TickManager.TicksGame;
            
            // 创建视觉效果
            CreateVisualEffect();
            
            Log.Message($"[GuidedEnergyLance] Guided EnergyLance started, controlled by {controlBuilding?.Label ?? "None"}");
        }

        private void CreateVisualEffect()
        {
            // 使用ModExtension中定义的Mote，如果没有则使用默认的PowerBeam
            if (extension.moteDef != null)
            {
                Mote mote = MoteMaker.MakeStaticMote(base.Position, base.Map, extension.moteDef);
            }
            else
            {
                // 使用原版PowerBeam的视觉效果
                MoteMaker.MakePowerBeamMote(base.Position, base.Map);
            }
        }

        protected override void Tick()
        {
            base.Tick();
            
            if (!base.Destroyed && extension != null)
            {
                // 检查目标更新状态
                CheckTargetStatus();
                
                // 移动光束
                MoveBeam();
                
                // 造成伤害
                for (int i = 0; i < firesPerTick; i++)
                {
                    DoBeamDamage();
                }
            }
        }
        
        private void CheckTargetStatus()
        {
            int currentTick = Find.TickManager.TicksGame;
            int ticksSinceUpdate = currentTick - lastTargetUpdateTick;
            
            // 检查是否长时间未收到更新
            if (ticksSinceUpdate >= maxNoUpdateTicks)
            {
                Log.Message($"[GuidedEnergyLance] No target updates for {ticksSinceUpdate} ticks, destroying");
                Destroy();
                return;
            }
            
            // 检查控制建筑状态
            if (controlBuilding == null || controlBuilding.Destroyed || !controlBuilding.Spawned)
            {
                Log.Message($"[GuidedEnergyLance] Control building lost, destroying");
                Destroy();
                return;
            }
        }

        private void MoveBeam()
        {
            if (!hasValidTarget || !currentTarget.IsValid)
            {
                // 没有有效目标，缓慢移动或保持原地
                ApplyMinimalMovement();
                return;
            }
            
            // 计算移动方向
            Vector3 targetDirection = (currentTarget.ToVector3() - base.Position.ToVector3()).normalized;
            
            // 平滑转向
            if (currentVelocity.magnitude > 0.1f)
            {
                float maxTurnAngle = turnSpeed * 0.0167f; // 每帧最大转向角度（假设60FPS）
                currentVelocity = Vector3.RotateTowards(currentVelocity, targetDirection, maxTurnAngle * Mathf.Deg2Rad, moveSpeed * 0.0167f);
            }
            else
            {
                currentVelocity = targetDirection * moveSpeed * 0.0167f;
            }
            
            // 应用移动
            Vector3 newPos = base.Position.ToVector3() + currentVelocity;
            IntVec3 newCell = new IntVec3(Mathf.RoundToInt(newPos.x), 0, Mathf.RoundToInt(newPos.z));
            
            if (newCell != base.Position && newCell.InBounds(base.Map))
            {
                base.Position = newCell;
            }
            
            // 检查是否接近目标
            float distanceToTarget = Vector3.Distance(base.Position.ToVector3(), currentTarget.ToVector3());
            if (distanceToTarget < 1.5f)
            {
                // 到达目标附近，可以减速或保持位置
                currentVelocity *= 0.8f;
            }
            
            Log.Message($"[GuidedEnergyLance] Moving to {currentTarget}, distance: {distanceToTarget:F1}");
        }
        
        private void ApplyMinimalMovement()
        {
            // 无目标时的最小移动，防止完全静止
            if (currentVelocity.magnitude < 0.1f)
            {
                // 随机轻微移动
                currentVelocity = new Vector3(Rand.Range(-0.1f, 0.1f), 0f, Rand.Range(-0.1f, 0.1f));
            }
            else
            {
                // 缓慢减速
                currentVelocity *= 0.95f;
            }
            
            Vector3 newPos = base.Position.ToVector3() + currentVelocity;
            IntVec3 newCell = new IntVec3(Mathf.RoundToInt(newPos.x), 0, Mathf.RoundToInt(newPos.z));
            
            if (newCell != base.Position && newCell.InBounds(base.Map))
            {
                base.Position = newCell;
            }
        }

        private void DoBeamDamage()
        {
            if (extension == null) return;

            // 在当前光束位置周围随机选择一个单元格
            IntVec3 targetCell = (from x in GenRadial.RadialCellsAround(base.Position, 2f, useCenter: true)
                where x.InBounds(base.Map)
                select x).RandomElementByWeight((IntVec3 x) => 1f - Mathf.Min(x.DistanceTo(base.Position) / 2f, 1f) + 0.05f);

            // 尝试在该单元格点火（如果配置了点火）
            if (extension.igniteFires)
            {
                FireUtility.TryStartFireIn(targetCell, base.Map, Rand.Range(0.1f, extension.fireIgniteChance), instigator);
            }
            
            // 对该单元格内的物体造成伤害
            tmpThings.Clear();
            tmpThings.AddRange(targetCell.GetThingList(base.Map));
            
            for (int i = 0; i < tmpThings.Count; i++)
            {
                Thing thing = tmpThings[i];
                
                // 检查是否对尸体造成伤害
                if (!extension.applyDamageToCorpses && thing is Corpse)
                    continue;
                
                // 计算伤害量
                int damageAmount = (thing is Corpse) ? 
                    extension.corpseDamageAmountRange.RandomInRange : 
                    extension.damageAmountRange.RandomInRange;
                
                Pawn pawn = thing as Pawn;
                BattleLogEntry_DamageTaken battleLogEntry = null;
                
                if (pawn != null)
                {
                    battleLogEntry = new BattleLogEntry_DamageTaken(pawn, RulePackDefOf.DamageEvent_PowerBeam, instigator as Pawn);
                    Find.BattleLog.Add(battleLogEntry);
                }
                
                // 使用ModExtension中定义的伤害类型
                DamageInfo damageInfo = new DamageInfo(extension.damageDef, damageAmount, 0f, -1f, instigator, null, weaponDef);
                thing.TakeDamage(damageInfo).AssociateWithLog(battleLogEntry);
            }
            
            tmpThings.Clear();
        }
        
        // 外部调用：更新目标位置
        public void UpdateTarget(IntVec3 newTarget)
        {
            lastTargetUpdateTick = Find.TickManager.TicksGame;
            
            if (newTarget.IsValid && newTarget.InBounds(base.Map))
            {
                currentTarget = newTarget;
                hasValidTarget = true;
                
                Log.Message($"[GuidedEnergyLance] Target updated to {newTarget}");
            }
            else
            {
                hasValidTarget = false;
                currentTarget = IntVec3.Invalid;
                
                Log.Message($"[GuidedEnergyLance] Target cleared");
            }
        }

        public override void ExposeData()
        {
            base.ExposeData();
            
            Scribe_References.Look(ref controlBuilding, "controlBuilding");
            Scribe_Values.Look(ref currentTarget, "currentTarget", IntVec3.Invalid);
            Scribe_Values.Look(ref lastTargetUpdateTick, "lastTargetUpdateTick", -1);
            Scribe_Values.Look(ref maxNoUpdateTicks, "maxNoUpdateTicks", 60);
            Scribe_Values.Look(ref moveSpeed, "moveSpeed", 2.0f);
            Scribe_Values.Look(ref turnSpeed, "turnSpeed", 90f);
            Scribe_Values.Look(ref firesPerTick, "firesPerTick", 3);
        }
    }
}
