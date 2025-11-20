using System;
using System.Collections.Generic;
using UnityEngine;
using Verse;
using Verse.AI;
using RimWorld;

namespace WulaFallenEmpire
{
    public class HediffCompProperties_TopTurret : HediffCompProperties
    {
        public HediffCompProperties_TopTurret()
        {
            this.compClass = typeof(HediffComp_TopTurret);
        }

        public ThingDef turretDef;
        public float angleOffset;
        public bool autoAttack = true;
        public bool defaultEnabled = true; // 新增：默认启用状态
    }

    [StaticConstructorOnStartup]
    public class HediffComp_TopTurret : HediffComp, IAttackTargetSearcher
    {
        public Thing Thing
        {
            get
            {
                return this.Pawn;
            }
        }

        private HediffCompProperties_TopTurret Props
        {
            get
            {
                return (HediffCompProperties_TopTurret)this.props;
            }
        }

        public Verb CurrentEffectiveVerb
        {
            get
            {
                return this.AttackVerb;
            }
        }

        public LocalTargetInfo LastAttackedTarget
        {
            get
            {
                return this.lastAttackedTarget;
            }
        }

        public int LastAttackTargetTick
        {
            get
            {
                return this.lastAttackTargetTick;
            }
        }

        public CompEquippable GunCompEq
        {
            get
            {
                return this.gun.TryGetComp<CompEquippable>();
            }
        }

        public Verb AttackVerb
        {
            get
            {
                return this.GunCompEq.PrimaryVerb;
            }
        }

        private bool WarmingUp
        {
            get
            {
                return this.burstWarmupTicksLeft > 0;
            }
        }


