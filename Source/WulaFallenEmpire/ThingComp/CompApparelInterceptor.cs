using HarmonyLib;
using RimWorld;
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace WulaFallenEmpire
{
    public class CompProperties_ApparelInterceptor : CompProperties
    {
        public float radius = 3f;
        public int startupDelay = 0;
        public int rechargeDelay = 3200;
        public int hitPoints = 100;
        public int maxBounces = 3;
        public float bounceRange = 15f; // 反弹射程

        public bool interceptGroundProjectiles = false;
        public bool interceptNonHostileProjectiles = false;
        public bool interceptAirProjectiles = true;

        public EffecterDef soundInterceptEffecter;
        public EffecterDef soundBreakEffecter;
        public EffecterDef reactivateEffect;

        public Color color = new Color(0.5f, 0.5f, 0.9f);
        public bool drawWithNoSelection = true;
        public bool isImmuneToEMP = false;

        public int cooldownTicks = 0;
        public int chargeDurationTicks = 0;
        public int chargeIntervalTicks = 0;
        public bool startWithMaxHitPoints = true;
        public bool hitPointsRestoreInstantlyAfterCharge = true;
        public int rechargeHitPointsIntervalTicks = 60;
        public bool activated = false;
        public int activeDuration = 0;
        public SoundDef activeSound;
        public bool alwaysShowHitpointsGizmo = false;
        public float minAlpha = 0f;
        public float idlePulseSpeed = 0.02f;
        public float minIdleAlpha = 0.05f;
        public int disarmedByEmpForTicks = 0;

        public CompProperties_ApparelInterceptor()
        {
            compClass = typeof(CompApparelInterceptor);
        }
    }

    [StaticConstructorOnStartup]
    public class CompApparelInterceptor : ThingComp
    {
        // 状态变量
        private int lastInterceptTicks = -999999;
        private int startedChargingTick = -1;
        private bool shutDown;
        private StunHandler stunner;
        private Sustainer sustainer;
        public int currentHitPoints = -1;
        private int ticksToReset;
        private int activatedTick = -999999;
        private bool initialized = false;
        
        // 视觉效果变量
        private float lastInterceptAngle;
        private bool drawInterceptCone;
        
        // 静态资源
        private static readonly Material ForceFieldMat = MaterialPool.MatFrom("Other/ForceField", ShaderDatabase.MoteGlow);
        private static readonly Material ForceFieldConeMat = MaterialPool.MatFrom("Other/ForceFieldCone", ShaderDatabase.MoteGlow);
        private static readonly MaterialPropertyBlock MatPropertyBlock = new MaterialPropertyBlock();
        private static readonly Color InactiveColor = new Color(0.2f, 0.2f, 0.2f);
        
        // 属性
        public CompProperties_ApparelInterceptor Props => (CompProperties_ApparelInterceptor)props;
        public Pawn PawnOwner => (parent as Apparel)?.Wearer;


        private void BounceProjectileNew(Projectile originalProjectile)
        {
            try
            {
                if (originalProjectile == null || originalProjectile.Destroyed)
                    return;
                // 计算反弹方向 - 朝穿戴者前方发射
                Vector3 bounceDirection = CalculateForwardBounceDirection();

                // 计算新目标位置
                Vector3 newDestination = PawnOwner.Position.ToVector3Shifted() + bounceDirection * Props.bounceRange;
                IntVec3 targetCell = newDestination.ToIntVec3();
                // 创建新的抛射体
                Projectile newProjectile = (Projectile)ThingMaker.MakeThing(originalProjectile.def, null);

                // 使用 Traverse 复制字段
                CopyProjectileFieldsUsingTraverse(newProjectile, originalProjectile);
                // 生成新抛射体
                GenSpawn.Spawn(newProjectile, PawnOwner.Position, PawnOwner.Map);

                // 使用 Traverse 调用 Launch 方法
                LaunchProjectileUsingTraverse(newProjectile, targetCell, originalProjectile);
                // 销毁原抛射体
                originalProjectile.Destroy(DestroyMode.Vanish);
                // 播放反弹效果
                PlayBounceEffect(originalProjectile);
                Log.Message($"[Interceptor] Projectile bounced forward to {targetCell}");
            }
            catch (Exception ex)
            {
                Log.Error($"Error in BounceProjectileNew: {ex}");
            }
        }
        // 使用 Traverse 复制字段
        private void CopyProjectileFieldsUsingTraverse(Projectile newProjectile, Projectile originalProjectile)
        {
            try
            {
                Traverse newTraverse = Traverse.Create(newProjectile);
                Traverse originalTraverse = Traverse.Create(originalProjectile);
                // 复制所有重要字段
                newTraverse.Field("launcher").SetValue(originalTraverse.Field("launcher").GetValue());
                newTraverse.Field("equipment").SetValue(originalTraverse.Field("equipment").GetValue());
                newTraverse.Field("equipmentDef").SetValue(originalTraverse.Field("equipmentDef").GetValue());
                newTraverse.Field("damageDefOverride").SetValue(originalTraverse.Field("damageDefOverride").GetValue());
                newTraverse.Field("targetCoverDef").SetValue(originalTraverse.Field("targetCoverDef").GetValue());

                // 复制额外伤害列表
                List<ExtraDamage> originalExtraDamages = originalTraverse.Field("extraDamages").GetValue<List<ExtraDamage>>();
                if (originalExtraDamages != null)
                {
                    newTraverse.Field("extraDamages").SetValue(new List<ExtraDamage>(originalExtraDamages));
                }
            }
            catch (Exception ex)
            {
                Log.Warning($"Error copying projectile fields with Traverse: {ex}");
            }
        }
        // 使用 Traverse 调用 Launch 方法
        private void LaunchProjectileUsingTraverse(Projectile projectile, IntVec3 targetCell, Projectile originalProjectile)
        {
            try
            {
                Traverse projectileTraverse = Traverse.Create(projectile);
                Traverse originalTraverse = Traverse.Create(originalProjectile);
                // 获取 Launch 方法
                var launchMethod = projectileTraverse.Method("Launch", new object[]
                {
                    PawnOwner, // 发射者
                    PawnOwner.Position.ToVector3Shifted(), // 发射位置
                    new LocalTargetInfo(targetCell), // 目标位置
                    new LocalTargetInfo(targetCell), // 预期目标
                    originalProjectile.HitFlags, // 命中标志
                    originalTraverse.Field("preventFriendlyFire").GetValue<bool>(), // 防止友军伤害
                    originalTraverse.Field("equipment").GetValue<Thing>(), // 装备
                    originalTraverse.Field("targetCoverDef").GetValue<ThingDef>() // 目标覆盖定义
                });
                if (launchMethod.MethodExists())
                {
                    launchMethod.GetValue();
                }
                else
                {
                    Log.Error("Launch method not found using Traverse");
                }
            }
            catch (Exception ex)
            {
                Log.Error($"Error launching projectile with Traverse: {ex}");
            }
        }
        // 使用反射设置抛射体字段
        private void SetProjectileFields(Projectile newProjectile, Projectile originalProjectile)
        {
            try
            {
                // 获取 Projectile 类型
                Type projectileType = typeof(Projectile);

                // 设置发射者
                FieldInfo launcherField = projectileType.GetField("launcher", BindingFlags.Instance | BindingFlags.NonPublic);
                launcherField?.SetValue(newProjectile, originalProjectile.Launcher);

                // 设置装备
                FieldInfo equipmentField = projectileType.GetField("equipment", BindingFlags.Instance | BindingFlags.NonPublic);
                equipmentField?.SetValue(newProjectile, GetEquipment(originalProjectile));

                // 设置装备定义
                FieldInfo equipmentDefField = projectileType.GetField("equipmentDef", BindingFlags.Instance | BindingFlags.NonPublic);
                equipmentDefField?.SetValue(newProjectile, GetEquipmentDef(originalProjectile));

                // 设置伤害定义覆盖
                FieldInfo damageDefOverrideField = projectileType.GetField("damageDefOverride", BindingFlags.Instance | BindingFlags.NonPublic);
                damageDefOverrideField?.SetValue(newProjectile, GetDamageDefOverride(originalProjectile));

                // 设置额外伤害
                FieldInfo extraDamagesField = projectileType.GetField("extraDamages", BindingFlags.Instance | BindingFlags.NonPublic);
                if (extraDamagesField != null)
                {
                    List<ExtraDamage> originalExtraDamages = (List<ExtraDamage>)extraDamagesField.GetValue(originalProjectile);
                    if (originalExtraDamages != null)
                    {
                        List<ExtraDamage> newExtraDamages = new List<ExtraDamage>(originalExtraDamages);
                        extraDamagesField.SetValue(newProjectile, newExtraDamages);
                    }
                }

                // 设置目标覆盖定义
                FieldInfo targetCoverDefField = projectileType.GetField("targetCoverDef", BindingFlags.Instance | BindingFlags.NonPublic);
                targetCoverDefField?.SetValue(newProjectile, GetTargetCoverDef(originalProjectile));
            }
            catch (Exception ex)
            {
                Log.Warning($"Error setting projectile fields: {ex}");
            }
        }
        // 使用反射调用 Launch 方法
        private void LaunchProjectile(Projectile projectile, IntVec3 targetCell, Projectile originalProjectile)
        {
            try
            {
                Type projectileType = typeof(Projectile);

                // 获取 Launch 方法
                MethodInfo launchMethod = projectileType.GetMethod("Launch", new Type[]
                {
                    typeof(Thing),
                    typeof(Vector3),
                    typeof(LocalTargetInfo),
                    typeof(LocalTargetInfo),
                    typeof(ProjectileHitFlags),
                    typeof(bool),
                    typeof(Thing),
                    typeof(ThingDef)
                });
                if (launchMethod != null)
                {
                    // 获取原抛射体的命中标志
                    ProjectileHitFlags hitFlags = GetHitFlags(originalProjectile);

                    // 获取防止友军伤害设置
                    bool preventFriendlyFire = GetPreventFriendlyFire(originalProjectile);

                    // 调用 Launch 方法
                    launchMethod.Invoke(projectile, new object[]
                    {
                        PawnOwner, // 发射者改为护盾穿戴者
                        PawnOwner.Position.ToVector3Shifted(), // 发射位置
                        new LocalTargetInfo(targetCell), // 目标位置
                        new LocalTargetInfo(targetCell), // 预期目标
                        hitFlags,
                        preventFriendlyFire,
                        GetEquipment(originalProjectile), // 装备
                        GetTargetCoverDef(originalProjectile) // 目标覆盖定义
                    });
                }
                else
                {
                    Log.Error("Could not find Launch method on Projectile");
                }
            }
            catch (Exception ex)
            {
                Log.Error($"Error launching projectile: {ex}");
            }
        }
        // 使用反射获取私有字段值
        private Thing GetEquipment(Projectile projectile)
        {
            try
            {
                FieldInfo equipmentField = typeof(Projectile).GetField("equipment", BindingFlags.Instance | BindingFlags.NonPublic);
                return (Thing)equipmentField?.GetValue(projectile);
            }
            catch
            {
                return null;
            }
        }
        private ThingDef GetEquipmentDef(Projectile projectile)
        {
            try
            {
                FieldInfo equipmentDefField = typeof(Projectile).GetField("equipmentDef", BindingFlags.Instance | BindingFlags.NonPublic);
                return (ThingDef)equipmentDefField?.GetValue(projectile);
            }
            catch
            {
                return null;
            }
        }
        private DamageDef GetDamageDefOverride(Projectile projectile)
        {
            try
            {
                FieldInfo damageDefOverrideField = typeof(Projectile).GetField("damageDefOverride", BindingFlags.Instance | BindingFlags.NonPublic);
                return (DamageDef)damageDefOverrideField?.GetValue(projectile);
            }
            catch
            {
                return null;
            }
        }
        private ThingDef GetTargetCoverDef(Projectile projectile)
        {
            try
            {
                FieldInfo targetCoverDefField = typeof(Projectile).GetField("targetCoverDef", BindingFlags.Instance | BindingFlags.NonPublic);
                return (ThingDef)targetCoverDefField?.GetValue(projectile);
            }
            catch
            {
                return null;
            }
        }
        private ProjectileHitFlags GetHitFlags(Projectile projectile)
        {
            try
            {
                return projectile.HitFlags;
            }
            catch
            {
                return ProjectileHitFlags.All;
            }
        }
        private bool GetPreventFriendlyFire(Projectile projectile)
        {
            try
            {
                FieldInfo preventFriendlyFireField = typeof(Projectile).GetField("preventFriendlyFire", BindingFlags.Instance | BindingFlags.NonPublic);
                return preventFriendlyFireField != null && (bool)preventFriendlyFireField.GetValue(projectile);
            }
            catch
            {
                return false;
            }
        }

        // 主要拦截方法
        public bool TryInterceptProjectile(Projectile projectile, Thing hitThing)
        {
            try
            {
                EnsureInitialized();
                
                // 基础检查
                if (PawnOwner == null || !PawnOwner.Spawned || PawnOwner.Dead || PawnOwner.Downed)
                    return false;
                    
                if (projectile == null || projectile.Destroyed || !projectile.Spawned)
                    return false;
                    
                if (!Active)
                    return false;
                    
                // 检查地图匹配
                if (PawnOwner.Map == null || projectile.Map == null || PawnOwner.Map != projectile.Map)
                    return false;
                    
                // 关键检查：抛射体是否要击中这个护盾的穿戴者？
                if (hitThing != PawnOwner)
                    return false;
                    
                // 检查抛射体类型
                if (!InterceptsProjectile(Props, projectile))
                    return false;
                    
                // 检查敌对关系
                bool isHostile = (projectile.Launcher != null && projectile.Launcher.HostileTo(PawnOwner)) ||
                                (projectile.Launcher == null && Props.interceptNonHostileProjectiles);
                if (!isHostile)
                    return false;

                // --- 拦截成功 ---
                InterceptSuccess(projectile);
                return true;
            }
            catch (Exception ex)
            {
                Log.Warning($"[CompApparelInterceptor] Error in TryInterceptProjectile: {ex.Message}");
                return false;
            }
        }

        private void InterceptSuccess(Projectile projectile)
        {
            try
            {
                // 记录拦截角度用于视觉效果
                lastInterceptAngle = projectile.ExactPosition.AngleToFlat(PawnOwner.TrueCenter());
                lastInterceptTicks = Find.TickManager.TicksGame;
                drawInterceptCone = true;

                // 播放拦截效果
                if (Props.soundInterceptEffecter != null && PawnOwner.Map != null)
                {
                    var effecter = Props.soundInterceptEffecter.Spawn(PawnOwner.Position, PawnOwner.Map);
                    if (effecter != null)
                        effecter.Cleanup();
                }

                // 处理伤害类型
                if (projectile.DamageDef == DamageDefOf.EMP && !Props.isImmuneToEMP)
                {
                    BreakShieldEmp(new DamageInfo(projectile.DamageDef, projectile.DamageAmount, instigator: projectile.Launcher));
                }
                else
                {
                    // 固定减少1点护盾量
                    currentHitPoints = Mathf.Max(0, currentHitPoints - 1);
                    if (currentHitPoints <= 0)
                    {
                        BreakShieldHitpoints(new DamageInfo(projectile.DamageDef, 1, instigator: projectile.Launcher));
                    }
                }

                // 反弹抛射体 - 新方法：销毁原抛射体并创建新的
                BounceProjectileNew(projectile);
            }
            catch (Exception ex)
            {
                Log.Warning($"[CompApparelInterceptor] Error during interception effects: {ex.Message}");
            }
        }

        private Vector3 CalculateForwardBounceDirection()
        {
            // 获取穿戴者的朝向
            float pawnRotation = PawnOwner.Rotation.AsAngle;
            
            // 添加一些随机偏移，使反弹更自然
            float randomOffset = Rand.Range(-30f, 30f);
            float finalAngle = pawnRotation + randomOffset;
            
            // 转换为方向向量
            return Quaternion.AngleAxis(finalAngle, Vector3.up) * Vector3.forward;
        }

        private void PlayBounceEffect(Projectile projectile)
        {
            // 播放自定义反弹效果
            if (Props.soundInterceptEffecter != null)
            {
                var effecter = Props.soundInterceptEffecter.Spawn(projectile.Position, projectile.Map);
                effecter?.Cleanup();
            }

            // 添加视觉特效
            FleckMaker.Static(projectile.ExactPosition, projectile.Map, FleckDefOf.ShotFlash, 1.5f);
        }

        // 其余现有方法保持不变...
        private void EnsureInitialized()
        {
            if (initialized) return;

            if (stunner == null)
                stunner = new StunHandler(parent);

            if (currentHitPoints == -1)
                currentHitPoints = Props.startupDelay > 0 ? 0 : HitPointsMax;

            initialized = true;
        }

        public bool Active
        {
            get
            {
                EnsureInitialized();
                if (PawnOwner == null || !PawnOwner.Spawned) return false;

                if (stunner == null || OnCooldown || Charging || (stunner != null && stunner.Stunned) || shutDown || currentHitPoints <= 0)
                    return false;

                if (Props.activated && Find.TickManager.TicksGame > activatedTick + Props.activeDuration)
                    return false;

                return true;
            }
        }

        // ... 其余现有属性和方法保持不变
        public bool OnCooldown => ticksToReset > 0;
        public bool Charging => startedChargingTick >= 0 && Find.TickManager.TicksGame < startedChargingTick + Props.startupDelay;
        public int CooldownTicksLeft => ticksToReset;
        public int ChargingTicksLeft => (startedChargingTick < 0) ? 0 : Mathf.Max(startedChargingTick + Props.startupDelay - Find.TickManager.TicksGame, 0);
        public int HitPointsMax => Props.hitPoints;
        protected virtual int HitPointsPerInterval => 1;

        public override void PostPostMake()
        {
            base.PostPostMake();
            EnsureInitialized();

            if (Props.startupDelay > 0)
            {
                startedChargingTick = Find.TickManager.TicksGame;
            }
        }

        public override void PostExposeData()
        {
            base.PostExposeData();
            Scribe_Values.Look(ref lastInterceptTicks, "lastInterceptTicks", -999999);
            Scribe_Values.Look(ref shutDown, "shutDown", defaultValue: false);
            Scribe_Values.Look(ref startedChargingTick, "startedChargingTick", -1);
            Scribe_Values.Look(ref currentHitPoints, "currentHitPoints", -1);
            Scribe_Values.Look(ref ticksToReset, "ticksToReset", 0);
            Scribe_Values.Look(ref activatedTick, "activatedTick", -999999);
            Scribe_Values.Look(ref initialized, "initialized", false);
            Scribe_Deep.Look(ref stunner, "stunner", parent);
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                initialized = false;
                EnsureInitialized();
            }
        }

        public override void CompTick()
        {
            try
            {
                base.CompTick();
                EnsureInitialized();

                if (PawnOwner == null || !PawnOwner.Spawned) return;
                
                if (stunner != null)
                    stunner.StunHandlerTick();
                    
                if (OnCooldown)
                {
                    ticksToReset--;
                    if (ticksToReset <= 0) Reset();
                }
                else if (Charging)
                {
                    // Charging logic handled by property
                }
                else if (currentHitPoints < HitPointsMax && parent.IsHashIntervalTick(Props.rechargeHitPointsIntervalTicks))
                {
                    currentHitPoints = Mathf.Clamp(currentHitPoints + HitPointsPerInterval, 0, HitPointsMax);
                }
                
                if (Props.activeSound != null)
                {
                    if (Active && (sustainer == null || sustainer.Ended))
                        sustainer = Props.activeSound.TrySpawnSustainer(SoundInfo.InMap(parent));
                    sustainer?.Maintain();
                    if (!Active && sustainer != null && !sustainer.Ended)
                        sustainer.End();
                }
            }
            catch (System.Exception ex)
            {
                Log.Warning($"[CompApparelInterceptor] Error in CompTick: {ex.Message}");
            }
        }

        public void Reset()
        {
            if (PawnOwner != null && PawnOwner.Spawned && PawnOwner.Map != null)
                Props.reactivateEffect?.Spawn(PawnOwner.Position, PawnOwner.Map).Cleanup();

            currentHitPoints = HitPointsMax;
            ticksToReset = 0;
        }

        private void BreakShieldHitpoints(DamageInfo dinfo)
        {
            if (PawnOwner != null && PawnOwner.Spawned && PawnOwner.MapHeld != null)
            {
                if (Props.soundBreakEffecter != null)
                    Props.soundBreakEffecter.SpawnAttached(PawnOwner, PawnOwner.MapHeld, Props.radius).Cleanup();
            }
            currentHitPoints = 0;
            ticksToReset = Props.rechargeDelay;
        }

        private void BreakShieldEmp(DamageInfo dinfo)
        {
            BreakShieldHitpoints(dinfo);
            if (Props.disarmedByEmpForTicks > 0 && stunner != null)
                stunner.Notify_DamageApplied(new DamageInfo(DamageDefOf.EMP, (float)Props.disarmedByEmpForTicks / 30f));
        }

        public static bool InterceptsProjectile(CompProperties_ApparelInterceptor props, Projectile projectile)
        {
            if (projectile == null || projectile.def == null || projectile.def.projectile == null)
                return false;

            if (projectile.def.projectile.flyOverhead)
                return props.interceptAirProjectiles;
            return props.interceptGroundProjectiles;
        }

        // 视觉效果相关方法保持不变...
        protected bool ShouldDisplay
        {
            get
            {
                EnsureInitialized();
                if (PawnOwner == null || !PawnOwner.Spawned || PawnOwner.Dead || PawnOwner.Downed || !Active)
                    return false;
                    
                if (PawnOwner.Drafted || PawnOwner.InAggroMentalState || 
                    (PawnOwner.Faction != null && PawnOwner.Faction.HostileTo(Faction.OfPlayer) && !PawnOwner.IsPrisoner))
                    return true;
                    
                if (Find.Selector.IsSelected(PawnOwner))
                    return true;
                    
                return false;
            }
        }

        public override void CompDrawWornExtras()
        {
            try
            {
                base.CompDrawWornExtras();
                EnsureInitialized();

                if (PawnOwner == null || !PawnOwner.Spawned || !ShouldDisplay) return;
                
                Vector3 drawPos = PawnOwner.Drawer?.DrawPos ?? PawnOwner.Position.ToVector3Shifted();
                drawPos.y = AltitudeLayer.MoteOverhead.AltitudeFor();
                
                float alpha = GetCurrentAlpha();
                if (alpha > 0f)
                {
                    Color color = Props.color;
                    color.a *= alpha;
                    MatPropertyBlock.SetColor(ShaderPropertyIDs.Color, color);
                    Matrix4x4 matrix = default(Matrix4x4);
                    matrix.SetTRS(drawPos, Quaternion.identity, new Vector3(Props.radius * 2f * 1.1601562f, 1f, Props.radius * 2f * 1.1601562f));
                    Graphics.DrawMesh(MeshPool.plane10, matrix, ForceFieldMat, 0, null, 0, MatPropertyBlock);
                }
                
                float coneAlpha = GetCurrentConeAlpha_RecentlyIntercepted();
                if (coneAlpha > 0f)
                {
                    Color color = Props.color;
                    color.a *= coneAlpha;
                    MatPropertyBlock.SetColor(ShaderPropertyIDs.Color, color);
                    Matrix4x4 matrix = default(Matrix4x4);
                    matrix.SetTRS(drawPos, Quaternion.Euler(0f, lastInterceptAngle - 90f, 0f), new Vector3(Props.radius * 2f, 1f, Props.radius * 2f));
                    Graphics.DrawMesh(MeshPool.plane10, matrix, ForceFieldConeMat, 0, null, 0, MatPropertyBlock);
                }
            }
            catch (System.Exception ex)
            {
                Log.Warning($"[CompApparelInterceptor] Error in CompDrawWornExtras: {ex.Message}");
            }
        }

        private float GetCurrentAlpha()
        {
            float idleAlpha = Mathf.Lerp(0.3f, 0.6f, (Mathf.Sin((float)Gen.HashCombineInt(parent.thingIDNumber, 35990913) + Time.realtimeSinceStartup * 2f) + 1f) / 2f);
            float interceptAlpha = Mathf.Clamp01(1f - (float)(Find.TickManager.TicksGame - lastInterceptTicks) / 40f);
            return Mathf.Max(idleAlpha, interceptAlpha);
        }

        private float GetCurrentConeAlpha_RecentlyIntercepted()
        {
            if (!drawInterceptCone) return 0f;
            return Mathf.Clamp01(1f - (float)(Find.TickManager.TicksGame - lastInterceptTicks) / 40f) * 0.82f;
        }

        // Gizmo 相关保持不变...
        public override IEnumerable<Gizmo> CompGetWornGizmosExtra()
        {
            EnsureInitialized();

            if (PawnOwner != null && Find.Selector.SingleSelectedThing == PawnOwner)
            {
                yield return new Gizmo_EnergyShieldStatus { shield = this };
            }
        }

        public override string CompInspectStringExtra()
        {
            EnsureInitialized();

            StringBuilder sb = new StringBuilder();
            if (OnCooldown)
            {
                sb.Append("Cooldown: " + CooldownTicksLeft.ToStringTicksToPeriod());
            }
            else if (stunner != null && stunner.Stunned)
            {
                sb.Append("EMP Shutdown: " + stunner.StunTicksLeft.ToStringTicksToPeriod());
            }
            return sb.ToString();
        }
    }

    // Gizmo 类保持不变...
    [StaticConstructorOnStartup]
    public class Gizmo_EnergyShieldStatus : Gizmo
    {
        public CompApparelInterceptor shield;
        private static readonly Texture2D FullShieldBarTex = SolidColorMaterials.NewSolidColorMaterial(new Color(0.2f, 0.8f, 0.85f), ShaderDatabase.MetaOverlay).mainTexture as Texture2D;
        private static readonly Texture2D EmptyShieldBarTex = SolidColorMaterials.NewSolidColorMaterial(new Color(0.2f, 0.2f, 0.24f), ShaderDatabase.MetaOverlay).mainTexture as Texture2D;

        public override float GetWidth(float maxWidth) => 140f;

        public override GizmoResult GizmoOnGUI(Vector2 topLeft, float maxWidth, GizmoRenderParms parms)
        {
            Rect rect = new Rect(topLeft.x, topLeft.y, GetWidth(maxWidth), 75f);
            Rect rect2 = rect.ContractedBy(6f);
            Widgets.DrawWindowBackground(rect);

            Rect labelRect = rect2;
            labelRect.height = rect.height / 2f;
            Text.Font = GameFont.Tiny;
            Widgets.Label(labelRect, shield.parent.LabelCap);

            Rect barRect = rect2;
            barRect.yMin = rect2.y + rect2.height / 2f;
            float fillPercent = (float)shield.currentHitPoints / shield.HitPointsMax;
            Widgets.FillableBar(barRect, fillPercent, FullShieldBarTex, EmptyShieldBarTex, false);

            Text.Font = GameFont.Small;
            Text.Anchor = TextAnchor.MiddleCenter;

            TaggedString statusText = shield.OnCooldown ? "Broken".Translate() : new TaggedString(shield.currentHitPoints + " / " + shield.HitPointsMax);
            Widgets.Label(barRect, statusText);

            Text.Anchor = TextAnchor.UpperLeft;

            return new GizmoResult(GizmoState.Clear);
        }
    }
}
