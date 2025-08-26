# RimWorld Mod: 基于次数的护盾与原版护盾视觉集成

## 1. 引言

本Mod旨在为《RimWorld》引入一种新型的护盾机制：基于 Hediff 层数的次数护盾。与原版基于能量的护盾不同，本护盾的抵挡能力由可叠加的“层数”决定，每层护盾可以抵挡一次受到的伤害。同时，为了提供更沉浸和熟悉的体验，我们集成了原版能量护盾（CompShield）的视觉特效和音效，使次数护盾在抵挡伤害时，能够展现出与原版护盾相似的视觉冲击力。

## 2. 核心概念回顾

### 2.1 Hediff_DamageShield

这是我们自定义的 Hediff 类型，它代表了Pawn身上激活的次数护盾。它的核心特性是：
-   **层数管理**：通过 `ShieldCharges` 属性来跟踪剩余的护盾层数。当Pawn获得护盾时，层数增加；当护盾抵挡伤害时，层数减少。
-   **自动移除**：当护盾层数归零时，该 Hediff 会自动从Pawn身上移除。
-   **显示信息**：在Pawn的健康信息界面，会显示当前护盾的剩余层数。

### 2.2 CompShield

这是《RimWorld》原版用于实现能量护盾的组件。它通常附加在护盾腰带等物品上，提供以下核心功能：
-   **能量值**：护盾具有能量储备，受到伤害会消耗能量。
-   **充能与重置**：能量耗尽后，护盾会进入重置状态，并在一段时间后恢复能量。
-   **视觉和音效**：护盾拥有独特的视觉表现（如护盾泡泡）和音效（如吸收伤害时的音效）。

## 3. 实现细节

### 3.1 伤害抵挡逻辑与护盾渲染 (DRMDamageShield.cs & Hediff_DamageShield.cs)

**核心思想**：我们利用 `ThingComp` 的 `PostPreApplyDamage` 虚方法来拦截伤害，而不是使用 Harmony Patch `Pawn_HealthTracker.PreApplyDamage`。这将使代码更简洁，更符合 RimWorld 的组件化设计。护盾的视觉渲染也将由这个 `ThingComp` 负责。

-   **`DRMDamageShield.cs`**: 这是一个自定义的 `ThingComp`，它将附加到 Pawn 身上。
    -   **伤害拦截**：它重写了 `PostPreApplyDamage` 方法。当 Pawn 受到伤害时，这个方法会被自动调用。在这里，我们会检查 Pawn 是否拥有 `Hediff_DamageShield` 及其层数，如果满足条件，则消耗层数并设置 `absorbed = true` 来抵挡伤害。
    -   **视觉和音效集成**：在抵挡伤害时，`DRMDamageShield` 会触发原版能量护盾的吸收音效、闪光特效和抖动效果。
    -   **护盾渲染**：`DRMDamageShield` 包含了从 `CompShield` 中提取的护盾泡泡渲染逻辑。它会在 Pawn 身上渲染一个动态的护盾泡泡，其大小和显示状态与 `Hediff_DamageShield` 的层数关联。
    -   **能量同步**：`DRMDamageShield` 的“能量”和“最大能量”属性将直接从 Pawn 身上对应的 `Hediff_DamageShield` 实例中获取其 `ShieldCharges` 和 `def.maxSeverity`。

-   **`Hediff_DamageShield.cs`**:
    -   **动态管理 `DRMDamageShield`**：在 `PostAdd` 方法中，当 `Hediff_DamageShield` 被添加到 Pawn 身上时，它会确保 Pawn 拥有一个 `DRMDamageShield` 实例（如果Pawn还没有）。在 `PostRemoved` 方法中，当 `Hediff_DamageShield` 被移除时，它会禁用或移除对应的 `DRMDamageShield` 实例。
    -   **层数与能量关联**：`Hediff_DamageShield` 的 `ShieldCharges` 属性将作为 `DRMDamageShield` 的能量来源。

### 3.2 充能方式 (CompUseEffect_AddDamageShieldCharges.cs & WULA_DamageShieldGenerator)

护盾的充能方式保持不变，通过使用特定的物品来增加护盾层数。

-   **`CompUseEffect_AddDamageShieldCharges`**：这是一个自定义的物品使用效果组件。
    -   当物品被使用时，它会检查目标Pawn是否拥有 `Hediff_DamageShield`。
    -   如果Pawn没有该Hediff，则会为其添加一个，并赋予预设的初始层数（例如10层）。
    -   如果Pawn已有该Hediff，则会在现有层数的基础上增加预设的层数（例如每次使用增加10层）。
