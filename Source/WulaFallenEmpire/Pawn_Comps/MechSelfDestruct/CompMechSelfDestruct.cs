// CompMechSelfDestruct.cs
using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace WulaFallenEmpire
{
    public class CompMechSelfDestruct : ThingComp
    {
        public bool wickStarted;
        public int wickTicksLeft;
        private Thing instigator;
        private List<Thing> thingsIgnoredByExplosion;
        private Sustainer wickSoundSustainer;
        private OverlayHandle? overlayBurningWick;
        
        public CompProperties_MechSelfDestruct Props => (CompProperties_MechSelfDestruct)props;
        
        protected Pawn MechPawn => parent as Pawn;
        
        // 获取当前健康百分比
        private float CurrentHealthPercent
        {
            get
            {
                if (MechPawn == null || MechPawn.Dead || MechPawn.health == null)
                    return 0f;
                    
                return MechPawn.health.summaryHealth.SummaryHealthPercent;
            }
        }
        
        // 获取健康阈值（基于百分比）
        private float HealthThreshold => Props.healthPercentThreshold;
        
        protected virtual bool CanEverExplode
        {
            get
            {
                if (Props.chanceNeverExplode >= 1f)
                    return false;
                    
                if (Props.chanceNeverExplode <= 0f)
                    return true;
                    
                Rand.PushState();
                Rand.Seed = parent.thingIDNumber.GetHashCode();
                bool result = Rand.Value > Props.chanceNeverExplode;
                Rand.PopState();
                return result;
            }
        }
        
        public void AddThingsIgnoredByExplosion(List<Thing> things)
        {
            if (thingsIgnoredByExplosion == null)
            {
                thingsIgnoredByExplosion = new List<Thing>();
            }
            thingsIgnoredByExplosion.AddRange(things);
        }
        
        public override void PostExposeData()
        {
            base.PostExposeData();
            Scribe_References.Look(ref instigator, "instigator");
            Scribe_Collections.Look(ref thingsIgnoredByExplosion, "thingsIgnoredByExplosion", LookMode.Reference);
            Scribe_Values.Look(ref wickStarted, "wickStarted", defaultValue: false);
            Scribe_Values.Look(ref wickTicksLeft, "wickTicksLeft", 0);
        }
        
        public override void PostSpawnSetup(bool respawningAfterLoad)
        {
            UpdateOverlays();
        }
        
        public override void CompTick()
        {
            if (MechPawn == null || MechPawn.Dead)
                return;
                
            // 检查健康阈值（每60ticks检查一次以提高性能）
            if (Find.TickManager.TicksGame % 60 == 0)
            {
                CheckHealthThreshold();
            }
            
            if (!wickStarted)
                return;
                
            // 处理引信
            if (wickSoundSustainer == null)
            {
                StartWickSustainer();
            }
            else
            {
                wickSoundSustainer.Maintain();
            }
            
            wickTicksLeft--;
            if (wickTicksLeft <= 0)
            {
                Detonate(parent.MapHeld);
            }
        }
        
        private void CheckHealthThreshold()
        {
            if (wickStarted || !CanEverExplode || !Props.triggerOnHealthThreshold)
                return;
                
            float currentHealthPercent = CurrentHealthPercent;
            
            if (currentHealthPercent <= HealthThreshold && currentHealthPercent > 0f)
            {
                StartWick();
            }
        }
        
        private void StartWickSustainer()
        {
            SoundDefOf.MetalHitImportant.PlayOneShot(new TargetInfo(parent.PositionHeld, parent.MapHeld));
            SoundInfo info = SoundInfo.InMap(parent, MaintenanceType.PerTick);
            wickSoundSustainer = SoundDefOf.HissSmall.TrySpawnSustainer(info);
        }
        
        private void EndWickSustainer()
        {
            if (wickSoundSustainer != null)
            {
                wickSoundSustainer.End();
                wickSoundSustainer = null;
            }
        }
        
        private void UpdateOverlays()
        {
            if (parent.Spawned && Props.drawWick)
            {
                parent.Map.overlayDrawer.Disable(parent, ref overlayBurningWick);
                if (wickStarted)
                {
                    overlayBurningWick = parent.Map.overlayDrawer.Enable(parent, OverlayTypes.BurningWick);
                }
            }
        }
        
        public override void PostDestroy(DestroyMode mode, Map previousMap)
        {
            if (Props.triggerOnDeath && mode == DestroyMode.KillFinalize)
            {
                Detonate(previousMap, ignoreUnspawned: true);
            }
        }
        
        public override void PostDeSpawn(Map map, DestroyMode mode = DestroyMode.Vanish)
        {
            base.PostDeSpawn(map, mode);
            EndWickSustainer();
            StopWick();
        }
        
        public override void PostPreApplyDamage(ref DamageInfo dinfo, out bool absorbed)
        {
            absorbed = false;
            
            if (!CanEverExplode || MechPawn == null || MechPawn.Dead)
                return;
                
            // 特定伤害类型触发自毁
            if (!wickStarted && Props.startWickOnDamageTaken != null && 
                Props.startWickOnDamageTaken.Contains(dinfo.Def))
            {
                StartWick(dinfo.Instigator);
            }
        }
        
        public override void PostPostApplyDamage(DamageInfo dinfo, float totalDamageDealt)
        {
            if (!CanEverExplode || MechPawn == null || MechPawn.Dead || wickStarted)
                return;
                
            // 内部伤害触发自毁
            if (Props.startWickOnInternalDamageTaken != null && 
                Props.startWickOnInternalDamageTaken.Contains(dinfo.Def))
            {
                StartWick(dinfo.Instigator);
            }
            
            // 特定伤害类型触发自毁
            if (!wickStarted && Props.startWickOnDamageTaken != null && 
                Props.startWickOnDamageTaken.Contains(dinfo.Def))
            {
                StartWick(dinfo.Instigator);
            }
            
            // 检查是否需要停止引信（如眩晕）
            if (wickStarted && dinfo.Def == DamageDefOf.Stun)
            {
                StopWick();
            }
        }
        
        public void StartWick(Thing instigator = null)
        {
            if (!wickStarted && ExplosiveRadius() > 0f && CanEverExplode)
            {
                this.instigator = instigator;
                wickStarted = true;
                wickTicksLeft = Props.wickTicks.RandomInRange;
                StartWickSustainer();
                GenExplosion.NotifyNearbyPawnsOfDangerousExplosive(parent, Props.explosiveDamageType, null, instigator);
                UpdateOverlays();
            }
        }
        
        public void StopWick()
        {
            wickStarted = false;
            instigator = null;
            UpdateOverlays();
            EndWickSustainer();
        }
        
        public float ExplosiveRadius()
        {
            float radius = Props.explosiveRadius;
            
            // 根据堆叠数量扩展
            if (parent.stackCount > 1 && Props.explosiveExpandPerStackcount > 0f)
            {
                radius += Mathf.Sqrt((parent.stackCount - 1) * Props.explosiveExpandPerStackcount);
            }
            
            // 根据燃料扩展
            if (Props.explosiveExpandPerFuel > 0f && parent.GetComp<CompRefuelable>() != null)
            {
                radius += Mathf.Sqrt(parent.GetComp<CompRefuelable>().Fuel * Props.explosiveExpandPerFuel);
            }
            
            return radius;
        }
        
        protected void Detonate(Map map, bool ignoreUnspawned = false)
        {
            if (!ignoreUnspawned && !parent.SpawnedOrAnyParentSpawned)
                return;
                
            float radius = ExplosiveRadius();
            if (radius <= 0f)
                return;
                
            Thing responsible = (instigator == null || (instigator.HostileTo(parent.Faction) && parent.Faction != Faction.OfPlayer)) 
                ? parent 
                : instigator;
            
            // 消耗燃料
            if (Props.explosiveExpandPerFuel > 0f && parent.GetComp<CompRefuelable>() != null)
            {
                parent.GetComp<CompRefuelable>().ConsumeFuel(parent.GetComp<CompRefuelable>().Fuel);
            }

            EndWickSustainer();
            wickStarted = false;
            UpdateOverlays();
            
            if (map == null)
            {
                Log.Warning("Tried to detonate CompMechSelfDestruct in a null map.");
                return;
            }
            
            // 播放爆炸效果
            if (Props.explosionEffect != null)
            {
                Effecter effecter = Props.explosionEffect.Spawn();
                effecter.Trigger(new TargetInfo(parent.PositionHeld, map), new TargetInfo(parent.PositionHeld, map));
                effecter.Cleanup();
            }

            // 执行爆炸
            GenExplosion.DoExplosion(
                parent.PositionHeld, 
                map, 
                radius, 
                Props.explosiveDamageType, 
                responsible, 
                Props.damageAmountBase, 
                Props.armorPenetrationBase, 
                Props.explosionSound, 
                null, null, null, 
                Props.postExplosionSpawnThingDef, 
                Props.postExplosionSpawnChance, 
                Props.postExplosionSpawnThingCount, 
                Props.postExplosionGasType, 
                Props.postExplosionGasRadiusOverride, 
                Props.postExplosionGasAmount, 
                Props.applyDamageToExplosionCellsNeighbors, 
                Props.preExplosionSpawnThingDef, 
                Props.preExplosionSpawnChance, 
                Props.preExplosionSpawnThingCount, 
                Props.chanceToStartFire, 
                Props.damageFalloff, 
                null, 
                thingsIgnoredByExplosion, 
                null, 
                Props.doVisualEffects, 
                Props.propagationSpeed, 
                0f, 
                Props.doSoundEffects, 
                null, 1f, null, null, 
                Props.postExplosionSpawnSingleThingDef, 
                Props.preExplosionSpawnSingleThingDef
            );

            if (!MechPawn.Dead)
            {
                MechPawn.Kill(null, null);
            }
        }
    }
}
