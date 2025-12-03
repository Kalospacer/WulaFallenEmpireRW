using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;
using System.Linq;

namespace WulaFallenEmpire
{
    public class Wula_PathPierce_Extension : DefModExtension
    {
        // 设置正数表示有限命中次数，-1 表示无限穿透
        public int maxHits = 3;

        // 每次命中的伤害损失百分比。0.25 表示每次命中损失25%伤害
        public float damageFalloff = 0.25f;

        // 如果为 true，无论游戏设置如何，这个抛射体都不会造成友军伤害
        public bool preventFriendlyFire = false;

        // 尾部拖尾特效的 FleckDef
        public FleckDef tailFleckDef;

        // 拖尾特效延迟生成时间（tick）
        public int fleckDelayTicks = 10;


        // 1. 击中敌人时播放的效果器（Effecter）
        public EffecterDef hitEffecterDef;
        // 2. 击中敌人时播放的粒子（Fleck）
        public FleckDef hitFleckDef;
        // 4. 特效持续时间（tick，仅对效果器有效）
        public int effectDurationTicks = 60;
        // 5. 是否对每个命中的敌人都播放特效
        public bool playEffectOnEveryHit = true;
        // 6. 特效位置偏移（相对于被击中目标）
        public Vector3 effectOffset = Vector3.zero;
        // 7. 特效缩放
        public float effectScale = 1.0f;
        // 8. 伤害阈值：只有达到这个伤害值才会播放特效（0表示总是播放）
        public float damageThreshold = 0f;
        // 9. 随机播放的特效列表（随机选择一个）
        public List<EffecterDef> randomHitEffecters;
        // 10. 随机粒子列表（随机选择一个）
        public List<FleckDef> randomHitFlecks;
    }

    public class Projectile_WulaLineAttack : Bullet
    {
        private int hitCounter = 0;
        private List<Thing> alreadyDamaged = new List<Thing>();
        private Vector3 lastTickPosition;
        private int fleckMakeFleckTick; // 拖尾特效的计时器
        public int fleckMakeFleckTickMax = 1; // 拖尾特效的生成频率
        public IntRange fleckMakeFleckNum = new IntRange(1, 1); // 每次生成的粒子数量
        public FloatRange fleckAngle = new FloatRange(-180f, 180f); // 粒子角度
        public FloatRange fleckScale = new FloatRange(1f, 1f); // 粒子大小
        public FloatRange fleckSpeed = new FloatRange(0f, 0f); // 粒子速度
        public FloatRange fleckRotation = new FloatRange(-180f, 180f); // 粒子旋转
        
        // 特效维护列表
        private List<Effecter> activeEffecters = new List<Effecter>();
        private Dictionary<Pawn, int> effecterEndTicks = new Dictionary<Pawn, int>();
        
        private Wula_PathPierce_Extension Props => def.GetModExtension<Wula_PathPierce_Extension>();

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref hitCounter, "hitCounter", 0);
            Scribe_Collections.Look(ref alreadyDamaged, "alreadyDamaged", LookMode.Reference);
            Scribe_Values.Look(ref lastTickPosition, "lastTickPosition");
            Scribe_Collections.Look(ref activeEffecters, "activeEffecters", LookMode.Deep);
            Scribe_Collections.Look(ref effecterEndTicks, "effecterEndTicks", LookMode.Reference, LookMode.Value);
            
