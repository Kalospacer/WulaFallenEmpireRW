using RimWorld;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Verse;
using System;
using System.Text;

namespace WulaFallenEmpire
{
    public class CompResourceSubmitter : ThingComp, IThingHolder
    {
        public ThingOwner innerContainer;
        public List<TransferableOneWay> leftToLoad;
        
        private bool massUsageDirty = true;
        private float cachedMassUsage;
        
        public CompProperties_ResourceSubmitter Props => (CompProperties_ResourceSubmitter)props;
        
        public float MassUsage
        {
            get
            {
                if (massUsageDirty)
                {
                    massUsageDirty = false;
                    cachedMassUsage = CollectionsMassCalculator.MassUsage(innerContainer, IgnorePawnsInventoryMode.IgnoreIfAssignedToUnload, includePawnsMass: false);
                }
                return cachedMassUsage;
            }
        }
        
        public bool OverMassCapacity => MassUsage > Props.massCapacity;
        
        public bool AnythingLeftToLoad => FirstThingLeftToLoad != null;
        
        public Thing FirstThingLeftToLoad
        {
            get
            {
                if (leftToLoad == null) return null;
                for (int i = 0; i < leftToLoad.Count; i++)
                {
                    if (leftToLoad[i].CountToTransfer != 0 && leftToLoad[i].HasAnyThing)
                        return leftToLoad[i].AnyThing;
                }
                return null;
            }
        }
        
        public CompResourceSubmitter()
        {
            innerContainer = new ThingOwner<Thing>(this);
        }
        
        public override void PostExposeData()
        {
            base.PostExposeData();
            Scribe_Deep.Look(ref innerContainer, "innerContainer", this);
            Scribe_Collections.Look(ref leftToLoad, "leftToLoad", LookMode.Deep);
            
            if (Scribe.mode == LoadSaveMode.Saving)
            {
                leftToLoad?.RemoveWhere(t => t == null);
            }
        }
        
        public ThingOwner GetDirectlyHeldThings()
        {
            return innerContainer;
        }
        
        public void GetChildHolders(List<IThingHolder> outChildren)
        {
            ThingOwnerUtility.AppendThingHoldersFromThings(outChildren, GetDirectlyHeldThings());
        }

        // 在 CompResourceSubmitter 类中添加或更新以下方法：
        public override void PostSpawnSetup(bool respawningAfterLoad)
        {
            base.PostSpawnSetup(respawningAfterLoad);
            massUsageDirty = true;
        }

        public override void PostDeSpawn(Map map, DestroyMode mode = DestroyMode.Vanish)
        {
            base.PostDeSpawn(map, mode);
            if (mode != DestroyMode.WillReplace)
            {
                innerContainer.TryDropAll(parent.Position, map, ThingPlaceMode.Near);
            }
        }
        
        public override IEnumerable<Gizmo> CompGetGizmosExtra()
        {
            foreach (Gizmo g in base.CompGetGizmosExtra())
            {
                yield return g;
            }
            
            // 装载命令
            Command_LoadToResourceSubmitter loadCommand = new Command_LoadToResourceSubmitter();
            loadCommand.defaultLabel = "WULA_LoadResourceSubmitter".Translate();
            loadCommand.defaultDesc = "WULA_LoadResourceSubmitterDesc".Translate();
            loadCommand.icon = ContentFinder<Texture2D>.Get("UI/Commands/LoadTransporter");
            loadCommand.submitterComp = this;
            
            // 禁用检查
            if (!parent.Spawned)
            {
                loadCommand.Disable("WULA_NotSpawned".Translate());
            }
            else if (!IsOperational())
            {
                loadCommand.Disable(GetInoperativeReason());
            }
            
            yield return loadCommand;
            
            // 取消装载/卸载命令
            if (innerContainer.Any || AnythingLeftToLoad)
            {
                Command_Action cancelCommand = new Command_Action();
                cancelCommand.defaultLabel = innerContainer.Any ? "WULA_Unload".Translate() : "WULA_CancelLoad".Translate();
                cancelCommand.defaultDesc = innerContainer.Any ? "WULA_UnloadDesc".Translate() : "WULA_CancelLoadDesc".Translate();
                cancelCommand.icon = ContentFinder<Texture2D>.Get("UI/Designators/Cancel");
                cancelCommand.action = CancelLoad;
                yield return cancelCommand;
            }
            
            // 发射命令
            Command_Action launchCommand = new Command_Action();
            launchCommand.defaultLabel = "WULA_LaunchSubmitter".Translate();
            launchCommand.defaultDesc = "WULA_LaunchSubmitterDesc".Translate();
            launchCommand.icon = ContentFinder<Texture2D>.Get("UI/Commands/Launch");
            launchCommand.action = TryLaunch;
            
            // 发射条件检查
            if (!parent.Spawned)
            {
                launchCommand.Disable("WULA_NotSpawned".Translate());
            }
            else if (!IsOperational())
            {
                launchCommand.Disable(GetInoperativeReason());
            }
            else if (!innerContainer.Any)
            {
                launchCommand.Disable("WULA_NoItemsToSubmit".Translate());
            }
            else if (OverMassCapacity)
            {
                launchCommand.Disable("WULA_OverMassCapacity".Translate(MassUsage.ToString("F1"), Props.massCapacity.ToString("F1")));
            }
            
            yield return launchCommand;
        }
        
