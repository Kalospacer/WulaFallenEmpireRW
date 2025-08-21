using System.Collections.Generic;
using System.Linq;
using System.Text;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;
using Verse.AI;
using Verse.Sound;

namespace WulaFallenEmpire
{
    [StaticConstructorOnStartup]
    public class Building_ArmedShuttle : Building_PassengerShuttle, IAttackTargetSearcher
    {
        // --- TurretTop nested class ---
        public class TurretTop
        {
            private Building_ArmedShuttle parentTurret;
            private float curRotationInt;
            private int ticksUntilIdleTurn;
            private int idleTurnTicksLeft;
            private bool idleTurnClockwise;

            private const float IdleTurnDegreesPerTick = 0.26f;
            private const int IdleTurnDuration = 140;
            private const int IdleTurnIntervalMin = 150;
            private const int IdleTurnIntervalMax = 350;
            public static readonly int ArtworkRotation = -90;

            public float CurRotation
            {
                get => curRotationInt;
                set
                {
                    curRotationInt = value % 360f;
                    if (curRotationInt < 0f) curRotationInt += 360f;
                }
            }

            public TurretTop(Building_ArmedShuttle ParentTurret)
            {
                this.parentTurret = ParentTurret;
            }

            public void SetRotationFromOrientation() => CurRotation = parentTurret.Rotation.AsAngle;

            public void ForceFaceTarget(LocalTargetInfo targ)
            {
                if (targ.IsValid)
                {
                    CurRotation = (targ.Cell.ToVector3Shifted() - parentTurret.DrawPos).AngleFlat();
                }
            }

            public void TurretTopTick()
            {
                LocalTargetInfo currentTarget = parentTurret.CurrentTarget;
                if (currentTarget.IsValid)
                {
                    CurRotation = (currentTarget.Cell.ToVector3Shifted() - parentTurret.DrawPos).AngleFlat();
                    ticksUntilIdleTurn = Rand.RangeInclusive(150, 350);
                }
                else if (ticksUntilIdleTurn > 0)
                {
                    ticksUntilIdleTurn--;
                    if (ticksUntilIdleTurn == 0)
                    {
                        idleTurnClockwise = Rand.Value < 0.5f;
                        idleTurnTicksLeft = 140;
                    }
                }
                else
                {
                    CurRotation += idleTurnClockwise ? 0.26f : -0.26f;
                    idleTurnTicksLeft--;
                    if (idleTurnTicksLeft <= 0)
                    {
                        ticksUntilIdleTurn = Rand.RangeInclusive(150, 350);
                    }
                }
            }
            
            public void DrawTurret()
            {
                Vector3 v = new Vector3(parentTurret.def.building.turretTopOffset.x, 0f, parentTurret.def.building.turretTopOffset.y).RotatedBy(CurRotation);
		        float turretTopDrawSize = parentTurret.def.building.turretTopDrawSize;
		        float num = parentTurret.AttackVerb?.AimAngleOverride ?? CurRotation;
		        Vector3 pos = parentTurret.DrawPos + Altitudes.AltIncVect + v;
		        Quaternion q = ((float)ArtworkRotation + num).ToQuat();
		        Graphics.DrawMesh(matrix: Matrix4x4.TRS(pos, q, new Vector3(turretTopDrawSize, 1f, turretTopDrawSize)), mesh: MeshPool.plane10, material: parentTurret.TurretTopMaterial, layer: 0);
            }
        }
        
        // --- Fields ---
        protected LocalTargetInfo forcedTarget = LocalTargetInfo.Invalid;
        private LocalTargetInfo lastAttackedTarget;
        private int lastAttackTargetTick;
        private StunHandler stunner;
        private bool triedGettingStunner;
        protected int burstCooldownTicksLeft;
        protected int burstWarmupTicksLeft;
        protected LocalTargetInfo currentTargetInt = LocalTargetInfo.Invalid;
        private bool holdFire;
        private bool burstActivated;
        public Thing gun;
        protected TurretTop top;
        protected CompPowerTrader powerComp;
        protected CompCanBeDormant dormantComp;
        protected CompInitiatable initiatableComp;
        protected CompMannable mannableComp;
        protected CompInteractable interactableComp;
        public CompRefuelable refuelableComp;
        protected Effecter progressBarEffecter;
        protected CompMechPowerCell powerCellComp;
        protected CompHackable hackableComp;