            if (alreadyDamaged == null)
            {
                alreadyDamaged = new List<Thing>();
            }
            if (activeEffecters == null)
            {
                activeEffecters = new List<Effecter>();
            }
            if (effecterEndTicks == null)
            {
                effecterEndTicks = new Dictionary<Pawn, int>();
            }
        }

        public override void Launch(Thing launcher, Vector3 origin, LocalTargetInfo usedTarget, LocalTargetInfo intendedTarget, ProjectileHitFlags hitFlags, bool preventFriendlyFire = false, Thing equipment = null, ThingDef targetCoverDef = null)
        {
            base.Launch(launcher, origin, usedTarget, intendedTarget, hitFlags, preventFriendlyFire, equipment, targetCoverDef);
            this.lastTickPosition = origin;
            this.alreadyDamaged.Clear();
            this.hitCounter = 0;
            // 如果游戏设置为 true 或 XML 扩展为 true，则防止友军伤害
            this.preventFriendlyFire = preventFriendlyFire || (Props?.preventFriendlyFire ?? false);
            
            // 清理旧的特效器
            CleanupOldEffecters();
        }

        protected override void Tick()
        {
            Vector3 startPos = this.lastTickPosition;
            base.Tick();
            
            if (this.Destroyed) return;

            // 更新拖尾特效
            UpdateTrailFlecks();
            
            // 更新击中特效器
            UpdateHitEffecters();
            
            if (this.Destroyed) return;

            Vector3 endPos = this.ExactPosition;
            
            CheckPathForDamage(startPos, endPos);

            this.lastTickPosition = endPos;
        }
        
        /// <summary>
        /// 更新拖尾粒子特效
        /// </summary>
        private void UpdateTrailFlecks()
        {
            this.fleckMakeFleckTick++;
            
            // 只有当达到延迟时间后才开始生成 Fleck
            if (this.fleckMakeFleckTick >= Props?.fleckDelayTicks)
            {
                if (this.fleckMakeFleckTick >= (Props.fleckDelayTicks + this.fleckMakeFleckTickMax))
                {
                    this.fleckMakeFleckTick = Props.fleckDelayTicks; // 重置计时器，从延迟时间开始循环
                }

                Map map = base.Map;
                int randomInRange = this.fleckMakeFleckNum.RandomInRange;
                Vector3 currentPosition = this.ExactPosition; // 子弹当前位置
                
                for (int i = 0; i < randomInRange; i++)
                {
                    float currentBulletAngle = ExactRotation.eulerAngles.y; // 使用子弹当前的水平旋转角度
                    float fleckRotationAngle = currentBulletAngle; // Fleck 的旋转角度与子弹方向一致
                    float velocityAngle = this.fleckAngle.RandomInRange + currentBulletAngle; // Fleck 的速度角度基于子弹方向加上随机偏移
                    float randomInRange2 = this.fleckScale.RandomInRange;
                    float randomInRange3 = this.fleckSpeed.RandomInRange;
 
                    if (Props?.tailFleckDef != null)
                    {
                        FleckCreationData dataStatic = FleckMaker.GetDataStatic(currentPosition, map, Props.tailFleckDef, randomInRange2);
                        dataStatic.rotation = fleckRotationAngle;
                        dataStatic.rotationRate = this.fleckRotation.RandomInRange;
                        dataStatic.velocityAngle = velocityAngle;
                        dataStatic.velocitySpeed = randomInRange3;
                        map.flecks.CreateFleck(dataStatic);
                    }
                }
            }
        }
        
        /// <summary>
        /// 更新击中特效器
        /// </summary>
        private void UpdateHitEffecters()
        {
            if (activeEffecters == null || activeEffecters.Count == 0)
                return;
                
            var ticksGame = Find.TickManager.TicksGame;
            var effectersToRemove = new List<Effecter>();
            var pawnsToRemove = new List<Pawn>();
            
            // 检查每个特效器是否应该结束
            foreach (var kvp in effecterEndTicks)
            {
                if (ticksGame >= kvp.Value || kvp.Key == null || kvp.Key.Destroyed || !kvp.Key.Spawned)
                {
                    pawnsToRemove.Add(kvp.Key);
                }
            }
            
            // 清理结束的特效器
            foreach (var pawn in pawnsToRemove)
            {
                effecterEndTicks.Remove(pawn);
            }
        }
        
        /// <summary>
        /// 清理旧的特效器
        /// </summary>
        private void CleanupOldEffecters()
        {
            if (activeEffecters != null)
            {
                foreach (var effecter in activeEffecters)
                {
                    effecter?.Cleanup();
                }
                activeEffecters.Clear();
            }
            
            effecterEndTicks?.Clear();
        }

        protected override void Impact(Thing hitThing, bool blockedByShield = false)
        {
            CheckPathForDamage(lastTickPosition, this.ExactPosition);
            
            if (hitThing != null && alreadyDamaged.Contains(hitThing))
            {
                base.Impact(null, blockedByShield);
            }
            else
            {
                base.Impact(hitThing, blockedByShield);
            }
        }

        private void CheckPathForDamage(Vector3 startPos, Vector3 endPos)
        {
            if (startPos == endPos) return;

            int maxHits = Props?.maxHits ?? 1;
            bool infinitePenetration = maxHits < 0;

            if (!infinitePenetration && hitCounter >= maxHits) return;

            Map map = this.Map;
            float distance = Vector3.Distance(startPos, endPos);
            Vector3 direction = (endPos - startPos).normalized;

            for (float i = 0; i < distance; i += 0.8f) 
            {
                if (!infinitePenetration && hitCounter >= maxHits) break;

                Vector3 checkPos = startPos + direction * i;
                var thingsInCell = new HashSet<Thing>(map.thingGrid.ThingsListAt(checkPos.ToIntVec3()));

                foreach (Thing thing in thingsInCell)
                {
                   if (thing is Pawn pawn && pawn != this.launcher && !alreadyDamaged.Contains(pawn))
                   {
                       bool shouldDamage = false;

                       // 情况1：如果预期目标是pawn，总是造成伤害。这允许狩猎。
                       if (this.intendedTarget.Thing == pawn)
                       {
                           shouldDamage = true;
                       }
                       // 情况2：总是对路径上的敌对pawn造成伤害。
                       else if (pawn.HostileTo(this.launcher))
                       {
                           shouldDamage = true;
                       }
                       // 情况3：如果射击本身没有标记为防止友军伤害，则对非敌对（友好，中立）造成伤害。
                       else if (!this.preventFriendlyFire)
                       {
                           shouldDamage = true;
                       }

                       if (shouldDamage)
                       {
                           ApplyPathDamage(pawn);
                           if (!infinitePenetration && hitCounter >= maxHits) break;
                       }
                   }
                }
            }
        }

        private void ApplyPathDamage(Pawn pawn)
        {
            Wula_PathPierce_Extension props = Props;
            float falloff = props?.damageFalloff ?? 0.25f;
            
            // 伤害衰减现在普遍适用，即使是无限穿透。
            float damageMultiplier = Mathf.Pow(1f - falloff, hitCounter);
           
            int damageAmount = (int)(this.DamageAmount * damageMultiplier);
            if (damageAmount <= 0) return;
            
            // 检查伤害阈值
            if (props?.damageThreshold > 0 && damageAmount < props.damageThreshold)
            {
                return;
            }

            var dinfo = new DamageInfo(
                this.def.projectile.damageDef,
                damageAmount,
                this.ArmorPenetration * damageMultiplier,
                this.ExactRotation.eulerAngles.y,
                this.launcher,
                null,
                this.equipmentDef,
                DamageInfo.SourceCategory.ThingOrUnknown,
                this.intendedTarget.Thing);
            
            pawn.TakeDamage(dinfo);
            alreadyDamaged.Add(pawn);
            hitCounter++;
            
            // 播放击中特效
            PlayHitEffects(pawn, damageAmount);
        }
        
        /// <summary>
        /// 播放击中敌人时的特效
        /// </summary>
        /// <param name="pawn">被击中的Pawn</param>
        /// <param name="damageAmount">造成的伤害值</param>
        private void PlayHitEffects(Pawn pawn, int damageAmount)
        {
            if (pawn == null || pawn.Destroyed || pawn.Map == null)
                return;
                
            Wula_PathPierce_Extension props = Props;
            if (props == null)
                return;
            
            // 是否对每个命中都播放特效
            if (!props.playEffectOnEveryHit && hitCounter > 1)
                return;
            
            // 播放粒子特效
            PlayHitFleck(pawn);
            
            // 播放效果器特效
            PlayHitEffecter(pawn);
        }
        
        /// <summary>
        /// 播放击中粒子特效
        /// </summary>
        private void PlayHitFleck(Pawn pawn)
        {
            Wula_PathPierce_Extension props = Props;
            if (props == null)
                return;
                
            FleckDef fleckDef = null;
            
            // 选择粒子：优先使用随机列表，然后使用固定粒子
            if (props.randomHitFlecks != null && props.randomHitFlecks.Count > 0)
            {
                fleckDef = props.randomHitFlecks.RandomElement();
            }
            else if (props.hitFleckDef != null)
            {
                fleckDef = props.hitFleckDef;
            }
            
            if (fleckDef != null)
            {
                Vector3 position = pawn.DrawPos + props.effectOffset;
                float scale = props.effectScale;
                
                FleckCreationData data = FleckMaker.GetDataStatic(position, pawn.Map, fleckDef, scale);
                pawn.Map.flecks.CreateFleck(data);
            }
        }
        
        /// <summary>
        /// 播放击中效果器特效
        /// </summary>
        private void PlayHitEffecter(Pawn pawn)
        {
            Wula_PathPierce_Extension props = Props;
            if (props == null)
                return;
                
            EffecterDef effecterDef = null;
            
            // 选择效果器：优先使用随机列表，然后使用固定效果器
            if (props.randomHitEffecters != null && props.randomHitEffecters.Count > 0)
            {
                effecterDef = props.randomHitEffecters.RandomElement();
            }
            else if (props.hitEffecterDef != null)
            {
                effecterDef = props.hitEffecterDef;
            }
            
            if (effecterDef != null)
            {
                Vector3 position = pawn.DrawPos + props.effectOffset;
                
                // 创建效果器
                Effecter effecter = effecterDef.Spawn();
                effecter.Trigger(new TargetInfo(pawn.Position, pawn.Map), new TargetInfo(pawn.Position, pawn.Map));
                
                // 如果需要持续效果，添加到维护列表
                if (props.effectDurationTicks > 0)
                {
                    activeEffecters.Add(effecter);
                    effecterEndTicks[pawn] = Find.TickManager.TicksGame + props.effectDurationTicks;
                }
                else
                {
                    // 立即清理效果器
                    effecter.Cleanup();
                }
            }
        }

        public override void Destroy(DestroyMode mode = DestroyMode.Vanish)
        {
            // 清理所有特效器
            CleanupOldEffecters();
            base.Destroy(mode);
        }
    }
}
