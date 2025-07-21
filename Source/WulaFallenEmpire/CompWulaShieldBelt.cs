using RimWorld;
using System.Collections.Generic;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace WulaFallenEmpire
{
    [StaticConstructorOnStartup]
    public class CompWulaShieldBelt : ThingComp
    {
        private float shieldHitPoints;
        private int ticksToReset = -1;
        private int lastKeepDisplayTick = -9999;
        private Vector3 impactAngleVect;
        private int lastAbsorbDamageTick = -9999;
        private bool shieldEnabled = false;
        private Sustainer sustainer;
        // 静态构造函数加载材质
        private static readonly Material BubbleMat = MaterialPool.MatFrom("Other/ShieldBubble", ShaderDatabase.Transparent, Color.white);

        public CompProperties_WulaShieldBelt Props => (CompProperties_WulaShieldBelt)props;

        public float ShieldHitPoints => shieldHitPoints;
        public float ShieldMaxHitPoints => Props.maxShieldHitPoints;
        public bool ShieldEnabled => shieldEnabled;

        private bool ShouldDisplay
        {
            get
            {
                Pawn wearer = GetWearer();
                return wearer != null && wearer.Spawned && (wearer.Drafted || (wearer.Faction != null && wearer.Faction.IsPlayer) || Find.TickManager.TicksGame < lastKeepDisplayTick + 50);
            }
        }

        public override void PostExposeData()
        {
            base.PostExposeData();
            Scribe_Values.Look(ref shieldHitPoints, "shieldHitPoints", 0f);
            Scribe_Values.Look(ref ticksToReset, "ticksToReset", -1);
            Scribe_Values.Look(ref shieldEnabled, "shieldEnabled", Props.startEnabled);
        }

        public override void PostSpawnSetup(bool respawningAfterLoad)
        {
            base.PostSpawnSetup(respawningAfterLoad);
            if (!respawningAfterLoad)
            {
                shieldHitPoints = Props.maxShieldHitPoints;
                shieldEnabled = Props.startEnabled;
            }
        }

        public override void CompTick()
        {
            base.CompTick();
            
            Pawn wearer = GetWearer();
            if (wearer == null) return;

            if (shieldEnabled)
            {
                if (sustainer == null && Props.activeSound != null)
                {
                    sustainer = Props.activeSound.TrySpawnSustainer(SoundInfo.InMap(wearer, MaintenanceType.PerTick));
                }
                sustainer?.Maintain();
            }
            else
            {
                sustainer?.End();
                sustainer = null;
            }

            if (ticksToReset > 0)
            {
                ticksToReset--;
                if (ticksToReset <= 0)
                {
                    Reset();
                }
            }
            else if (shieldEnabled && Props.useHitPointsMode && shieldHitPoints < Props.maxShieldHitPoints)
            {
                shieldHitPoints += Props.rechargeRate / 60f; // 每秒恢复
                if (shieldHitPoints > Props.maxShieldHitPoints)
                {
                    shieldHitPoints = Props.maxShieldHitPoints;
                }
            }
        }

        public override void PostDraw()
        {
            base.PostDraw();
            if (shieldEnabled && ShouldDisplay)
            {
                float num = Mathf.Lerp(1.2f, 1.55f, shieldHitPoints / Props.maxShieldHitPoints);
                Vector3 drawPos = GetWearer().Drawer.DrawPos;
                drawPos.y = AltitudeLayer.MoteOverhead.AltitudeFor();
                int num2 = Find.TickManager.TicksGame - lastAbsorbDamageTick;
                if (num2 < 8)
                {
                    float num3 = (8 - num2) / 8f * 0.05f;
                    drawPos += impactAngleVect * num3;
                    num -= num3;
                }

                float alpha;
                if (Props.useHitPointsMode)
                {
                    // 生命值模式：透明度根据护盾生命值变化
                    alpha = Mathf.Lerp(0.2f, 0.7f, shieldHitPoints / Props.maxShieldHitPoints);
                }
                else
                {
                    // 偏转模式：固定透明度，稍微闪烁效果
                    alpha = 0.4f + Mathf.Sin(Time.time * 2f) * 0.1f;
                }
                Color color = Props.shieldColor;
                color.a = alpha;

                Matrix4x4 matrix = default(Matrix4x4);
                matrix.SetTRS(drawPos, Quaternion.identity, Vector3.one * num * Props.shieldRadius);
                Graphics.DrawMesh(MeshPool.plane10, matrix, BubbleMat, 0, null, 0, MaterialPropertyBlock);
            }
        }

        private MaterialPropertyBlock materialPropertyBlock;
        private MaterialPropertyBlock MaterialPropertyBlock
        {
            get
            {
                if (materialPropertyBlock == null)
                {
                    materialPropertyBlock = new MaterialPropertyBlock();
                }
                materialPropertyBlock.SetColor(ShaderPropertyIDs.Color, Props.shieldColor);
                return materialPropertyBlock;
            }
        }

        public bool CheckIntercept(Projectile projectile, Vector3 lastExactPos, Vector3 newExactPos)
        {
            if (!shieldEnabled)
                return false;

            // 如果使用生命值模式且护盾已破坏，则不拦截
            if (Props.useHitPointsMode && shieldHitPoints <= 0f)
                return false;

            Pawn wearer = GetWearer();
            if (wearer == null || !wearer.Spawned)
                return false;

            if (!Props.interceptGroundProjectiles && !projectile.def.projectile.flyOverhead)
                return false;

            if (!Props.interceptAirProjectiles && projectile.def.projectile.flyOverhead)
                return false;

            Vector3 center = wearer.TrueCenter();
            float radius = Props.shieldRadius;

            // 简单检查：如果射线起点和终点都在圆外，且连线不穿过圆，则不相交
            float distanceFromLastPos = Vector3.Distance(lastExactPos, center);
            float distanceFromNewPos = Vector3.Distance(newExactPos, center);
            
            if (distanceFromLastPos > radius && distanceFromNewPos > radius)
            {
                // 计算点到线段的最短距离
                Vector3 line = newExactPos - lastExactPos;
                float lineLength = line.magnitude;
                Vector3 lineDirection = line / lineLength;
                float projection = Mathf.Clamp(Vector3.Dot(center - lastExactPos, lineDirection), 0f, lineLength);
                Vector3 closestPoint = lastExactPos + lineDirection * projection;
                float distanceToLine = Vector3.Distance(center, closestPoint);
                
                if (distanceToLine > radius)
                    return false;
            }

            lastKeepDisplayTick = Find.TickManager.TicksGame + 40;
            
            // 根据模式处理伤害
            if (Props.useHitPointsMode)
            {
                // 生命值模式：吸收伤害并可能破坏护盾
                AbsorbDamage(projectile.DamageAmount, projectile.ExactPosition);
            }
            else
            {
                // 偏转模式：只是偏转，不消耗护盾生命值
                DeflectProjectile(projectile.ExactPosition);
            }
            
            return true;
        }

        public bool CheckMeleeIntercept(DamageInfo dinfo, Pawn attacker)
        {
            if (!shieldEnabled || !Props.interceptMeleeAttacks || shieldHitPoints <= 0f)
                return false;

            Pawn wearer = GetWearer();
            if (wearer == null || !wearer.Spawned)
                return false;

            lastKeepDisplayTick = Find.TickManager.TicksGame + 40;
            AbsorbDamage(dinfo.Amount, attacker.Position.ToVector3());
            return true;
        }

        private void AbsorbDamage(float damage, Vector3 impactPos)
        {
            if (Props.empImmune && damage > 0f)
            {
                // EMP免疫时减少EMP伤害
                damage *= 0.1f;
            }

            // 只有在生命值模式下才扣除护盾生命值
            if (Props.useHitPointsMode)
            {
                shieldHitPoints -= damage;
            }
            
            lastAbsorbDamageTick = Find.TickManager.TicksGame;
            
            Pawn wearer = GetWearer();
            if (wearer != null)
            {
                impactAngleVect = Vector3Utility.HorizontalVectorFromAngle((impactPos - wearer.TrueCenter()).AngleFlat() + 180f);
            }

            // 只有在生命值模式下才会破坏护盾
            if (Props.useHitPointsMode && shieldHitPoints <= 0f)
            {
                Break();
            }
        }

        private void DeflectProjectile(Vector3 impactPos)
        {
            // 偏转模式：只显示视觉效果，不消耗护盾生命值
            lastAbsorbDamageTick = Find.TickManager.TicksGame;
            
            Pawn wearer = GetWearer();
            if (wearer != null)
            {
                impactAngleVect = Vector3Utility.HorizontalVectorFromAngle((impactPos - wearer.TrueCenter()).AngleFlat() + 180f);
                
                // 播放偏转特效
                FleckMaker.ThrowLightningGlow(impactPos, wearer.Map, 0.5f);
            }
        }

        private void Break()
        {
            shieldHitPoints = 0f;
            ticksToReset = Props.rechargeCooldownTicks;
            sustainer?.End();
            sustainer = null;

            Pawn wearer = GetWearer();
            if (wearer != null && wearer.Map != null)
            {
                FleckMaker.Static(wearer.TrueCenter(), wearer.Map, FleckDefOf.ExplosionFlash, 12f);
                for (int i = 0; i < 6; i++)
                {
                    FleckMaker.ThrowDustPuff(wearer.TrueCenter() + Vector3Utility.HorizontalVectorFromAngle(Rand.Range(0, 360)) * Rand.Range(0.3f, 0.6f), wearer.Map, Rand.Range(0.8f, 1.2f));
                }
            }
        }

        private void Reset()
        {
            if (parent.Spawned)
            {
                SoundDefOf.EnergyShield_Reset.PlayOneShot(new TargetInfo(parent.Position, parent.Map));
                FleckMaker.ThrowLightningGlow(GetWearer().TrueCenter(), parent.Map, 3f);
                
                if (Props.reactivateEffect != null)
                {
                    Effecter effecter = Props.reactivateEffect.Spawn(parent.Position, parent.Map);
                    effecter.Trigger(new TargetInfo(parent.Position, parent.Map), TargetInfo.Invalid);
                    effecter.Cleanup();
                }
            }
            shieldHitPoints = Props.maxShieldHitPoints;
        }

        public void ToggleShield()
        {
            shieldEnabled = !shieldEnabled;
            
            Pawn wearer = GetWearer();
            if (wearer != null)
            {
                string message = shieldEnabled ? $"{wearer.LabelShort}激活了护盾" : $"{wearer.LabelShort}关闭了护盾";
                Messages.Message(message, MessageTypeDefOf.NeutralEvent, false);
            }

            if (!shieldEnabled)
            {
                sustainer?.End();
                sustainer = null;
            }
        }

        private Pawn GetWearer()
        {
            if (parent is Apparel apparel)
            {
                return apparel.Wearer;
            }
            return null;
        }
        
        // 添加初始化方法，确保护盾值正确设置
        public override void Initialize(CompProperties props)
        {
            base.Initialize(props);
            shieldHitPoints = ((CompProperties_WulaShieldBelt)props).maxShieldHitPoints;
            shieldEnabled = ((CompProperties_WulaShieldBelt)props).startEnabled;
        }

        public override IEnumerable<Gizmo> CompGetWornGizmosExtra()
        {
            // 确保穿戴者存在
            Pawn wearer = GetWearer();
            if (wearer == null) yield break;
            
            // 不限制只有选中时才显示
            yield return new Command_Toggle
            {
                defaultLabel = "护盾开关",
                defaultDesc = shieldEnabled ? "关闭护盾" : "激活护盾",
                icon = ContentFinder<Texture2D>.Get("UI/Commands/DesirePower"),
                isActive = () => shieldEnabled,
                toggleAction = ToggleShield
            };
        }

        public override string CompInspectStringExtra()
        {
            if (shieldEnabled)
            {
                if (Props.useHitPointsMode)
                {
                    return $"护盾: {shieldHitPoints:F0} / {Props.maxShieldHitPoints} (生命值模式)";
                }
                else
                {
                    return "护盾: 激活 (偏转模式)";
                }
            }
            else
            {
                return "护盾: 已关闭";
            }
        }
    }
}