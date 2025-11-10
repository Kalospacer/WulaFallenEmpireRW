using RimWorld;
using Verse;
using UnityEngine;
using System.Collections.Generic;
using System.Linq;

namespace WulaFallenEmpire
{
    public class EnergyLance : OrbitalStrike
    {
        // 移动相关属性
        public IntVec3 startPos;
        public IntVec3 endPos;
        public float moveDistance;
        public bool useFixedDistance;
        
        // 伤害配置
        public int firesPerTick = 4;
        
        // ModExtension引用
        private EnergyLanceExtension extension;
        
        // 移动状态
        private Vector3 currentPos;
        private Vector3 moveDirection;
        private float moveSpeed;
        private float traveledDistance;
        private const float effectRadius = 3f; // 作用半径
        
        private static List<Thing> tmpThings = new List<Thing>();

        public override void StartStrike()
        {
            base.StartStrike();
            
            // 获取ModExtension
            extension = def.GetModExtension<EnergyLanceExtension>();
            if (extension == null)
            {
                Log.Error($"[EnergyLance] No EnergyLanceExtension found on {def.defName}");
                return;
            }
            
            // 初始化移动参数
            currentPos = startPos.ToVector3();
            
            if (useFixedDistance)
            {
                // 从起点向终点方向移动固定距离
                Vector3 direction = (endPos.ToVector3() - startPos.ToVector3()).normalized;
                moveDirection = direction;
                moveSpeed = moveDistance / duration; // 根据持续时间计算移动速度
            }
            else
            {
                // 直接从起点移动到终点
                Vector3 direction = (endPos.ToVector3() - startPos.ToVector3());
                moveDirection = direction.normalized;
                moveSpeed = direction.magnitude / duration;
            }
            
            traveledDistance = 0f;
            
            // 创建视觉效果
            CreateVisualEffect();
            
            Log.Message($"[EnergyLance] Strike started from {startPos} to {endPos}, " +
                       $"damage: {extension.damageDef.defName}, speed: {moveSpeed}");
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
                // 移动光束
                MoveBeam();
                
                // 造成伤害
                for (int i = 0; i < firesPerTick; i++)
                {
                    DoBeamDamage();
                }
            }
        }

        private void MoveBeam()
        {
            // 计算移动距离
            float moveThisTick = moveSpeed;
            
            // 更新位置
            currentPos += moveDirection * moveThisTick;
            traveledDistance += moveThisTick;
            
            // 更新光束的实际位置
            IntVec3 newCell = new IntVec3(Mathf.RoundToInt(currentPos.x), 0, Mathf.RoundToInt(currentPos.z));
            if (newCell != base.Position && newCell.InBounds(base.Map))
            {
                base.Position = newCell;
            }
            
            // 检查是否到达终点
            if (useFixedDistance && traveledDistance >= moveDistance)
            {
                // 固定距离模式：移动指定距离后结束
                Destroy();
                Log.Message($"[EnergyLance] Reached fixed distance, destroying");
            }
            else if (!useFixedDistance && traveledDistance >= Vector3.Distance(startPos.ToVector3(), endPos.ToVector3()))
            {
                // 终点模式：到达终点后结束
                Destroy();
                Log.Message($"[EnergyLance] Reached end position, destroying");
            }
        }

        private void DoBeamDamage()
        {
            if (extension == null) return;

            // 在当前光束位置周围随机选择一个单元格
            IntVec3 targetCell = (from x in GenRadial.RadialCellsAround(base.Position, effectRadius, useCenter: true)
                where x.InBounds(base.Map)
                select x).RandomElementByWeight((IntVec3 x) => 1f - Mathf.Min(x.DistanceTo(base.Position) / effectRadius, 1f) + 0.05f);

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
                
                Log.Message($"[EnergyLance] Applied {extension.damageDef.defName} damage {damageAmount} to {thing.Label}");
            }
            
            tmpThings.Clear();
        }

        public override void ExposeData()
        {
            base.ExposeData();
            
            Scribe_Values.Look(ref startPos, "startPos");
            Scribe_Values.Look(ref endPos, "endPos");
            Scribe_Values.Look(ref moveDistance, "moveDistance");
            Scribe_Values.Look(ref useFixedDistance, "useFixedDistance");
            Scribe_Values.Look(ref firesPerTick, "firesPerTick", 4);
        }
    }
}