        // --- PROPERTIES ---
        public virtual Material TurretTopMaterial => def.building.turretTopMat;
        protected bool IsStunned
        {
            get
            {
                if (!triedGettingStunner)
                {
                    stunner = GetComp<CompStunnable>()?.StunHandler;
                    triedGettingStunner = true;
                }
                return stunner != null && stunner.Stunned;
            }
        }
        public LocalTargetInfo TargetCurrentlyAimingAt => CurrentTarget;
        public Verb CurrentEffectiveVerb => AttackVerb;
        public LocalTargetInfo LastAttackedTarget => lastAttackedTarget;
        public int LastAttackTargetTick => lastAttackTargetTick;
        public LocalTargetInfo ForcedTarget => forcedTarget;
        public virtual bool IsEverThreat => true;
        public bool Active => (powerComp == null || powerComp.PowerOn) && (dormantComp == null || dormantComp.Awake) && (initiatableComp == null || initiatableComp.Initiated) && (interactableComp == null || burstActivated) && (powerCellComp == null || !powerCellComp.depleted) && (hackableComp == null || !hackableComp.IsHacked);
        public CompEquippable GunCompEq => gun.TryGetComp<CompEquippable>();
        public virtual LocalTargetInfo CurrentTarget => currentTargetInt;
        private bool WarmingUp => burstWarmupTicksLeft > 0;
        public virtual Verb AttackVerb => GunCompEq.PrimaryVerb;
        public bool IsMannable => mannableComp != null;
        private bool PlayerControlled => (base.Faction == Faction.OfPlayer || MannedByColonist) && !MannedByNonColonist && !IsActivable;
        protected virtual bool CanSetForcedTarget => mannableComp != null && PlayerControlled;
        private bool CanToggleHoldFire => PlayerControlled;
        private bool IsMortar => def.building.IsMortar;
        private bool IsMortarOrProjectileFliesOverhead => AttackVerb.ProjectileFliesOverhead() || IsMortar;
        private bool IsActivable => interactableComp != null;
        protected virtual bool HideForceTargetGizmo => false;
        public TurretTop Top => top;
        private bool CanExtractShell => PlayerControlled && (gun.TryGetComp<CompChangeableProjectile>()?.Loaded ?? false);
        private bool MannedByColonist => mannableComp != null && mannableComp.ManningPawn != null && mannableComp.ManningPawn.Faction == Faction.OfPlayer;
        private bool MannedByNonColonist => mannableComp != null && mannableComp.ManningPawn != null && mannableComp.ManningPawn.Faction != Faction.OfPlayer;
        Thing IAttackTargetSearcher.Thing => this;

        // --- CONSTRUCTOR ---
        public Building_ArmedShuttle()
        {
            top = new TurretTop(this);
        }

        // --- METHODS ---
        public override void SpawnSetup(Map map, bool respawningAfterLoad)
        {
            base.SpawnSetup(map, respawningAfterLoad);
            dormantComp = GetComp<CompCanBeDormant>();
            initiatableComp = GetComp<CompInitiatable>();
            powerComp = GetComp<CompPowerTrader>();
            mannableComp = GetComp<CompMannable>();
            interactableComp = GetComp<CompInteractable>();
            refuelableComp = GetComp<CompRefuelable>();
            powerCellComp = GetComp<CompMechPowerCell>();
            hackableComp = GetComp<CompHackable>();
            if (!respawningAfterLoad)
            {
                top.SetRotationFromOrientation();
                // ShuttleComp.shipParent.Start(); // Already handled by base.SpawnSetup
            }
        }

        public override void PostMake()
        {
            base.PostMake();
            burstCooldownTicksLeft = def.building.turretInitialCooldownTime.SecondsToTicks();
            MakeGun();
        }