        public override string CompInspectStringExtra()
        {
            StringBuilder stringBuilder = new StringBuilder();
            stringBuilder.Append("WULA_SubmitterContents".Translate() + ": " + innerContainer.ContentsString.CapitalizeFirst());
            
            string massString = "WULA_Mass".Translate() + ": " + MassUsage.ToString("F1") + " / " + Props.massCapacity.ToString("F1") + " kg";
            stringBuilder.AppendLine().Append(OverMassCapacity ? massString.Colorize(ColorLibrary.RedReadable) : massString);
            
            if (!IsOperational())
            {
                stringBuilder.AppendLine().Append("WULA_Status".Translate() + ": " + "WULA_Inoperative".Translate().Colorize(ColorLibrary.RedReadable));
            }
            
            return stringBuilder.ToString();
        }
        
        public void AddToTheToLoadList(TransferableOneWay t, int count)
        {
            if (!t.HasAnyThing || count <= 0) return;
            
            if (leftToLoad == null)
            {
                leftToLoad = new List<TransferableOneWay>();
            }
            
            TransferableOneWay existing = TransferableUtility.TransferableMatching(t.AnyThing, leftToLoad, TransferAsOneMode.PodsOrCaravanPacking);
            if (existing != null)
            {
                for (int i = 0; i < t.things.Count; i++)
                {
                    if (!existing.things.Contains(t.things[i]))
                    {
                        existing.things.Add(t.things[i]);
                    }
                }
                if (existing.CanAdjustBy(count).Accepted)
                {
                    existing.AdjustBy(count);
                }
            }
            else
            {
                TransferableOneWay newTransferable = new TransferableOneWay();
                leftToLoad.Add(newTransferable);
                newTransferable.things.AddRange(t.things);
                newTransferable.AdjustTo(count);
            }
        }
        
        public void Notify_ThingAdded(Thing t)
        {
            SubtractFromToLoadList(t, t.stackCount);
            massUsageDirty = true;
        }
        
        public void Notify_ThingRemoved(Thing t)
        {
            massUsageDirty = true;
        }
        
        public void Notify_ThingAddedAndMergedWith(Thing t, int mergedCount)
        {
            SubtractFromToLoadList(t, mergedCount);
            massUsageDirty = true;
        }
        
        private int SubtractFromToLoadList(Thing t, int count)
        {
            if (leftToLoad == null) return 0;
            
            TransferableOneWay transferable = TransferableUtility.TransferableMatchingDesperate(t, leftToLoad, TransferAsOneMode.PodsOrCaravanPacking);
            if (transferable == null || transferable.CountToTransfer <= 0) return 0;
            
            int num = Mathf.Min(count, transferable.CountToTransfer);
            transferable.AdjustBy(-num);
            
            if (transferable.CountToTransfer <= 0)
            {
                leftToLoad.Remove(transferable);
            }
            
            return num;
        }
        
        private void CancelLoad()
        {
            if (leftToLoad != null)
            {
                leftToLoad.Clear();
            }
            innerContainer.TryDropAll(parent.Position, parent.Map, ThingPlaceMode.Near);
            massUsageDirty = true;
        }
        
