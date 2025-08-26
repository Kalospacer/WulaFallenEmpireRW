using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;
using Verse;
using Verse.Sound;
using HarmonyLib; // For AccessTools

namespace WulaFallenEmpire
{
    // 自定义 CompProperties_Shield 变体
    public class DRMCompShieldProp : CompProperties
    {
        public int startingTicksToReset = 3200;
        public float minDrawSize = 1.2f;
        public float maxDrawSize = 1.55f;
        public float energyLossPerDamage = 0.033f;
        public float energyOnReset = 0.2f;
        public bool blocksRangedWeapons = true;

        public DRMCompShieldProp()
        {
            compClass = typeof(DRMDamageShield);
        }
    }

    [StaticConstructorOnStartup] // 确保在游戏启动时加载
    public class DRMDamageShield : ThingComp
    {
        // 从 Hediff_DamageShield 获取层数作为能量
        public float Energy
        {
            get
            {
                Hediff_DamageShield hediff = PawnOwner?.health?.hediffSet.GetFirstHediff<Hediff_DamageShield>();
                return hediff?.ShieldCharges ?? 0;
            }
            set
            {
                Hediff_DamageShield hediff = PawnOwner?.health?.hediffSet.GetFirstHediff<Hediff_DamageShield>();
                if (hediff != null)
                {
                    hediff.ShieldCharges = (int)value;
                }
            }
        }

        public float MaxEnergy
        {
            get
            {
                Hediff_DamageShield hediff = PawnOwner?.health?.hediffSet.GetFirstHediff<Hediff_DamageShield>();
                return hediff?.def.maxSeverity ?? 0;
            }
            set
            {
                // MaxEnergy 由 HediffDef 控制，这里不需要设置
            }
        }

        public bool IsActive = false; // 控制护盾是否激活，由 Hediff_DamageShield 管理

        // 复制自 CompShield
        protected int ticksToReset = -1;
        protected int lastKeepDisplayTick = -9999;
        private Vector3 impactAngleVect;
        private int lastAbsorbDamageTick = -9999;

        private const float MaxDamagedJitterDist = 0.05f;
        private const int JitterDurationTicks = 8;
        private int KeepDisplayingTicks = 1000;

        // 获取原版 CompShield 的 BubbleMat
        private static readonly Material BubbleMat;

        static DRMDamageShield()
        {
            // 使用 Harmony AccessTools 获取 CompShield 的私有静态字段 BubbleMat
            BubbleMat = (Material)AccessTools.Field(typeof(CompShield), "BubbleMat").GetValue(null);
        }

        public DRMCompShieldProp Props => (DRMCompShieldProp)props;

        public ShieldState ShieldState
        {
            get
            {
                if (PawnOwner == null || !IsActive || Energy <= 0)
                {
                    return ShieldState.Disabled;
                }
                if (ticksToReset <= 0)
                {
                    return ShieldState.Active;
                }
                return ShieldState.Resetting;
            }
        }

        protected bool ShouldDisplay
        {
            get
            {
                Pawn pawnOwner = PawnOwner;
                if (pawnOwner == null || !pawnOwner.Spawned || pawnOwner.Dead || pawnOwner.Downed)
                {
                    return false;
                }
                if (pawnOwner.InAggroMentalState)
                {
                    return true;
                }
                if (pawnOwner.Drafted)
                {
                    return true;
                }
                if (pawnOwner.Faction.HostileTo(Faction.OfPlayer) && !pawnOwner.IsPrisoner)
                {
                    return true;
                }
                if (Find.TickManager.TicksGame < lastKeepDisplayTick + KeepDisplayingTicks)
                {
                    return true;
                }
                return false;
            }
        }

        protected Pawn PawnOwner
        {
            get
            {
                return parent as Pawn;
            }
        }

        public override void PostExposeData()
        {
            base.PostExposeData();
            Scribe_Values.Look(ref ticksToReset, "ticksToReset", -1);
            Scribe_Values.Look(ref lastKeepDisplayTick, "lastKeepDisplayTick", 0);
            Scribe_Values.Look(ref IsActive, "isActive", false);
        }

        public override void CompTick()
        {
            base.CompTick();
            if (PawnOwner == null || !IsActive)
            {
                return;
            }

            if (ShieldState == ShieldState.Resetting)
            {
                ticksToReset--;
                if (ticksToReset <= 0)
                {
                    Reset();
                }
            }
            else if (ShieldState == ShieldState.Active)
            {
                // 护盾能量（层数）通过 Hediff_DamageShield 的 Tick 方法管理，这里不需要额外回复
                // 如果需要自动回复层数，可以在这里实现
            }
        }