        public override void DeSpawn(DestroyMode mode = DestroyMode.Vanish)
        {
            base.DeSpawn(mode);
            ResetCurrentTarget();
            progressBarEffecter?.Cleanup();
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_TargetInfo.Look(ref forcedTarget, "forcedTarget");
            Scribe_TargetInfo.Look(ref lastAttackedTarget, "lastAttackedTarget");
            Scribe_Values.Look(ref lastAttackTargetTick, "lastAttackTargetTick", 0);
            Scribe_Values.Look(ref burstCooldownTicksLeft, "burstCooldownTicksLeft", 0);
            Scribe_Values.Look(ref burstWarmupTicksLeft, "burstWarmupTicksLeft", 0);
            Scribe_TargetInfo.Look(ref currentTargetInt, "currentTarget");
            Scribe_Values.Look(ref holdFire, "holdFire", defaultValue: false);
            Scribe_Values.Look(ref burstActivated, "burstActivated", defaultValue: false);
            Scribe_Deep.Look(ref gun, "gun");
            // Scribe_Values.Look(ref shuttleName, "shuttleName"); // Already handled by base.ExposeData
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                if (gun == null)
                {
                    Log.Error("Turret had null gun after loading. Recreating.");
                    MakeGun();
                }
                else
                {
                    UpdateGunVerbs();
                }
            }
        }

        protected override void Tick()
        {
            base.Tick();
            if (forcedTarget.HasThing && (!forcedTarget.Thing.Spawned || !base.Spawned || forcedTarget.Thing.Map != base.Map))
            {
                forcedTarget = LocalTargetInfo.Invalid;
            }
            if (CanExtractShell && MannedByColonist)
            {
                CompChangeableProjectile compChangeableProjectile = gun.TryGetComp<CompChangeableProjectile>();
                if (!compChangeableProjectile.allowedShellsSettings.AllowedToAccept(compChangeableProjectile.LoadedShell))
                {
                    ExtractShell();
                }
            }
            if (forcedTarget.IsValid && !CanSetForcedTarget) ResetForcedTarget();
            if (!CanToggleHoldFire) holdFire = false;
            if (forcedTarget.ThingDestroyed) ResetForcedTarget();
            
            if (Active && (mannableComp == null || mannableComp.MannedNow) && !IsStunned && base.Spawned)
            {
                GunCompEq.verbTracker.VerbsTick();
                if (AttackVerb.state != VerbState.Bursting)
                {
                    burstActivated = false;
                    if (WarmingUp)
                    {
                        burstWarmupTicksLeft--;
                        if (burstWarmupTicksLeft <= 0) BeginBurst();
                    }
                    else
                    {
                        if (burstCooldownTicksLeft > 0)
                        {
                            burstCooldownTicksLeft--;
                            if (IsMortar)
                            {
                                if (progressBarEffecter == null) progressBarEffecter = EffecterDefOf.ProgressBar.Spawn();
                                progressBarEffecter.EffectTick(this, TargetInfo.Invalid);
                                MoteProgressBar mote = ((SubEffecter_ProgressBar)progressBarEffecter.children[0]).mote;
                                mote.progress = 1f - (float)Mathf.Max(burstCooldownTicksLeft, 0) / (float)BurstCooldownTime().SecondsToTicks();
                                mote.offsetZ = -0.8f;
                            }
                        }
                        if (burstCooldownTicksLeft <= 0 && this.IsHashIntervalTick(15))
                        {
                            TryStartShootSomething(canBeginBurstImmediately: true);
                        }
                    }
                }
                top.TurretTopTick();
            }
            else
            {
                ResetCurrentTarget();
            }
        }