-   **`WULA_DamageShieldGenerator`**：这是定义在XML中的一个物品，它附加了 `CompUseEffect_AddDamageShieldCharges` 组件。玩家可以通过制作或获得这个物品，并对其Pawn使用来获取或补充护盾层数。

## 4. 代码结构与内容

以下是本Mod的关键文件及其作用和完整代码内容：

### 4.1 Hediff_DamageShield.cs (更新)

此文件定义了基于层数的护盾 Hediff。它将管理护盾层数，并在 Pawn 身上动态添加/移除 `DRMDamageShield`。

```csharp
using Verse;
using System.Text;
using RimWorld;
using UnityEngine;
using HarmonyLib; // Needed for AccessTools if you use it here directly

namespace WulaFallenEmpire
{
    public class Hediff_DamageShield : HediffWithComps
    {
        // 伤害抵挡层数
        public int ShieldCharges
        {
            get => (int)severityInt;
            set => severityInt = value;
        }

        private DRMDamageShield cachedShieldComp;

        // 获取或创建 DRMDamageShield 组件
        public DRMDamageShield ShieldComp
        {
            get
            {
                if (cachedShieldComp == null || cachedShieldComp.parent != pawn)
                {
                    cachedShieldComp = pawn.GetComp<DRMDamageShield>();
                    if (cachedShieldComp == null)
                    {
                        // 如果没有，动态添加一个
                        cachedShieldComp = (DRMDamageShield)Activator.CreateInstance(typeof(DRMDamageShield));
                        cachedShieldComp.parent = pawn;
                        cachedShieldComp.props = new DRMCompShieldProp(); // 确保有属性，即使是默认的
                        pawn.AllComps.Add(cachedShieldComp);
                        cachedShieldComp.Initialize(cachedShieldComp.props);
                    }
                }
                return cachedShieldComp;
            }
        }


        public override string LabelInBrackets
        {
            get
            {
                if (ShieldCharges > 0)
                {
                    return "层数: " + ShieldCharges;
                }
                return null;
            }
        }

        public override string TipStringExtra
        {
            get
            {
                StringBuilder sb = new StringBuilder();
                sb.Append(base.TipStringExtra);
                if (ShieldCharges > 0)
                {
                    sb.AppendLine("  - 每层抵挡一次伤害。当前层数: " + ShieldCharges);
                }
                else
                {
                    sb.AppendLine("  - 没有可用的抵挡层数。");
                }
                return sb.ToString();
            }
        }

        public override void ExposeData()
        {
            base.ExposeData();
            // severityInt 会自动保存，所以不需要额外处理 ShieldCharges
        }

        public override void PostAdd(DamageInfo? dinfo)
        {
            base.PostAdd(dinfo);
            // 确保 Pawn 拥有 DRMCompShield 组件
            DRMDamageShield comp = ShieldComp; // 访问属性以确保组件被添加
            if (comp != null)
            {
                comp.IsActive = true; // 激活护盾组件
                // 能量同步将在 Tick() 中完成
            }
        }

        public override void PostRemoved()
        {
            base.PostRemoved();
            // 禁用护盾组件
            if (cachedShieldComp != null && cachedShieldComp.parent == pawn)
            {
                cachedShieldComp.IsActive = false;
            }
        }

        public override void Tick()
        {
            base.Tick();
            // 如果层数归零，移除 Hediff
            if (ShieldCharges <= 0)
            {
                pawn.health.RemoveHediff(this);
            }
            // 同步能量到 ShieldComp
            if (ShieldComp != null && ShieldComp.IsActive)
            {
                ShieldComp.Energy = ShieldCharges;
                ShieldComp.MaxEnergy = (int)def.maxSeverity;
            }
        }
    }
}
```

### 4.2 DRMDamageShield.cs (新文件)

此文件定义了自定义的 `ThingComp`，用于处理护盾的渲染和部分行为。它将从 `CompShield` 和 `PlasmaShieldImplant.cs` 中提取渲染和伤害处理逻辑。

