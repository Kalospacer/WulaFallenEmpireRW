using RimWorld;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using Verse;
using Verse.Sound;
using RimWorld.Planet;

namespace WulaFallenEmpire
{
    public class CompValueConverter : CompLaunchable_TransportPod
    {
        public new CompProperties_ValueConverter Props => (CompProperties_ValueConverter)this.props;
        public CompGarbageShield GarbageShieldComp => this.parent.GetComp<CompGarbageShield>();

        // 新增：专门为价值转换器检查禁止物品
        public List<Thing> GetForbiddenItemsForConverter(ThingOwner container)
        {
            List<Thing> forbiddenItems = new List<Thing>();
            
            // 如果配置了专门的垃圾屏蔽组件，使用它
            if (GarbageShieldComp != null && GarbageShieldComp.GarbageShieldEnabled)
            {
                forbiddenItems.AddRange(GarbageShieldComp.GetForbiddenItems(container));
            }
            else
            {
                // 否则使用价值转换器自己的配置
                forbiddenItems.AddRange(GetForbiddenItemsByConverterConfig(container));
            }
            
            return forbiddenItems;
        }

        // 新增：根据价值转换器配置检查禁止物品
        private List<Thing> GetForbiddenItemsByConverterConfig(ThingOwner container)
        {
            List<Thing> forbiddenItems = new List<Thing>();
            
            if (!Props.garbageShieldEnabled) return forbiddenItems;
            
            foreach (Thing item in container)
            {
                if (IsForbiddenItemByConverterConfig(item))
                {
                    forbiddenItems.Add(item);
                }
            }
            
            return forbiddenItems;
        }

        // 新增：根据价值转换器配置判断是否为禁止物品
        private bool IsForbiddenItemByConverterConfig(Thing thing)
        {
            // 检查是否是殖民者
            if (thing is Pawn pawn)
                return true;

            // 检查是否是尸体
            if (thing.def.IsCorpse)
                return true;

            // 检查是否是有毒垃圾
            if (IsToxicWaste(thing))
                return true;

            // 检查不可交易物品（价值转换器专用）
            if (Props.checkNonTradableItems && IsNonTradableItem(thing))
                return true;

            return false;
        }

        // 新增：判断是否为有毒垃圾
        private bool IsToxicWaste(Thing thing)
        {
            return thing.def == ThingDefOf.Wastepack;
        }

        // 新增：判断是否为不可交易物品
        private bool IsNonTradableItem(Thing thing)
        {
            return thing.def.tradeability == Tradeability.None;
        }

        public override IEnumerable<Gizmo> CompGetGizmosExtra()
        {
            // 移除原有的发射按钮，替换为我们自己的价值转换按钮
            foreach (Gizmo gizmo in base.CompGetGizmosExtra())
            {
                if (gizmo is Command_Action launchCommand && 
                    (launchCommand.defaultDesc == "CommandLaunchGroupDesc".Translate() || 
                     launchCommand.defaultDesc == "CommandLaunchSingleDesc".Translate()))
                {
                    continue; // 跳过原版的发射按钮
                }
                yield return gizmo;
            }

            if (this.Transporter.LoadingInProgressOrReadyToLaunch)
            {
                Command_Action command = new Command_Action();
                command.defaultLabel = "WULA_ConvertToCurrency".Translate();
                command.defaultDesc = GetConversionDescription();
                command.icon = ContentFinder<Texture2D>.Get("UI/Commands/LaunchShip");
                command.action = delegate
                {
                    this.ConvertToCurrency();
                };
                
                // 禁用条件检查
                string disableReason;
                if (!CanConvert(out disableReason))
                {
                    command.Disable(disableReason);
                }

                yield return command;
            }
        }