        public override void PostPreApplyDamage(ref DamageInfo dinfo, out bool absorbed)
        {
            absorbed = false;
            // 获取 Hediff_DamageShield 实例
            Hediff_DamageShield damageShield = PawnOwner?.health?.hediffSet.GetFirstHediff<Hediff_DamageShield>();

            if (ShieldState != ShieldState.Active || !IsActive || damageShield == null || damageShield.ShieldCharges <= 0)
            {
                return;
            }

            // 如果是 EMP 伤害，且护盾没有 EMP 抗性（这里假设我们的护盾没有），则直接击穿
            // 为了简化，我们假设我们的次数盾没有 EMP 抗性，任何 EMP 伤害都会直接击穿
            if (dinfo.Def == DamageDefOf.EMP)
            {
                Energy = 0; // 能量归零
                Notify_ShieldBreak(); // 触发护盾击穿效果
                absorbed = true;
                return;
            }

            // 如果是远程或爆炸伤害，且护盾阻挡这些类型
            if (Props.blocksRangedWeapons && (dinfo.Def.isRanged || dinfo.Def.isExplosive))
            {
                // 消耗一层护盾
                damageShield.ShieldCharges--;

                // 触发护盾吸收效果
                Notify_DamageAbsorbed(dinfo);
                
                // 护盾抖动效果
                PawnOwner.Drawer.renderer.wiggler.SetToCustomRotation(Rand.Range(-0.05f, 0.05f));
                // 移除文字提示
                // 移除粒子效果

                absorbed = true; // 伤害被吸收
                
                // 如果护盾层数归零，触发护盾击穿效果
                if (damageShield.ShieldCharges <= 0)
                {
                    Notify_ShieldBreak();
                }
            }
        }

        public void Notify_DamageAbsorbed(DamageInfo dinfo)
        {
            // 复制自 CompShield.AbsorbedDamage
            SoundDefOf.EnergyShield_AbsorbDamage.PlayOneShot(new TargetInfo(PawnOwner.Position, PawnOwner.Map));
            impactAngleVect = Vector3Utility.HorizontalVectorFromAngle(dinfo.Angle);
            // 移除 FleckMaker.Static 和 FleckMaker.ThrowDustPuff
            lastAbsorbDamageTick = Find.TickManager.TicksGame;
            KeepDisplaying();
        }

        public void Notify_ShieldBreak()
        {
            // 复制自 CompShield.Break
            if (parent.Spawned)
            {
                float scale = Mathf.Lerp(Props.minDrawSize, Props.maxDrawSize, Energy / MaxEnergy); // 根据当前能量比例调整大小
                EffecterDefOf.Shield_Break.SpawnAttached(parent, parent.MapHeld, scale);
                // 移除 FleckMaker.Static 和 FleckMaker.ThrowDustPuff
            }
            ticksToReset = Props.startingTicksToReset;
            // 护盾层数归零将由 Hediff_DamageShield 负责移除 Hediff
        }

        private void Reset()
        {
            // 复制自 CompShield.Reset
            if (PawnOwner.Spawned)
            {
                SoundDefOf.EnergyShield_Reset.PlayOneShot(new TargetInfo(PawnOwner.Position, PawnOwner.Map));
                // 移除 FleckMaker.ThrowLightningGlow
            }
            ticksToReset = -1;
            // 能量恢复由 Hediff_DamageShield 负责，这里不需要设置 Energy
            // 这里可以添加逻辑，让 Hediff_DamageShield 恢复层数
            Hediff_DamageShield hediff = PawnOwner?.health?.hediffSet.GetFirstHediff<Hediff_DamageShield>();
            if (hediff != null)
            {
                hediff.ShieldCharges = (int)hediff.def.initialSeverity; // 重置时恢复到初始层数
            }
        }

        public void KeepDisplaying()
        {
            lastKeepDisplayTick = Find.TickManager.TicksGame;
        }

        public override void PostDraw()
        {
            base.PostDraw();
            Draw();
        }

        private void Draw()
        {
            if (ShieldState == ShieldState.Active && ShouldDisplay)
            {
                float num = Mathf.Lerp(Props.minDrawSize, Props.maxDrawSize, Energy / MaxEnergy); // 根据当前能量比例调整大小
                Vector3 drawPos = PawnOwner.Drawer.DrawPos;
                drawPos.y = AltitudeLayer.MoteOverhead.AltitudeFor();
                int num2 = Find.TickManager.TicksGame - lastAbsorbDamageTick;
                if (num2 < JitterDurationTicks) // 使用 JitterDurationTicks
                {
                    float num3 = (float)(JitterDurationTicks - num2) / JitterDurationTicks * MaxDamagedJitterDist; // 使用 MaxDamagedJitterDist
                    drawPos += impactAngleVect * num3;
                    num -= num3;
                }
                float angle = Rand.Range(0, 360);
                Vector3 s = new Vector3(num, 1f, num);
                Matrix4x4 matrix = default(Matrix4x4);
                matrix.SetTRS(drawPos, Quaternion.AngleAxis(angle, Vector3.up), s);
                Graphics.DrawMesh(MeshPool.plane10, matrix, BubbleMat, 0);
            }
        }
    }
}