```csharp
using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;
using Verse;
using Verse.Sound;
using System.Reflection; // For AccessTools
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
                // 显示抵挡文本
                Verse.MoteMaker.ThrowText(PawnOwner.DrawPos, PawnOwner.Map, "伤害被护盾抵挡!", Color.cyan, 1.2f);

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
            Vector3 loc = PawnOwner.TrueCenter() + impactAngleVect.RotatedBy(180f) * 0.5f;
            float num = Mathf.Min(10f, 2f + dinfo.Amount / 10f);
            FleckMaker.Static(loc, PawnOwner.Map, FleckDefOf.ExplosionFlash, num);
            int num2 = (int)num;
            for (int i = 0; i < num2; i++)
            {
                FleckMaker.ThrowDustPuff(loc, PawnOwner.Map, Rand.Range(0.8f, 1.2f));
            }
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
                FleckMaker.Static(PawnOwner.TrueCenter(), PawnOwner.Map, FleckDefOf.ExplosionFlash, 12f);
                for (int i = 0; i < 6; i++)
                {
                    FleckMaker.ThrowDustPuff(PawnOwner.TrueCenter() + Vector3Utility.HorizontalVectorFromAngle(Rand.Range(0, 360)) * Rand.Range(0.3f, 0.6f), PawnOwner.Map, Rand.Range(0.8f, 1.2f));
                }
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
                FleckMaker.ThrowLightningGlow(PawnOwner.TrueCenter(), PawnOwner.Map, 3f);
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
```

### 4.3 CompUseEffect_AddDamageShieldCharges.cs (不变)

```csharp
using Verse;
using RimWorld;
using System.Collections.Generic;

namespace WulaFallenEmpire
{
    public class CompUseEffect_AddDamageShieldCharges : CompUseEffect
    {
        public CompProperties_AddDamageShieldCharges Props => (CompProperties_AddDamageShieldCharges)props;

        public override void DoEffect(Pawn user)
        {
            base.DoEffect(user);

            // 获取或添加 Hediff_DamageShield
            Hediff_DamageShield damageShield = user.health.hediffSet.GetFirstHediff<Hediff_DamageShield>();

            if (damageShield == null)
            {
                // 如果没有 Hediff，则添加一个
                damageShield = (Hediff_DamageShield)HediffMaker.MakeHediff(Props.hediffDef, user);
                user.health.AddHediff(damageShield);
                damageShield.ShieldCharges = Props.chargesToAdd; // 设置初始层数
            }
            else
            {
                // 如果已有 Hediff，则增加层数
                damageShield.ShieldCharges += Props.chargesToAdd;
            }

            // 确保层数不超过最大值
            if (damageShield.ShieldCharges > (int)damageShield.def.maxSeverity)
            {
                damageShield.ShieldCharges = (int)damageShield.def.maxSeverity;
            }

            // 发送消息
            Messages.Message("WULA_MessageGainedDamageShieldCharges".Translate(user.LabelShort, Props.chargesToAdd), user, MessageTypeDefOf.PositiveEvent);
        }

        // 修正 CanBeUsedBy 方法签名
        public override AcceptanceReport CanBeUsedBy(Pawn p)
        {
            // 确保只能对活着的 Pawn 使用
            if (p.Dead)
            {
                return "WULA_CannotUseOnDeadPawn".Translate();
            }
            
            // 检查是否已达到最大层数
            Hediff_DamageShield damageShield = p.health.hediffSet.GetFirstHediff<Hediff_DamageShield>();
            if (damageShield != null && damageShield.ShieldCharges >= (int)damageShield.def.maxSeverity)
            {
                return "WULA_DamageShieldMaxChargesReached".Translate();
            }

            return true; // 可以使用
        }

        // 可以在这里添加 GetDescriptionPart() 来显示描述
        public override string GetDescriptionPart()
        {
            return "WULA_DamageShieldChargesDescription".Translate(Props.chargesToAdd);
        }
    }

    public class CompProperties_AddDamageShieldCharges : CompProperties_UseEffect
    {
        public HediffDef hediffDef;
        public int chargesToAdd;

        public CompProperties_AddDamageShieldCharges()
        {
            compClass = typeof(CompUseEffect_AddDamageShieldCharges);
        }
    }
}
```

### 4.4 DamageShieldPatch.cs (将删除)

此文件将不再需要，因为伤害拦截逻辑已转移到 `DRMDamageShield.cs`。

```csharp
// 此文件将被删除
```

### 4.5 Hediffs_WULA_DamageShield.xml (不变)

```xml
<?xml version="1.0" encoding="utf-8" ?>
<Defs>
    <HediffDef ParentName="HediffWithCompsBase">
        <defName>WULA_DamageShield</defName>
        <label>伤害护盾</label>
        <description>一种特殊的能量护盾，可以抵挡受到的伤害。每层护盾可以抵挡一次伤害。</description>
        <hediffClass>WulaFallenEmpire.Hediff_DamageShield</hediffClass>
        <initialSeverity>10</initialSeverity> <!-- 初始层数设置为10 -->
        <maxSeverity>999</maxSeverity> <!-- 最大层数，可以根据需要调整 -->
        <tendable>false</tendable>
        <displayAllParts>false</displayAllParts>
        <priceImpact>1</priceImpact>
        <addedSimultaneously>true</addedSimultaneously>
        <countsAsAddedPartOrImplant>false</countsAsAddedPartOrImplant>
        <stages>
            <li>
                <label>活跃</label>
                <minSeverity>1</minSeverity>
                <!-- 这里可以添加一些统计数据偏移，例如增加防御等 -->
            </li>
        </stages>
        <scenarioCanAdd>false</scenarioCanAdd>
    </HediffDef>
</Defs>
```