        private void TryLaunch()
        {
            if (!IsOperational())
            {
                Messages.Message(GetInoperativeReason(), MessageTypeDefOf.RejectInput);
                return;
            }
            
            if (!innerContainer.Any)
            {
                Messages.Message("WULA_NoItemsToSubmit".Translate(), MessageTypeDefOf.RejectInput);
                return;
            }
            
            if (OverMassCapacity)
            {
                Messages.Message("WULA_OverMassCapacity".Translate(MassUsage.ToString("F1"), Props.massCapacity.ToString("F1")), MessageTypeDefOf.RejectInput);
                return;
            }
            
            if (SubmitContentsToStorage())
            {
                CreateLaunchEffect();
                parent.Destroy();
            }
        }
        
        private bool SubmitContentsToStorage()
        {
            try
            {
                var globalStorage = Find.World.GetComponent<GlobalStorageWorldComponent>();
                if (globalStorage == null)
                {
                    Log.Error("GlobalStorageWorldComponent not found");
                    return false;
                }

                int submittedCount = 0;
                List<Thing> processedItems = new List<Thing>();
                
                // 复制列表以避免修改时迭代
                List<Thing> itemsToProcess = innerContainer.ToList();
                
                foreach (Thing item in itemsToProcess)
                {
                    if (item == null || item.Destroyed) continue;

                    if (IsEquipment(item.def))
                    {
                        globalStorage.AddToOutputStorage(item.def, item.stackCount);
                    }
                    else
                    {
                        globalStorage.AddToInputStorage(item.def, item.stackCount);
                    }
                    
                    processedItems.Add(item);
                    submittedCount += item.stackCount;
                }

                // 从容器中移除已提交的物品
                foreach (Thing item in processedItems)
                {
                    innerContainer.Remove(item);
                }

                Messages.Message("WULA_ItemsSubmitted".Translate(submittedCount), MessageTypeDefOf.PositiveEvent);
                Log.Message($"Successfully submitted {submittedCount} items to global storage");
                return submittedCount > 0;
            }
            catch (Exception ex)
            {
                Log.Error($"Error submitting items to storage: {ex}");
                Messages.Message("WULA_SubmissionFailed".Translate(), MessageTypeDefOf.NegativeEvent);
                return false;
            }
        }
        
        private bool IsEquipment(ThingDef thingDef)
        {
            return thingDef.IsApparel || thingDef.IsWeapon || thingDef.category == ThingCategory.Building;
        }
        
        private void CreateLaunchEffect()
        {
            try
            {
                // 使用自定义的 Skyfaller 定义
                ThingDef skyfallerDef = DefDatabase<ThingDef>.GetNamedSilentFail("ResourceSubmitterSkyfaller");
                if (skyfallerDef == null)
                {
                    // 备用：使用运输舱效果
                    skyfallerDef = DefDatabase<ThingDef>.GetNamedSilentFail("DropPodIncoming");
                }
                
                if (skyfallerDef != null)
                {
                    Skyfaller skyfaller = (Skyfaller)ThingMaker.MakeThing(skyfallerDef);
                    GenSpawn.Spawn(skyfaller, parent.Position, parent.Map);
                }
                
                // 视觉效果
                for (int i = 0; i < 3; i++)
                {
                    FleckMaker.ThrowLightningGlow(parent.DrawPos, parent.Map, 2f);
                }
                FleckMaker.ThrowSmoke(parent.DrawPos, parent.Map, 3f);
            }
            catch (Exception ex)
            {
                Log.Error($"Error creating launch effect: {ex}");
            }
        }
        
        private bool IsOperational()
        {
            var building = parent as Building_ResourceSubmitter;
            return building?.IsOperational ?? false;
        }
        
        private string GetInoperativeReason()
        {
            var building = parent as Building_ResourceSubmitter;
            return building?.GetInoperativeReason() ?? "WULA_UnknownReason".Translate();
        }
    }
    
    public class CompProperties_ResourceSubmitter : CompProperties
    {
        public float massCapacity = 150f;
        
        public CompProperties_ResourceSubmitter()
        {
            compClass = typeof(CompResourceSubmitter);
        }
    }
}