        /// <summary>
        /// 获取转换描述信息
        /// </summary>
        private string GetConversionDescription()
        {
            try
            {
                StringBuilder sb = new StringBuilder();
                
                // 安全地获取目标货币标签
                string targetCurrencyLabel = "Unknown Currency";
                if (Props?.targetCurrency != null)
                {
                    targetCurrencyLabel = Props.targetCurrency.LabelCap;
                }
                
                sb.AppendLine("WULA_ConvertToCurrencyDesc".Translate(targetCurrencyLabel, (Props.conversionRatio * 100f).ToString("F0")));
                
                // 显示当前物品总价值和预计转换结果
                float totalValue = CalculateTotalValue();
                float convertedValue = totalValue * Props.conversionRatio;
                
                // 显示物品列表
                var items = GetItemList();
                if (items.Count > 0)
                {
                    sb.AppendLine();
                    sb.AppendLine("WULA_ContainedItems".Translate());
                    foreach (Thing item in items)
                    {
                        sb.AppendLine("  - " + item.LabelCap + " x" + item.stackCount);
                    }
                }

                return sb.ToString();
            }
            catch (System.Exception ex)
            {
                WulaLog.Debug($"[WULA ValueConverter] Error in GetConversionDescription: {ex}");
                return "WULA_ConversionDescriptionError".Translate();
            }
        }

        /// <summary>
        /// 检查是否可以转换
        /// </summary>
        private bool CanConvert(out string reason)
        {
            reason = null;

            if (!this.parent.Spawned)
            {
                reason = "WULA_ConverterNotSpawned".Translate();
                return false;
            }

            CompTransporter transporter = this.Transporter;
            if (transporter == null || !transporter.innerContainer.Any)
            {
                reason = "WULA_NoItemsToConvert".Translate();
                return false;
            }

            // 检查基类的发射条件（燃料、冷却时间等）
            var baseResult = base.CanLaunch(null);
            if (!baseResult.Accepted)
            {
                reason = baseResult.Reason;
                return false;
            }

            return true;
        }

        /// <summary>
        /// 转换为货币
        /// </summary>
        public void ConvertToCurrency()
        {
            CompTransporter transporter = this.Transporter;
            var globalStorage = Find.World.GetComponent<GlobalStorageWorldComponent>();

            // 检查垃圾屏蔽 - 使用价值转换器专用的检查方法
            List<Thing> forbiddenItems = GetForbiddenItemsForConverter(transporter.innerContainer);
            if (forbiddenItems.Count > 0)
            {
                // 显示取消发射消息
                StringBuilder forbiddenList = new StringBuilder();
                foreach (Thing item in forbiddenItems)
                {
                    if (forbiddenList.Length > 0) forbiddenList.Append(", ");
                    forbiddenList.Append($"{item.LabelCap} x{item.stackCount}");
                }

                Messages.Message("WULA_LaunchCancelledDueToForbiddenItems".Translate(forbiddenList.ToString()),
                    this.parent, MessageTypeDefOf.RejectInput);

                // 触发垃圾屏蔽UI事件
                ProcessGarbageShieldTriggerForConverter(forbiddenItems);

                return; // 取消发射
            }

            if (!this.parent.Spawned)
            {
                WulaLog.Debug("Tried to convert " + this.parent + " but it's not spawned.");
                return;
            }

            if (transporter == null || !transporter.innerContainer.Any)
            {
                Messages.Message("WULA_NoItemsToConvert".Translate(), this.parent, MessageTypeDefOf.RejectInput);
                return;
            }

            // 1. 计算总价值并生成白银
            float totalValue = CalculateTotalValue();
            float convertedValue = totalValue * Props.conversionRatio;
            int silverAmount = Mathf.FloorToInt(convertedValue);

            if (silverAmount <= 0)
            {
                Messages.Message("WULA_ConvertedValueTooLow".Translate(), this.parent, MessageTypeDefOf.RejectInput);
                return;
            }

            // 2. 将白银添加到全局存储的输入储存器
            if (globalStorage != null)
            {
                globalStorage.AddToInputStorage(Props.targetCurrency, silverAmount);
            }
            else
            {
                WulaLog.Debug("Could not find GlobalStorageWorldComponent.");
                return;
            }

            // 3. 统计转换的物品信息
            StringBuilder convertedItems = new StringBuilder();
            int itemCount = 0;
            float originalTotalValue = 0f;

            foreach (Thing item in transporter.innerContainer)
            {
                itemCount += item.stackCount;
                originalTotalValue += item.MarketValue * item.stackCount;
                
                if (convertedItems.Length > 0) convertedItems.Append(", ");
                convertedItems.Append($"{item.LabelCap} x{item.stackCount}");
            }

            // 4. 清空容器
            transporter.innerContainer.ClearAndDestroyContents();

            // 5. 显示转换结果消息
            string message = BuildConversionMessage(itemCount, originalTotalValue, silverAmount, convertedItems.ToString());
            Messages.Message(message, this.parent, MessageTypeDefOf.PositiveEvent);

            // 6. 调用基类的发射方法，处理动画和销毁
            // 使用当前地图的tile作为目的地，arrivalAction为null
            base.TryLaunch(this.parent.Map.Tile, null);
        }

