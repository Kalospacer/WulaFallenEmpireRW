using RimWorld;
using System.Collections.Generic;
using Verse;
using Verse.AI;
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
            
            // 添加乘员相关的Gizmo
            var crewComp = this.TryGetComp<CompMechCrewHolder>();
            if (crewComp != null)
            {
                foreach (var gizmo in crewComp.CompGetGizmosExtra())
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
                        Messages.Message("WULA_CannotDraftWithoutPilot".Translate(this.LabelShort),
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
                command_Toggle.Disable("WULA_NoPilot".Translate());
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
            // 在死亡前弹出所有驾驶员和乘员
            var pilotComp = this.TryGetComp<CompMechPilotHolder>();
            if (pilotComp != null && pilotComp.HasPilots)
            {
                pilotComp.EjectAllPilotsOnDeath();
            }
            
            var crewComp = this.TryGetComp<CompMechCrewHolder>();
            if (crewComp != null && crewComp.HasCrew)
            {
                crewComp.RemoveAllCrew();
            }
            
            base.Kill(dinfo, exactCulprit);
        }
        
        // 重写销毁方法
        public override void Destroy(DestroyMode mode = DestroyMode.Vanish)
        {
            // 在销毁前弹出所有驾驶员和乘员
            var pilotComp = this.TryGetComp<CompMechPilotHolder>();
            if (pilotComp != null && pilotComp.HasPilots)
            {
                pilotComp.EjectAllPilotsOnDeath();
            }
            
            var crewComp = this.TryGetComp<CompMechCrewHolder>();
            if (crewComp != null && crewComp.HasCrew)
            {
                crewComp.RemoveAllCrew();
            }
            
            base.Destroy(mode);
        }
        
        // IThingHolder 接口实现
        public new ThingOwner GetDirectlyHeldThings()
        {
            // 合并驾驶员和乘员容器
            var pilotComp = this.TryGetComp<CompMechPilotHolder>();
            var crewComp = this.TryGetComp<CompMechCrewHolder>();
            
            if (pilotComp != null && crewComp != null)
            {
                // 合并两个容器
                var combined = new ThingOwner<Thing>(this);
                combined.TryAddRangeOrTransfer(pilotComp.innerContainer);
                combined.TryAddRangeOrTransfer(crewComp.innerContainer);
                return combined;
            }
            else if (pilotComp != null)
            {
                return pilotComp.innerContainer;
            }
            else if (crewComp != null)
            {
                return crewComp.innerContainer;
            }
            
            return null;
        }
        
        public new void GetChildHolders(List<IThingHolder> outChildren)
        {
            ThingOwnerUtility.AppendThingHoldersFromThings(outChildren, GetDirectlyHeldThings());
        }

        public override void ExposeData()
        {
            base.ExposeData();
        }
    }
}
