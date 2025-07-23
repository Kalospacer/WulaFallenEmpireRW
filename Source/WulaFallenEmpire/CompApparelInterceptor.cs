using HarmonyLib;
using RimWorld;
using System;
using System.Collections.Generic;
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

        public bool interceptGroundProjectiles = false;
        public bool interceptNonHostileProjectiles = false;
        public bool interceptAirProjectiles = true;

        public SoundDef soundIntercept;
        public SoundDef soundBreak;
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
        private Pawn PawnOwner => (parent as Apparel)?.Wearer;

        public bool Active
        {
            get
            {
                if (PawnOwner == null || !PawnOwner.Spawned) return false;
                if (OnCooldown || Charging || stunner.Stunned || shutDown || currentHitPoints <= 0) return false;
                if (Props.activated && Find.TickManager.TicksGame > activatedTick + Props.activeDuration) return false;
                return true;
            }
        }

        protected bool ShouldDisplay
        {
            get
            {
                if (PawnOwner == null || !PawnOwner.Spawned || PawnOwner.Dead || PawnOwner.Downed || !Active)
                {
                    return false;
                }
                if (PawnOwner.Drafted || PawnOwner.InAggroMentalState || (PawnOwner.Faction != null && PawnOwner.Faction.HostileTo(Faction.OfPlayer) && !PawnOwner.IsPrisoner))
                {
                    return true;
                }
                if (Find.Selector.IsSelected(PawnOwner))
                {
                    return true;
                }
                return false;
            }
        }

        public bool OnCooldown => ticksToReset > 0;
        public bool Charging => startedChargingTick >= 0 && Find.TickManager.TicksGame < startedChargingTick + Props.startupDelay;
        public int CooldownTicksLeft => ticksToReset;
        public int ChargingTicksLeft => (startedChargingTick < 0) ? 0 : Mathf.Max(startedChargingTick + Props.startupDelay - Find.TickManager.TicksGame, 0);
        public int HitPointsMax => Props.hitPoints;
        protected virtual int HitPointsPerInterval => 1;

        public override void PostPostMake()
        {
            base.PostPostMake();
            stunner = new StunHandler(parent);
            if (Props.startupDelay > 0)
            {
                startedChargingTick = Find.TickManager.TicksGame;
                currentHitPoints = 0;
            }
            else
            {
                currentHitPoints = HitPointsMax;
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
            Scribe_Deep.Look(ref stunner, "stunner", parent);

            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                if (stunner == null) stunner = new StunHandler(parent);
                if (currentHitPoints == -1) currentHitPoints = HitPointsMax;
            }
        }

        public bool TryIntercept(Projectile projectile, Vector3 lastExactPos, Vector3 newExactPos)
        {
            if (PawnOwner == null || !PawnOwner.Spawned || !Active)
            {
                return false;
            }

            if (!GenGeo.IntersectLineCircleOutline(PawnOwner.Position.ToVector2(), Props.radius, lastExactPos.ToVector2(), newExactPos.ToVector2()))
            {
                return false;
            }

            if (!InterceptsProjectile(Props, projectile))
            {
                return false;
            }

            bool isHostile = (projectile.Launcher != null && projectile.Launcher.HostileTo(PawnOwner)) || (projectile.Launcher == null && Props.interceptNonHostileProjectiles);
            if (!isHostile)
            {
                return false;
            }

            // --- Interception Success ---
            lastInterceptAngle = projectile.ExactPosition.AngleToFlat(PawnOwner.TrueCenter());
            lastInterceptTicks = Find.TickManager.TicksGame;
            drawInterceptCone = true;
            if (Props.soundIntercept != null) Props.soundIntercept.PlayOneShot(new TargetInfo(PawnOwner.Position, PawnOwner.Map));
            EffecterDefOf.Interceptor_BlockedProjectile.Spawn(PawnOwner.Position, PawnOwner.Map);

            if (projectile.DamageDef == DamageDefOf.EMP && !Props.isImmuneToEMP)
            {
                BreakShieldEmp(new DamageInfo(projectile.DamageDef, projectile.DamageAmount, instigator: projectile.Launcher));
            }
            else if (HitPointsMax > 0)
            {
                currentHitPoints -= projectile.DamageAmount;
                if (currentHitPoints <= 0)
                {
                    BreakShieldHitpoints(new DamageInfo(projectile.DamageDef, projectile.DamageAmount, instigator: projectile.Launcher));
                }
            }
            return true;
        }

        public override void CompTick()
        {
            base.CompTick();
            if (PawnOwner == null || !PawnOwner.Spawned) return;

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
                if (Active && (sustainer == null || sustainer.Ended)) sustainer = Props.activeSound.TrySpawnSustainer(SoundInfo.InMap(parent));
                sustainer?.Maintain();
                if (!Active && sustainer != null && !sustainer.Ended) sustainer.End();
            }
        }

        public void Reset()
        {
            if (PawnOwner.Spawned) Props.reactivateEffect?.Spawn(PawnOwner.Position, PawnOwner.Map).Cleanup();
            currentHitPoints = HitPointsMax;
            ticksToReset = 0;
        }

        private void BreakShieldHitpoints(DamageInfo dinfo)
        {
            if (PawnOwner.Spawned)
            {
                if (Props.soundBreak != null) Props.soundBreak.PlayOneShot(new TargetInfo(PawnOwner.Position, PawnOwner.Map));
                EffecterDefOf.Shield_Break.SpawnAttached(PawnOwner, PawnOwner.MapHeld, Props.radius);
            }
            currentHitPoints = 0;
            ticksToReset = Props.rechargeDelay;
        }

        private void BreakShieldEmp(DamageInfo dinfo)
        {
            BreakShieldHitpoints(dinfo);
            if (Props.disarmedByEmpForTicks > 0) stunner.Notify_DamageApplied(new DamageInfo(DamageDefOf.EMP, (float)Props.disarmedByEmpForTicks / 30f));
        }

        public static bool InterceptsProjectile(CompProperties_ApparelInterceptor props, Projectile projectile)
        {
            if (projectile.def.projectile.flyOverhead) return props.interceptAirProjectiles;
            return props.interceptGroundProjectiles;
        }

        // --- DRAWING LOGIC ---
        public override void CompDrawWornExtras()
        {
            base.CompDrawWornExtras();
            if (PawnOwner == null || !PawnOwner.Spawned || !ShouldDisplay) return;

            Vector3 drawPos = PawnOwner.Drawer.DrawPos;
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

        // --- GIZMO ---
        public override IEnumerable<Gizmo> CompGetWornGizmosExtra()
        {
            if (PawnOwner != null && Find.Selector.SingleSelectedThing == PawnOwner)
            {
                yield return new Gizmo_EnergyShieldStatus { shield = this };
            }
        }

        public override string CompInspectStringExtra()
        {
            StringBuilder sb = new StringBuilder();
            if (OnCooldown)
            {
                sb.Append("Cooldown: " + CooldownTicksLeft.ToStringTicksToPeriod());
            }
            else if (stunner.Stunned)
            {
                sb.Append("EMP Shutdown: " + stunner.StunTicksLeft.ToStringTicksToPeriod());
            }
            return sb.ToString();
        }
    }

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