        public override IEnumerable<Gizmo> GetGizmos()
        {
            foreach (Gizmo gizmo in base.GetGizmos()) yield return gizmo;
            if (CanExtractShell)
            {
                CompChangeableProjectile compChangeableProjectile = gun.TryGetComp<CompChangeableProjectile>();
                Command_Action command_Action = new Command_Action();
                command_Action.defaultLabel = "CommandExtractShell".Translate();
                command_Action.defaultDesc = "CommandExtractShellDesc".Translate();
                command_Action.icon = compChangeableProjectile.LoadedShell.uiIcon;
                command_Action.iconAngle = compChangeableProjectile.LoadedShell.uiIconAngle;
                command_Action.iconOffset = compChangeableProjectile.LoadedShell.uiIconOffset;
                command_Action.iconDrawScale = GenUI.IconDrawScale(compChangeableProjectile.LoadedShell);
                command_Action.action = delegate { ExtractShell(); };
                yield return command_Action;
            }
            CompChangeableProjectile compChangeableProjectile2 = gun.TryGetComp<CompChangeableProjectile>();
            if (compChangeableProjectile2 != null)
            {
                foreach (Gizmo item in StorageSettingsClipboard.CopyPasteGizmosFor(compChangeableProjectile2.GetStoreSettings()))
                {
                    yield return item;
                }
            }
            if (!HideForceTargetGizmo)
            {
                if (CanSetForcedTarget)
                {
                    Command_VerbTarget command_VerbTarget = new Command_VerbTarget();
                    command_VerbTarget.defaultLabel = "CommandSetForceAttackTarget".Translate();
                    command_VerbTarget.defaultDesc = "CommandSetForceAttackTargetDesc".Translate();
                    command_VerbTarget.icon = ContentFinder<Texture2D>.Get("UI/Commands/Attack");
                    command_VerbTarget.verb = AttackVerb;
                    command_VerbTarget.hotKey = KeyBindingDefOf.Misc4;
                    command_VerbTarget.drawRadius = false;
                    command_VerbTarget.requiresAvailableVerb = false;
                    if (base.Spawned && IsMortarOrProjectileFliesOverhead && base.Position.Roofed(base.Map))
                    {
                        command_VerbTarget.Disable("CannotFire".Translate() + ": " + "Roofed".Translate().CapitalizeFirst());
                    }
                    yield return command_VerbTarget;
                }
                if (forcedTarget.IsValid)
                {
                    Command_Action command_Action2 = new Command_Action();
                    command_Action2.defaultLabel = "CommandStopForceAttack".Translate();
                    command_Action2.defaultDesc = "CommandStopForceAttackDesc".Translate();
                    command_Action2.icon = ContentFinder<Texture2D>.Get("UI/Commands/Halt");
                    command_Action2.action = delegate
                    {
                        ResetForcedTarget();
                        SoundDefOf.Tick_Low.PlayOneShotOnCamera();
                    };
                    if (!forcedTarget.IsValid)
                    {
                        command_Action2.Disable("CommandStopAttackFailNotForceAttacking".Translate());
                    }
                    command_Action2.hotKey = KeyBindingDefOf.Misc5;
                    yield return command_Action2;
                }
            }
            if (CanToggleHoldFire)
            {
                Command_Toggle command_Toggle = new Command_Toggle();
                command_Toggle.defaultLabel = "CommandHoldFire".Translate();
                command_Toggle.defaultDesc = "CommandHoldFireDesc".Translate();
                command_Toggle.icon = ContentFinder<Texture2D>.Get("UI/Commands/HoldFire");
                command_Toggle.hotKey = KeyBindingDefOf.Misc6;
                command_Toggle.toggleAction = delegate
                {
                    holdFire = !holdFire;
                    if (holdFire) ResetForcedTarget();
                };
                command_Toggle.isActive = () => holdFire;
                yield return command_Toggle;
            }
            Log.Message($"[WULA] Stage 2: Launch Sequence - Providing launch gizmos for {this.Label}.");
            // The following gizmos are already provided by Building_PassengerShuttle's GetGizmos()
            // foreach (Gizmo gizmo in ShuttleComp.CompGetGizmosExtra()) yield return gizmo;
            // foreach (Gizmo gizmo in LaunchableComp.CompGetGizmosExtra()) yield return gizmo;
            // foreach (Gizmo gizmo in TransporterComp.CompGetGizmosExtra()) yield return gizmo;
            // fuel related gizmos are also handled by base class.
        }

        public void OrderAttack(LocalTargetInfo targ)
        {
            if (!targ.IsValid)
            {
                if (forcedTarget.IsValid) ResetForcedTarget();
                return;
            }
            if ((targ.Cell - base.Position).LengthHorizontal < AttackVerb.verbProps.EffectiveMinRange(targ, this))
            {
                Messages.Message("MessageTargetBelowMinimumRange".Translate(), this, MessageTypeDefOf.RejectInput, historical: false);
                return;
            }
            if ((targ.Cell - base.Position).LengthHorizontal > AttackVerb.EffectiveRange)
            {
                Messages.Message("MessageTargetBeyondMaximumRange".Translate(), this, MessageTypeDefOf.RejectInput, historical: false);
                return;
            }
            if (forcedTarget != targ)
            {
                forcedTarget = targ;
                if (burstCooldownTicksLeft <= 0) TryStartShootSomething(canBeginBurstImmediately: false);
            }
            if (holdFire)
            {
                Messages.Message("MessageTurretWontFireBecauseHoldFire".Translate(def.label), this, MessageTypeDefOf.RejectInput, historical: false);
            }
        }
        