### 4.6 ThingDefs_WULA_Items_DamageShield.xml (修改)

此文件将定义新的物品 `WULA_DamageShieldGenerator`，它将使用 `CompProperties_AddDamageShieldCharges`。

```xml
<?xml version="1.0" encoding="utf-8" ?>
<Defs>
    <ThingDef ParentName="ResourceBase">
        <defName>WULA_DamageShieldGenerator</defName>
        <label>伤害护盾发生器</label>
        <description>一个便携式设备，可以激活并生成一个临时的能量护盾，抵挡即将到来的伤害。</description>
        <graphicData>
            <texPath>Things/Item/WULA_DamageShieldGenerator</texPath> <!-- 假设有一个贴图 -->
            <graphicClass>Graphic_Single</graphicClass>
        </graphicData>
        <stackLimit>1</stackLimit>
        <useHitPoints>true</useHitPoints>
        <healthAffectsPrice>false</healthAffectsPrice>
        <statBases>
            <MaxHitPoints>50</MaxHitPoints>
            <MarketValue>500</MarketValue>
            <Mass>0.5</Mass>
            <WorkToMake>1000</WorkToMake>
        </statBases>
        <thingCategories>
            <li>Items</li>
        </thingCategories>
        <tradeability>Sellable</tradeability>
        <comps>
            <li Class="CompProperties_Usable">
                <useJob>UseItem</useJob>
                <floatMenuCommandLabel>使用伤害护盾发生器</floatMenuCommandLabel>
            </li>
            <li Class="WulaFallenEmpire.CompProperties_AddDamageShieldCharges">
                <hediffDef>WULA_DamageShield</hediffDef>
                <chargesToAdd>10</chargesToAdd> <!-- 每次使用添加 10 层 -->
            </li>
        </comps>
    </ThingDef>
</Defs>
```

### 4.7 WULA_Keyed.xml (不变)

```xml
<?xml version="1.0" encoding="utf-8" ?>
<LanguageData>
    <WULA_MessageGainedDamageShieldCharges>{0} 获得了 {1} 层伤害护盾！</WULA_MessageGainedDamageShieldCharges>
    <WULA_CannotUseOnDeadPawn>无法对已死亡的Pawn使用。</WULA_CannotUseOnDeadPawn>
    <WULA_DamageShieldMaxChargesReached>伤害护盾已达到最大层数。</WULA_DamageShieldMaxChargesReached>
    <WULA_DamageShieldChargesDescription>使用：增加 {0} 层伤害护盾</WULA_DamageShieldChargesDescription>
</LanguageData>
```

## 5. 安装与测试

### 5.1 安装 Mod

1.  将本Mod的文件夹放置在《RimWorld》的Mods目录下。
2.  在游戏启动器中激活本Mod。

### 5.2 游戏内测试

1.  进入游戏，加载或开始一个殖民地。
2.  打开开发者模式（通常按 `~` 键）。
3.  **生成护盾物品**：在开发者控制台中输入 `spawn WULA_DamageShieldGenerator 1` 来生成一个护盾发生器物品。
4.  **使用护盾物品**：让Pawn拾取并使用 `WULA_DamageShieldGenerator`。观察Pawn是否获得了 `伤害护盾` Hediff，并且层数是否正确显示。
5.  **测试伤害抵挡**：让Pawn受到伤害（例如，让敌人攻击，或使用开发者模式中的“伤害”工具）。观察护盾层数是否减少，伤害是否被抵挡，以及是否触发了护盾吸收的音效和闪光特效。
6.  **测试护盾渲染**：观察Pawn身上是否显示了护盾泡泡。

## 6. 未来展望

-   **护盾渲染动态化**：使护盾泡泡的视觉表现（例如透明度、大小）与剩余层数更紧密地关联，层数越低，护盾视觉效果越弱。
-   **充能动画**：为 `WULA_DamageShieldGenerator` 的使用添加充能动画。
-   **平衡性调整**：根据游戏测试反馈，调整护盾的初始层数、每次充能的层数、以及护盾的最大层数，以达到更好的游戏平衡。
-   **扩展功能**：
    -   添加护盾在特定条件下自动充能的机制。
    -   引入不同类型的次数护盾，具有不同的抵挡特性或额外效果。
    -   护盾被击穿时的特殊效果。