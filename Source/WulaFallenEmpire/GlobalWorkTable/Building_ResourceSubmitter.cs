using RimWorld;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Verse;

namespace WulaFallenEmpire
{
    public class Building_ResourceSubmitter : Building_Storage
    {
        private CompPowerTrader powerComp;
        private CompRefuelable refuelableComp;
        private CompFlickable flickableComp;

        public override void SpawnSetup(Map map, bool respawningAfterLoad)
        {
            base.SpawnSetup(map, respawningAfterLoad);
            powerComp = GetComp<CompPowerTrader>();
            refuelableComp = GetComp<CompRefuelable>();
            flickableComp = GetComp<CompFlickable>();
        }

        /// <summary>
        /// 检查建筑是否可用（电力、燃料、开关等）
        /// </summary>
        public bool IsOperational
        {
            get
            {
                if (powerComp != null && !powerComp.PowerOn)
                    return false;
                if (refuelableComp != null && !refuelableComp.HasFuel)
                    return false;
                if (flickableComp != null && !flickableComp.SwitchIsOn)
                    return false;
                return true;
            }
        }

        /// <summary>
        /// 获取建筑的中心位置（用于生成 Skyfaller）
        /// </summary>
        public IntVec3 CenterPosition
        {
            get
            {
                // 对于偶数尺寸的建筑，返回中心附近的单元格
                var center = Position + new IntVec3(def.Size.x / 2, 0, def.Size.z / 2);
                // 确保在建筑范围内
                return center;
            }
        }

        public override IEnumerable<Gizmo> GetGizmos()
        {
            foreach (Gizmo g in base.GetGizmos())
            {
                yield return g;
            }

            // 添加提交到资源储存器的命令
            yield return new Command_Action
            {
                action = SubmitContentsToStorage,
                defaultLabel = "WULA_SubmitToStorage".Translate(),
                defaultDesc = "WULA_SubmitToStorageDesc".Translate(),
                icon = ContentFinder<Texture2D>.Get("UI/Commands/Upload"),
                disabledReason = GetDisabledReason()
            };
        }

        /// <summary>
        /// 获取存储的物品列表 - 修复版本
        /// </summary>
        private List<Thing> GetStoredItems()
        {
            var items = new List<Thing>();
            
            // 方法1：通过直接持有的物品获取（如果建筑本身是容器）
            if (this is IThingHolder thingHolder)
            {
                ThingOwner directlyHeldThings = thingHolder.GetDirectlyHeldThings();
                if (directlyHeldThings != null)
                {
                    items.AddRange(directlyHeldThings);
                }
            }
            
            // 方法2：通过存储设置获取地图上的物品
            if (items.Count == 0)
            {
                // 获取建筑的存储设置
                var storageSettings = GetStoreSettings();
                if (storageSettings != null)
                {
                    // 查找地图上被此建筑接受的物品
                    foreach (Thing thing in Map.listerThings.ThingsInGroup(ThingRequestGroup.HaulableAlways))
                    {
                        if (thing.Position.InHorDistOf(Position, 2f) && storageSettings.AllowedToAccept(thing))
                        {
                            items.Add(thing);
                        }
                    }
                }
            }
            
            return items;
        }

        /// <summary>
        /// 获取禁用原因
        /// </summary>
        private string GetDisabledReason()
        {
            if (!IsOperational)
            {
                if (powerComp != null && !powerComp.PowerOn)
                    return "WULA_NoPower".Translate();
                if (refuelableComp != null && !refuelableComp.HasFuel)
                    return "WULA_NoFuel".Translate();
                if (flickableComp != null && !flickableComp.SwitchIsOn)
                    return "WULA_SwitchOff".Translate();
            }
            
            if (GetStoredItems().Count == 0)
                return "WULA_NoItemsToSubmit".Translate();
                
            return string.Empty;
        }

        /// <summary>
        /// 提交内容到资源储存器
        /// </summary>
        private void SubmitContentsToStorage()
        {
            if (!IsOperational)
            {
                Messages.Message("WULA_DeviceNotOperational".Translate(), MessageTypeDefOf.RejectInput);
                return;
            }

            var storedItems = GetStoredItems();
            if (storedItems.Count == 0)
            {
                Messages.Message("WULA_NoItemsToSubmit".Translate(), MessageTypeDefOf.RejectInput);
                return;
            }

            // 执行提交逻辑
            if (TrySubmitItems(storedItems))
            {
                // 生成 Skyfaller 演出效果
                CreateSubmissionEffect();
                
                Messages.Message("WULA_ItemsSubmitted".Translate(storedItems.Count), MessageTypeDefOf.PositiveEvent);
            }
            else
            {
                Messages.Message("WULA_SubmissionFailed".Translate(), MessageTypeDefOf.NegativeEvent);
            }
        }

        /// <summary>
        /// 尝试提交物品到资源储存器
        /// </summary>
        private bool TrySubmitItems(List<Thing> items)
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
                var processedItems = new List<Thing>();

