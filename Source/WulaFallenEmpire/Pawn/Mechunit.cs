// File: Wulamechunit_Fixed.cs
using RimWorld;
using System.Collections.Generic;
using Verse;
using Verse.AI.Group;

namespace WulaFallenEmpire
{
    public class Wulamechunit : Pawn, IThingHolder
    {
        public override void DrawExtraSelectionOverlays()
        {
            base.DrawExtraSelectionOverlays();
            pather.curPath?.DrawPath(this);
            jobs.DrawLinesBetweenTargets();
        }

        public override IEnumerable<Gizmo> GetGizmos()
        {
            foreach (Gizmo gizmo in base.GetGizmos())
            {
                yield return gizmo;
            }
            
            // 添加驾驶员相关的Gizmo
            var pilotComp = this.TryGetComp<CompMechPilotHolder>();
            if (pilotComp != null)
            {
                foreach (var gizmo in pilotComp.CompGetGizmosExtra())
                {
                    yield return gizmo;
                }
            }
            
            // 原有的征兆Gizmo
            if (drafter == null)
            {
                yield break;
            }
            
            foreach (Gizmo draftGizmo in GetDraftGizmos())
            {
                yield return draftGizmo;
            }
        }

        public IEnumerable<Gizmo> GetDraftGizmos()
        {
            AcceptanceReport allowsDrafting = this.GetLord()?.AllowsDrafting(this) ?? ((AcceptanceReport)true);

            if (!drafter.ShowDraftGizmo)
            {
                yield break;
            }
            
            Command_Toggle command_Toggle = new Command_Toggle
            {
                hotKey = KeyBindingDefOf.Command_ColonistDraft,
                isActive = () => base.Drafted,
                toggleAction = delegate
                {
                    // 检查是否有驾驶员
                    var pilotComp = this.TryGetComp<CompMechPilotHolder>();
                    if (pilotComp != null && !pilotComp.HasPilots)
                    {
                        Messages.Message("DD_CannotDraftWithoutPilot".Translate(this.LabelShort),
                            this, MessageTypeDefOf.RejectInput);
                        return;
                    }
                    
                    drafter.Drafted = !drafter.Drafted;
                    PlayerKnowledgeDatabase.KnowledgeDemonstrated(ConceptDefOf.Drafting, KnowledgeAmount.SpecificInteraction);
                    if (base.Drafted)
                    {
                        LessonAutoActivator.TeachOpportunity(ConceptDefOf.QueueOrders, OpportunityType.GoodToKnow);
                    }
                },
                defaultDesc = "CommandToggleDraftDesc".Translate(),
                icon = TexCommand.Draft,
                turnOnSound = SoundDefOf.DraftOn,
                turnOffSound = SoundDefOf.DraftOff,
                groupKeyIgnoreContent = 81729172,
                defaultLabel = (base.Drafted ? "CommandUndraftLabel" : "CommandDraftLabel").Translate()
            };

            if (base.Faction != Faction.OfPlayer)
            {
                command_Toggle.Disable("CannotOrderNonControlledLower".Translate());
            }

            if (base.Downed)
            {
                command_Toggle.Disable("IsIncapped".Translate(LabelShort, this));
            }
            if (base.Deathresting)
            {
                command_Toggle.Disable("IsDeathresting".Translate(this.Named("PAWN")));
            }
            
            // 没有驾驶员时禁用
            var pilotComp = this.TryGetComp<CompMechPilotHolder>();
            if (pilotComp != null && !pilotComp.HasPilots)
            {
                command_Toggle.Disable("DD_NoPilot".Translate());
            }
            
            command_Toggle.tutorTag = ((!base.Drafted) ? "Draft" : "Undraft");
            yield return command_Toggle;

            foreach (Gizmo attackGizmo in PawnAttackGizmoUtility.GetAttackGizmos(this))
            {
                if (!allowsDrafting && !attackGizmo.Disabled)
                {
                    attackGizmo.Disabled = true;
                    attackGizmo.disabledReason = allowsDrafting.Reason;
                }
                yield return attackGizmo;
            }
        }
        
        // 关键修复：重写死亡相关方法
        public override void Kill(DamageInfo? dinfo = null, Hediff exactCulprit = null)
        {
            // 在死亡前弹出所有驾驶员
            var pilotComp = this.TryGetComp<CompMechPilotHolder>();
            if (pilotComp != null && pilotComp.HasPilots)
            {
                pilotComp.EjectAllPilotsOnDeath();
            }
            
            base.Kill(dinfo, exactCulprit);
        }
        
        // 重写销毁方法
        public override void Destroy(DestroyMode mode = DestroyMode.Vanish)
        {
            // 在销毁前弹出所有驾驶员
            var pilotComp = this.TryGetComp<CompMechPilotHolder>();
            if (pilotComp != null && pilotComp.HasPilots)
            {
                pilotComp.EjectAllPilotsOnDeath();
            }
            
            base.Destroy(mode);
        }
        
        // IThingHolder 接口实现
        public new ThingOwner GetDirectlyHeldThings()
        {
            var pilotComp = this.TryGetComp<CompMechPilotHolder>();
            return pilotComp?.GetDirectlyHeldThings();
        }
        
        public new void GetChildHolders(List<IThingHolder> outChildren)
        {
            ThingOwnerUtility.AppendThingHoldersFromThings(outChildren, GetDirectlyHeldThings());
        }

        public override void ExposeData()
        {
            base.ExposeData();
            
            // 驾驶员容器的数据会在CompMechPilotHolder中自动保存
        }
    }
}