        public override void CompPostTick(ref float severityAdjustment)
        {
            base.CompPostTick(ref severityAdjustment);
            if (!TurretEnabled)
            {
                ResetCurrentTarget();
                return;
            }
            if (!this.CanShoot)
            {
                return;
            }
            // 优先处理手动目标
            if (HasManualTarget && CanAttackManualTarget)
            {
                LocalTargetInfo manualTarget = VolleyTargetManager.GetVolleyTarget(Pawn);
                this.currentTarget = manualTarget;
                this.curRotation = (manualTarget.Cell.ToVector3Shifted() - this.Pawn.DrawPos).AngleFlat() + this.Props.angleOffset;
            }
            else if (this.currentTarget.IsValid)
            {
                this.curRotation = (this.currentTarget.Cell.ToVector3Shifted() - this.Pawn.DrawPos).AngleFlat() + this.Props.angleOffset;
            }
            this.AttackVerb.VerbTick();
            if (this.AttackVerb.state != VerbState.Bursting)
            {
                if (this.WarmingUp)
                {
                    this.burstWarmupTicksLeft--;
                    if (this.burstWarmupTicksLeft == 0)
                    {
                        bool attackSuccess = this.AttackVerb.TryStartCastOn(this.currentTarget, false, true, false, true);
                        if (attackSuccess)
                        {
                            this.lastAttackTargetTick = Find.TickManager.TicksGame;
                            this.lastAttackedTarget = this.currentTarget;
                        }
                        else
                        {
                            // 如果手动攻击失败且目标无效，清除手动目标
                            if (HasManualTarget && !CanAttackManualTarget)
                            {
                                VolleyTargetManager.ClearVolleyTarget(Pawn);
                            }
                        }
                        return;
                    }
                }
                else
                {
                    if (this.burstCooldownTicksLeft > 0)
                    {
                        this.burstCooldownTicksLeft--;
                    }
                    if (this.burstCooldownTicksLeft <= 0 && this.Pawn.IsHashIntervalTick(10))
                    {
                        // 如果手动目标无效，清除它
                        if (HasManualTarget && !CanAttackManualTarget)
                        {
                            VolleyTargetManager.ClearVolleyTarget(Pawn);
                        }
                        // 只有在没有有效的手动目标时才寻找新目标
                        if (!HasManualTarget || !CanAttackManualTarget)
                        {
                            this.currentTarget = (Thing)AttackTargetFinder.BestShootTargetFromCurrentPosition(this, TargetScanFlags.NeedThreat | TargetScanFlags.NeedAutoTargetable, null, 0f, 9999f);
                            if (this.currentTarget.IsValid)
                            {
                                this.burstWarmupTicksLeft = 1;
                                return;
                            }
                        }
                        this.ResetCurrentTarget();
                    }
                }
            }
        }
        // 检查是否有手动目标
        private bool HasManualTarget
        {
            get
            {
                LocalTargetInfo manualTarget = VolleyTargetManager.GetVolleyTarget(Pawn);
                return manualTarget.IsValid;
            }
        }
        // 检查是否可以攻击手动目标
        private bool CanAttackManualTarget
        {
            get
            {
                LocalTargetInfo manualTarget = VolleyTargetManager.GetVolleyTarget(Pawn);
                if (!manualTarget.IsValid)
                    return false;
                // 检查目标是否在射程内
                float distance = Pawn.Position.DistanceTo(manualTarget.Cell);
                if (distance > AttackVerb.verbProps.range)
                    return false;
                // 检查是否可以命中目标
                if (!AttackVerb.CanHitTarget(manualTarget))
                    return false;
                // 检查目标是否还活着（如果是生物）
                if (manualTarget.Thing is Pawn targetPawn && (targetPawn.Dead || targetPawn.Downed))
                    return false;
                // 检查目标是否被摧毁（如果是建筑）
                if (manualTarget.Thing != null && manualTarget.Thing.Destroyed)
                    return false;
                return true;
            }
        }
        // 简化的Gizmos - 只有设置目标和清除目标按钮
        public override IEnumerable<Gizmo> CompGetGizmos()
        {
            // 只有 pawn 被选中且是玩家派系时才显示按钮
            if (this.Pawn.Faction == Faction.OfPlayer && Find.Selector.IsSelected(this.Pawn))
            {
                // 原有开关按钮
                yield return new Command_Toggle
                {
                    defaultLabel = "CommandToggleTurret".Translate(),
                    defaultDesc = "CommandToggleTurretDesc".Translate(),
                    icon = ContentFinder<Texture2D>.Get("UI/Gizmos/ToggleTurret"),
                    isActive = () => TurretEnabled,
                    toggleAction = () => TurretEnabled = !TurretEnabled,
                    hotKey = KeyBindingDefOf.Misc1
                };
                // 设置目标按钮
                yield return new Command_Action
                {
                    defaultLabel = "CommandSetTarget".Translate(),
                    defaultDesc = "CommandSetTargetDesc".Translate(),
                    icon = ContentFinder<Texture2D>.Get("UI/Gizmos/SetTarget"),
                    action = () =>
                    {
                        Find.Targeter.BeginTargeting(
                            CreateTargetingParameters(),
                            delegate (LocalTargetInfo target)
                            {
                                VolleyTargetManager.SetVolleyTarget(Pawn, target);
                            },
                            Pawn, // caster 参数
                            null, // actionWhenFinished
                            null, // mouseAttachment
                            true  // requiresCastedSelected
                        );
                    },
                    hotKey = KeyBindingDefOf.Misc2
                };
                // 清除目标按钮（只在有手动目标时显示）
                LocalTargetInfo currentTarget = VolleyTargetManager.GetVolleyTarget(Pawn);
                if (currentTarget.IsValid)
                {
                    yield return new Command_Action
                    {
                        defaultLabel = "CommandClearTarget".Translate(),
                        defaultDesc = "CommandClearTargetDesc".Translate(),
                        icon = ContentFinder<Texture2D>.Get("UI/Gizmos/ClearTarget"),
                        action = () => VolleyTargetManager.ClearVolleyTarget(Pawn),
                        hotKey = KeyBindingDefOf.Misc3
                    };
                }
            }
        }
        // 创建目标参数
        private TargetingParameters CreateTargetingParameters()
        {
            return TargetingParameters.ForThing();
        }
        // 在提示中显示目标状态
        public override string CompTipStringExtra
        {
            get
            {
                string baseString = base.CompTipStringExtra;
                string turretStatus = TurretEnabled ? "Turret: Active" : "Turret: Inactive";
                string targetStatus = "Manual Target: ";
                LocalTargetInfo manualTarget = VolleyTargetManager.GetVolleyTarget(Pawn);
                if (manualTarget.IsValid)
                {
                    targetStatus += $"{manualTarget.Thing?.LabelCap ?? manualTarget.Cell.ToString()}";
                    if (!CanAttackManualTarget)
                    {
                        targetStatus += " (Unreachable)";
                    }
                }
                else
                {
                    targetStatus += "None";
                }
                string result = turretStatus + "\n" + targetStatus;
                return string.IsNullOrEmpty(baseString) ? result : baseString + "\n" + result;
            }
        }

        // 新增：炮塔启用状态
        public bool TurretEnabled
        {
            get { return turretEnabled; }
            set
            {
                turretEnabled = value;
                if (!turretEnabled)
                {
                    ResetCurrentTarget(); // 禁用时重置目标
                }
            }
        }