                foreach (Thing item in items)
                {
                    if (item == null || item.Destroyed)
                        continue;

                    // 检查是否为装备或武器
                    if (IsEquipment(item.def))
                    {
                        // 装备和武器直接添加到输出存储
                        globalStorage.AddToOutputStorage(item.def, item.stackCount);
                        processedItems.Add(item);
                        submittedCount += item.stackCount;
                    }
                    else
                    {
                        // 其他物品添加到输入存储
                        globalStorage.AddToInputStorage(item.def, item.stackCount);
                        processedItems.Add(item);
                        submittedCount += item.stackCount;
                    }
                }

                // 从世界中移除已提交的物品
                foreach (Thing item in processedItems)
                {
                    // 如果物品在建筑的直接容器中
                    if (this is IThingHolder thingHolder)
                    {
                        ThingOwner directlyHeldThings = thingHolder.GetDirectlyHeldThings();
                        if (directlyHeldThings != null && directlyHeldThings.Contains(item))
                        {
                            directlyHeldThings.Remove(item);
                        }
                    }
                    
                    // 如果物品在地图上，直接销毁
                    if (item.Spawned)
                    {
                        item.Destroy();
                    }
                }

                Log.Message($"Successfully submitted {submittedCount} items to global storage");
                return submittedCount > 0;
            }
            catch (System.Exception ex)
            {
                Log.Error($"Error submitting items to storage: {ex}");
                return false;
            }
        }

        /// <summary>
        /// 检查是否为装备或武器
        /// </summary>
        private bool IsEquipment(ThingDef thingDef)
        {
            return thingDef.IsApparel || thingDef.IsWeapon || thingDef.category == ThingCategory.Building;
        }

        /// <summary>
        /// 创建提交效果（Skyfaller）
        /// </summary>
        private void CreateSubmissionEffect()
        {
            try
            {
                // 获取 Skyfaller 定义
                ThingDef skyfallerDef = DefDatabase<ThingDef>.GetNamedSilentFail("DropPodIncoming");
                if (skyfallerDef == null)
                {
                    // 备用方案：使用简单的效果
                    CreateFallbackEffect();
                    return;
                }

                // 创建空的 Skyfaller
                Skyfaller skyfaller = (Skyfaller)ThingMaker.MakeThing(skyfallerDef);
                
                // 设置位置（建筑中心）
                IntVec3 dropPos = CenterPosition;
                
                // 确保位置有效
                if (!dropPos.IsValid || !dropPos.InBounds(Map))
                {
                    dropPos = Position; // 回退到建筑位置
                }

                // 生成 Skyfaller
                GenSpawn.Spawn(skyfaller, dropPos, Map);

                // 可选：添加一些视觉效果
                FleckMaker.ThrowLightningGlow(dropPos.ToVector3Shifted(), Map, 2f);
                
                Log.Message("Created submission skyfaller effect");
            }
            catch (System.Exception ex)
            {
                Log.Error($"Error creating skyfaller effect: {ex}");
                CreateFallbackEffect();
            }
        }

        /// <summary>
        /// 备用效果（如果 Skyfaller 失败）
        /// </summary>
        private void CreateFallbackEffect()
        {
            try
            {
                IntVec3 center = CenterPosition;
                
                // 生成闪光效果
                for (int i = 0; i < 3; i++)
                {
                    FleckMaker.ThrowLightningGlow(center.ToVector3Shifted(), Map, 1.5f);
                }
                
                // 生成烟雾效果
                FleckMaker.ThrowSmoke(center.ToVector3Shifted(), Map, 2f);
                
                Log.Message("Created fallback submission effect");
            }
            catch (System.Exception ex)
            {
                Log.Error($"Error creating fallback effect: {ex}");
            }
        }

        /// <summary>
        /// 修复的检查字符串方法 - 避免空行问题
        /// </summary>
        public override string GetInspectString()
        {
            StringBuilder stringBuilder = new StringBuilder();
            
            // 获取基础检查字符串
            string baseString = base.GetInspectString();
            if (!baseString.NullOrEmpty())
            {
                stringBuilder.Append(baseString);
            }
            
            // 获取存储信息
            var storedItems = GetStoredItems();
            int itemCount = storedItems.Count;
            int totalStack = storedItems.Sum(item => item.stackCount);

            // 添加存储信息
            if (stringBuilder.Length > 0)
            {
                stringBuilder.AppendLine();
            }
            stringBuilder.Append($"{"WULA_StoredItems".Translate()}: {itemCount} ({totalStack} {"WULA_Items".Translate()})");
            
            // 添加状态信息（如果不工作）
            if (!IsOperational)
            {
                if (stringBuilder.Length > 0)
                {
                    stringBuilder.AppendLine();
                }
                stringBuilder.Append($"{"WULA_Status".Translate()}: {"WULA_Inoperative".Translate()}");
            }

            return stringBuilder.ToString();
        }
    }
}