        public bool ThreatDisabled(IAttackTargetSearcher disabledFor)
        {
            if (!IsEverThreat) return true;
            if (powerComp != null && !powerComp.PowerOn) return true;
            if (mannableComp != null && !mannableComp.MannedNow) return true;
            if (dormantComp != null && !dormantComp.Awake) return true;
            if (initiatableComp != null && !initiatableComp.Initiated) return true;
            if (powerCellComp != null && powerCellComp.depleted) return true;
            if (hackableComp != null && hackableComp.IsHacked) return true;
            return false;
        }

        protected void OnAttackedTarget(LocalTargetInfo target)
        {
            lastAttackTargetTick = Find.TickManager.TicksGame;
            lastAttackedTarget = target;
        }

        public void TryStartShootSomething(bool canBeginBurstImmediately)
        {
            if (progressBarEffecter != null)
            {
                progressBarEffecter.Cleanup();
                progressBarEffecter = null;
            }
            if (!base.Spawned || (holdFire && CanToggleHoldFire) || (AttackVerb.ProjectileFliesOverhead() && base.Map.roofGrid.Roofed(base.Position)) || !AttackVerb.Available())
            {
                ResetCurrentTarget();
                return;
            }
            bool wasValid = currentTargetInt.IsValid;
            currentTargetInt = forcedTarget.IsValid ? forcedTarget : TryFindNewTarget();
            if (!wasValid && currentTargetInt.IsValid && def.building.playTargetAcquiredSound)
            {
                SoundDefOf.TurretAcquireTarget.PlayOneShot(new TargetInfo(base.Position, base.Map));
            }
            if (currentTargetInt.IsValid)
            {
                float warmupTime = def.building.turretBurstWarmupTime.RandomInRange;
                if (warmupTime > 0f)
                {
                    burstWarmupTicksLeft = warmupTime.SecondsToTicks();
                }
                else if (canBeginBurstImmediately)
                {
                    BeginBurst();
                }
                else
                {
                    burstWarmupTicksLeft = 1;
                }
            }
            else
            {
                ResetCurrentTarget();
            }
        }

        public virtual LocalTargetInfo TryFindNewTarget()
        {
            IAttackTargetSearcher searcher = this;
            Faction faction = searcher.Thing.Faction;
            float range = AttackVerb.EffectiveRange;
            if (Rand.Value < 0.5f && AttackVerb.ProjectileFliesOverhead() && faction.HostileTo(Faction.OfPlayer))
            {
                if (base.Map.listerBuildings.allBuildingsColonist.Where(delegate(Building x)
                {
                    float minRange = AttackVerb.verbProps.EffectiveMinRange(x, this);
                    float distSq = x.Position.DistanceToSquared(base.Position);
                    return distSq > minRange * minRange && distSq < range * range;
                }).TryRandomElement(out Building result))
                {
                    return result;
                }
            }
            TargetScanFlags flags = TargetScanFlags.NeedThreat | TargetScanFlags.NeedAutoTargetable;
            if (!AttackVerb.ProjectileFliesOverhead())
            {
                flags |= TargetScanFlags.NeedLOSToAll | TargetScanFlags.LOSBlockableByGas;
            }
            if (AttackVerb.IsIncendiary_Ranged())
            {
                flags |= TargetScanFlags.NeedNonBurning;
            }
            if (IsMortar)
            {
                flags |= TargetScanFlags.NeedNotUnderThickRoof;
            }
            return (Thing)AttackTargetFinder.BestShootTargetFromCurrentPosition(searcher, flags, IsValidTarget);
        }

        private IAttackTargetSearcher TargSearcher() => (mannableComp != null && mannableComp.MannedNow) ? (IAttackTargetSearcher)mannableComp.ManningPawn : this;

        private bool IsValidTarget(Thing t)
        {
            if (t is Pawn pawn)
            {
                if (base.Faction == Faction.OfPlayer && pawn.IsPrisoner) return false;
                if (AttackVerb.ProjectileFliesOverhead())
                {
                    RoofDef roof = base.Map.roofGrid.RoofAt(t.Position);
                    if (roof != null && roof.isThickRoof) return false;
                }
                if (mannableComp == null) return !GenAI.MachinesLike(base.Faction, pawn);
                if (pawn.RaceProps.Animal && pawn.Faction == Faction.OfPlayer) return false;
            }
            return true;
        }