        /// <summary>
        /// 新增：价值转换器专用的垃圾屏蔽触发处理
        /// </summary>
        private void ProcessGarbageShieldTriggerForConverter(List<Thing> forbiddenItems)
        {
            if (forbiddenItems.Count > 0)
            {
                string uiEventDefName = Props.garbageShieldUIEventDefName;
                
                // 如果配置了专门的垃圾屏蔽组件，使用它的UI事件配置
                if (GarbageShieldComp != null && !string.IsNullOrEmpty(GarbageShieldComp.Props.garbageShieldUIEventDefName))
                {
                    uiEventDefName = GarbageShieldComp.Props.garbageShieldUIEventDefName;
                }
                
                if (!string.IsNullOrEmpty(uiEventDefName))
                {
                    EventDef uiDef = DefDatabase<EventDef>.GetNamed(uiEventDefName, false);
                    if (uiDef != null)
                    {
                        Find.WindowStack.Add(new Dialog_CustomDisplay(uiDef));
                    }
                    else
                    {
                        WulaLog.Debug($"[CompValueConverter] Could not find EventDef named '{uiEventDefName}'.");
                    }
                }
            }
        }

        /// <summary>
        /// 计算容器内物品的总价值
        /// </summary>
        private float CalculateTotalValue()
        {
            float totalValue = 0f;
            CompTransporter transporter = this.Transporter;

            if (transporter != null)
            {
                foreach (Thing item in transporter.innerContainer)
                {
                    totalValue += item.MarketValue * item.stackCount;
                }
            }

            return totalValue;
        }

        /// <summary>
        /// 获取物品列表
        /// </summary>
        private List<Thing> GetItemList()
        {
            List<Thing> items = new List<Thing>();
            CompTransporter transporter = this.Transporter;

            if (transporter != null)
            {
                foreach (Thing item in transporter.innerContainer)
                {
                    items.Add(item);
                }
            }

            return items;
        }

        /// <summary>
        /// 构建转换消息
        /// </summary>
        private string BuildConversionMessage(int itemCount, float originalValue, int silverAmount, string itemList)
        {
            StringBuilder message = new StringBuilder();
            
            message.Append("WULA_ConversionComplete".Translate(itemCount, originalValue.ToString("F2"), silverAmount, Props.targetCurrency?.LabelCap ?? "Unknown Currency"));
            
            if (!string.IsNullOrEmpty(itemList))
            {
                message.Append("\n\n");
                message.Append("WULA_ConvertedItems".Translate(itemList));
            }
            
            message.Append("\n\n");
            message.Append("WULA_ConversionRatioApplied".Translate((Props.conversionRatio * 100f).ToString("F0")));

            return message.ToString();
        }

        /// <summary>
        /// 重写基类的发射方法，确保使用我们的逻辑
        /// </summary>
        public new void TryLaunch(PlanetTile destinationTile, TransportersArrivalAction arrivalAction)
        {
            // 这个方法不应该被直接调用，应该使用ConvertToCurrency
            WulaLog.Debug("CompValueConverter.TryLaunch should not be called directly. Use ConvertToCurrency instead.");
            ConvertToCurrency();
        }
    }
}