        private bool CanShoot
        {
            get
            {
                // 新增：检查炮塔是否启用
                if (!TurretEnabled)
                    return false;

                Pawn pawn;
                if ((pawn = (this.Pawn)) != null)
                {
                    if (!pawn.Spawned || pawn.Downed || pawn.Dead || !pawn.Awake())
                    {
                        return false;
                    }
                    if (pawn.stances.stunner.Stunned)
                    {
                        return false;
                    }
                    if (this.TurretDestroyed)
                    {
                        return false;
                    }
                    if (pawn.IsColonyMechPlayerControlled && !this.fireAtWill)
                    {
                        return false;
                    }
                }
                CompCanBeDormant compCanBeDormant = this.Pawn.TryGetComp<CompCanBeDormant>();
                return compCanBeDormant == null || compCanBeDormant.Awake;
            }
        }

        public bool TurretDestroyed
        {
            get
            {
                Pawn pawn;
                return (pawn = (this.Pawn)) != null && this.AttackVerb.verbProps.linkedBodyPartsGroup != null && this.AttackVerb.verbProps.ensureLinkedBodyPartsGroupAlwaysUsable && PawnCapacityUtility.CalculateNaturalPartsAverageEfficiency(pawn.health.hediffSet, this.AttackVerb.verbProps.linkedBodyPartsGroup) <= 0f;
            }
        }

        private Material TurretMat
        {
            get
            {
                if (this.turretMat == null)
                {
                    this.turretMat = MaterialPool.MatFrom(this.Props.turretDef.graphicData.texPath);
                }
                return this.turretMat;
            }
        }

        public bool AutoAttack
        {
            get
            {
                return this.Props.autoAttack;
            }
        }

        public override void CompPostMake()
        {
            base.CompPostMake();
            this.MakeGun();
            // 新增：设置默认启用状态
            TurretEnabled = Props.defaultEnabled;
        }

        private void MakeGun()
        {
            this.gun = ThingMaker.MakeThing(this.Props.turretDef, null);
            this.UpdateGunVerbs();
        }

        private void UpdateGunVerbs()
        {
            List<Verb> allVerbs = this.gun.TryGetComp<CompEquippable>().AllVerbs;
            for (int i = 0; i < allVerbs.Count; i++)
            {
                Verb verb = allVerbs[i];
                verb.caster = this.Pawn;
                verb.castCompleteCallback = delegate ()
                {
                    this.burstCooldownTicksLeft = this.AttackVerb.verbProps.defaultCooldownTime.SecondsToTicks();
                };
            }
        }
        private void ResetCurrentTarget()
        {
            this.currentTarget = LocalTargetInfo.Invalid;
            this.burstWarmupTicksLeft = 0;
        }

        public override void CompExposeData()
        {
            base.CompExposeData();
            Scribe_Values.Look<int>(ref this.burstCooldownTicksLeft, "burstCooldownTicksLeft", 0, false);
            Scribe_Values.Look<int>(ref this.burstWarmupTicksLeft, "burstWarmupTicksLeft", 0, false);
            Scribe_TargetInfo.Look(ref this.currentTarget, "currentTarget");
            Scribe_Deep.Look<Thing>(ref this.gun, "gun", Array.Empty<object>());
            Scribe_Values.Look<bool>(ref this.fireAtWill, "fireAtWill", true, false);
            // 新增：保存启用状态
            Scribe_Values.Look<bool>(ref this.turretEnabled, "turretEnabled", Props.defaultEnabled, false);

            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                if (this.gun == null)
                {
                    Log.Error("CompTurrentGun had null gun after loading. Recreating.");
                    this.MakeGun();
                    return;
                }
                this.UpdateGunVerbs();
            }
        }

        private const int StartShootIntervalTicks = 10;

        private static readonly CachedTexture ToggleTurretIcon = new CachedTexture("UI/Gizmos/ToggleTurret");

        public Thing gun;
        protected int burstCooldownTicksLeft;
        protected int burstWarmupTicksLeft;
        protected LocalTargetInfo currentTarget = LocalTargetInfo.Invalid;
        private bool fireAtWill = true;
        private LocalTargetInfo lastAttackedTarget = LocalTargetInfo.Invalid;
        private int lastAttackTargetTick;
        private float curRotation;

        // 新增：炮塔启用状态字段
        private bool turretEnabled = true;

        [Unsaved(false)]
        public Material turretMat;
    }
}