        protected virtual void BeginBurst()
        {
            AttackVerb.TryStartCastOn(CurrentTarget);
            OnAttackedTarget(CurrentTarget);
        }

        protected void BurstComplete()
        {
            burstCooldownTicksLeft = BurstCooldownTime().SecondsToTicks();
        }

        protected float BurstCooldownTime() => (def.building.turretBurstCooldownTime >= 0f) ? def.building.turretBurstCooldownTime : AttackVerb.verbProps.defaultCooldownTime;

        public override string GetInspectString()
        {
            StringBuilder sb = new StringBuilder(base.GetInspectString());
            if (AttackVerb.verbProps.minRange > 0f)
            {
                sb.AppendLine("MinimumRange".Translate() + ": " + AttackVerb.verbProps.minRange.ToString("F0"));
            }
            if (base.Spawned && IsMortarOrProjectileFliesOverhead && base.Position.Roofed(base.Map))
            {
                sb.AppendLine("CannotFire".Translate() + ": " + "Roofed".Translate().CapitalizeFirst());
            }
            else if (base.Spawned && burstCooldownTicksLeft > 0 && BurstCooldownTime() > 5f)
            {
                sb.AppendLine("CanFireIn".Translate() + ": " + burstCooldownTicksLeft.ToStringSecondsFromTicks());
            }
            CompChangeableProjectile changeable = gun.TryGetComp<CompChangeableProjectile>();
            if (changeable != null)
            {
                sb.AppendLine(changeable.Loaded ? "ShellLoaded".Translate(changeable.LoadedShell.LabelCap, changeable.LoadedShell) : "ShellNotLoaded".Translate());
            }
            return sb.ToString().TrimEndNewlines();
        }

        protected override void DrawAt(Vector3 drawLoc, bool flip = false)
        {
            top.DrawTurret();
            base.DrawAt(drawLoc, flip);
        }

        public override void DrawExtraSelectionOverlays()
        {
            base.DrawExtraSelectionOverlays();
            float range = AttackVerb.EffectiveRange;
            if (range < 90f) GenDraw.DrawRadiusRing(base.Position, range);
            float minRange = AttackVerb.verbProps.EffectiveMinRange(allowAdjacentShot: true);
            if (minRange < 90f && minRange > 0.1f) GenDraw.DrawRadiusRing(base.Position, minRange);
            if (WarmingUp)
            {
                int degrees = (int)(burstWarmupTicksLeft * 0.5f);
                GenDraw.DrawAimPie(this, CurrentTarget, degrees, (float)def.size.x * 0.5f);
            }
            if (forcedTarget.IsValid && (!forcedTarget.HasThing || forcedTarget.Thing.Spawned))
            {
                Vector3 b = forcedTarget.HasThing ? forcedTarget.Thing.TrueCenter() : forcedTarget.Cell.ToVector3Shifted();
                Vector3 a = this.TrueCenter();
                b.y = a.y = AltitudeLayer.MetaOverlays.AltitudeFor();
                GenDraw.DrawLineBetween(a, b, MaterialPool.MatFrom(GenDraw.LineTexPath, ShaderDatabase.Transparent, new Color(1f, 0.5f, 0.5f)));
            }
        }

        private void ExtractShell() => GenPlace.TryPlaceThing(gun.TryGetComp<CompChangeableProjectile>().RemoveShell(), base.Position, base.Map, ThingPlaceMode.Near);
        
        private void ResetForcedTarget()
        {
            forcedTarget = LocalTargetInfo.Invalid;
            burstWarmupTicksLeft = 0;
            if (burstCooldownTicksLeft <= 0) TryStartShootSomething(canBeginBurstImmediately: false);
        }

        private void ResetCurrentTarget()
        {
            currentTargetInt = LocalTargetInfo.Invalid;
            burstWarmupTicksLeft = 0;
        }

        public void MakeGun()
        {
            gun = ThingMaker.MakeThing(def.building.turretGunDef);
            UpdateGunVerbs();
        }

        private void UpdateGunVerbs()
        {
            List<Verb> allVerbs = gun.TryGetComp<CompEquippable>().AllVerbs;
            for (int i = 0; i < allVerbs.Count; i++)
            {
                allVerbs[i].caster = this;
                allVerbs[i].castCompleteCallback = BurstComplete;
            }
        }
        
    }
}