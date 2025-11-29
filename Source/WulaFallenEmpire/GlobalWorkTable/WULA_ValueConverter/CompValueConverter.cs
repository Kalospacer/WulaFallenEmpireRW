using RimWorld;
using System.Collections.Generic;
using UnityEngine;
using Verse;
using Verse.Sound;
using System.Text;
using System.Linq;
using RimWorld.Planet;


namespace WulaFallenEmpire
{
    public class CompValueConverter : CompLaunchable_TransportPod
    {
        public new CompProperties_ValueConverter Props => (CompProperties_ValueConverter)this.props;
        
        // 获取垃圾屏蔽组件
        public CompGarbageShield GarbageShieldComp => this.parent.GetComp<CompGarbageShield>();
        
        // 获取容器组件
        public new CompTransporter Transporter => this.parent.GetComp<CompTransporter>();

        public override IEnumerable<Gizmo> CompGetGizmosExtra()
        {
            // 首先处理基类的Gizmo，但过滤掉原版的发射按钮
            foreach (Gizmo gizmo in base.CompGetGizmosExtra())
            {
                // 跳过原版的发射按钮
                if (gizmo is Command_Action launchCommand && 
                    (launchCommand.defaultDesc == "CommandLaunchGroupDesc".Translate() || 
                     launchCommand.defaultDesc == "CommandLaunchSingleDesc".Translate()))
                {
                    continue;
                }
                yield return gizmo;
            }

            // 添加我们的转换按钮
            if (Transporter != null && Transporter.innerContainer.Any)
            {
                Command_Action command = new Command_Action();
                command.defaultLabel = "WULA_ConvertToSilver".Translate();
                command.defaultDesc = "WULA_ConvertToSilverDesc".Translate(Props.conversionRate.ToStringPercent());
                command.icon = ContentFinder<Texture2D>.Get("UI/Commands/ConvertToSilver");
                command.action = delegate
                {
                    this.TryLaunchToSilver();
                };
                
                // 添加禁用状态检查
                if (!CanConvert())
                {
                    command.Disable("WULA_CannotConvert".Translate());
                }
                
                yield return command;
            }
        }

        /// <summary>
        /// 检查是否可以执行转换
        /// </summary>
        private bool CanConvert()
        {
            if (Transporter == null || !Transporter.innerContainer.Any)
                return false;

            // 检查垃圾屏蔽
            if (GarbageShieldComp != null && GarbageShieldComp.GarbageShieldEnabled)
            {
                List<Thing> forbiddenItems = GarbageShieldComp.GetForbiddenItems(Transporter.innerContainer);
                if (forbiddenItems.Count > 0)
                    return false;
            }

            return true;
        }

        /// <summary>
        /// 将物品价值转换为白银
        /// </summary>
        public void TryLaunchToSilver()
        {
            if (!this.parent.Spawned)
            {
                Log.Error("Tried to convert value from " + this.parent + " but it's not spawned.");
                return;
            }

            var globalStorage = Find.World.GetComponent<GlobalStorageWorldComponent>();
            if (globalStorage == null)
            {
                Log.Error("Could not find GlobalStorageWorldComponent.");
                return;
            }

            if (Transporter == null || !Transporter.innerContainer.Any)
            {
                Messages.Message("WULA_NoItemsToConvert".Translate(), this.parent, MessageTypeDefOf.RejectInput);
                return;
            }

            // 检查垃圾屏蔽
            if (GarbageShieldComp != null && GarbageShieldComp.GarbageShieldEnabled)
            {
                List<Thing> forbiddenItems = GarbageShieldComp.GetForbiddenItems(Transporter.innerContainer);
                if (forbiddenItems.Count > 0)
                {
                    StringBuilder forbiddenList = new StringBuilder();
                    foreach (Thing item in forbiddenItems)
                    {
                        if (forbiddenList.Length > 0) forbiddenList.Append(", ");
                        forbiddenList.Append($"{item.LabelCap} x{item.stackCount}");
                    }

                    Messages.Message("WULA_ConversionCancelledDueToForbiddenItems".Translate(forbiddenList.ToString()),
                        this.parent, MessageTypeDefOf.RejectInput);

                    GarbageShieldComp.ProcessGarbageShieldTrigger(forbiddenItems);
                    return;
                }
            }

            // 计算总价值
            float totalValue = CalculateTotalValue();
            if (totalValue <= 0)
            {
                Messages.Message("WULA_NoValuableItems".Translate(), this.parent, MessageTypeDefOf.RejectInput);
                return;
            }

            // 应用转换倍率
            int silverAmount = CalculateSilverAmount(totalValue);
            if (silverAmount <= 0)
            {
                Messages.Message("WULA_ConversionValueTooLow".Translate(), this.parent, MessageTypeDefOf.RejectInput);
                return;
            }

            // 执行转换
            PerformConversion(globalStorage, silverAmount, totalValue);

            // 调用基类的发射方法，让它处理动画和销毁
            // 注意：这里我们发射到当前地图的同一个位置，实际上只是利用发射动画
            base.TryLaunch(this.parent.Map.Tile, null);
        }

        /// <summary>
        /// 重写基类的TryLaunch方法，阻止原版发射逻辑
        /// </summary>
        public new void TryLaunch(PlanetTile destinationTile, TransportersArrivalAction arrivalAction)
        {
            // 阻止原版发射逻辑，使用我们的转换逻辑
            TryLaunchToSilver();
        }

        /// <summary>
        /// 计算容器内物品的总价值
        /// </summary>
        private float CalculateTotalValue()
        {
            float totalValue = 0f;
            
            foreach (Thing item in Transporter.innerContainer)
            {
                // 计算单个物品的市场价值
                float itemValue = item.MarketValue * item.stackCount;
                totalValue += itemValue;
                
                if (Prefs.DevMode)
                {
                    Log.Message($"[ValueConverter] {item.LabelCap} x{item.stackCount}: {item.MarketValue} each, total: {itemValue}");
                }
            }
            
            if (Prefs.DevMode)
            {
                Log.Message($"[ValueConverter] Total value before conversion: {totalValue}");
            }
            return totalValue;
        }

        /// <summary>
        /// 计算转换后的白银数量
        /// </summary>
        private int CalculateSilverAmount(float totalValue)
        {
            // 应用转换倍率
            float convertedValue = totalValue * Props.conversionRate;
            
            // 转换为白银（白银的市场价值为1）
            int silverAmount = Mathf.FloorToInt(convertedValue);
            
            if (Prefs.DevMode)
            {
                Log.Message($"[ValueConverter] After conversion rate ({Props.conversionRate}): {convertedValue}, Silver amount: {silverAmount}");
            }
            
            return silverAmount;
        }

        /// <summary>
        /// 执行转换操作
        /// </summary>
        private void PerformConversion(GlobalStorageWorldComponent globalStorage, int silverAmount, float originalValue)
        {
            // 1. 将白银添加到全局存储器的输入端
            ThingDef silverDef = Props.outputThingDef ?? ThingDefOf.Silver;
            globalStorage.AddToInputStorage(silverDef, silverAmount);

            // 2. 显示转换结果消息
            ShowConversionMessage(silverAmount, originalValue);

            // 4. 清空容器
            Transporter.innerContainer.ClearAndDestroyContents();

            // 5. 如果配置为转换后销毁，则销毁建筑
            if (Props.destroyAfterConversion)
            {
                this.parent.Destroy(DestroyMode.Vanish);
            }
        }

        /// <summary>
        /// 显示转换结果消息
        /// </summary>
        private void ShowConversionMessage(int silverAmount, float originalValue)
        {
            string message;
            
            if (Props.conversionRate < 1.0f)
            {
                message = "WULA_ValueConvertedWithLoss".Translate(
                    originalValue.ToString("F0"), 
                    silverAmount, 
                    Props.conversionRate.ToStringPercent()
                );
            }
            else if (Props.conversionRate > 1.0f)
            {
                message = "WULA_ValueConvertedWithBonus".Translate(
                    originalValue.ToString("F0"), 
                    silverAmount, 
                    Props.conversionRate.ToStringPercent()
                );
            }
            else
            {
                message = "WULA_ValueConverted".Translate(
                    originalValue.ToString("F0"), 
                    silverAmount
                );
            }
            
            Messages.Message(message, this.parent, MessageTypeDefOf.PositiveEvent);
        }

        /// <summary>
        /// 获取转换效率描述（用于界面显示）
        /// </summary>
        public string GetConversionEfficiencyDescription()
        {
            StringBuilder sb = new StringBuilder();
            
            sb.AppendLine("WULA_ConversionEfficiency".Translate(Props.conversionRate.ToStringPercent()));
            
            if (Props.conversionRate < 1.0f)
            {
                sb.AppendLine("WULA_ConversionEfficiencyLoss".Translate());
            }
            else if (Props.conversionRate > 1.0f)
            {
                sb.AppendLine("WULA_ConversionEfficiencyBonus".Translate());
            }
            else
            {
                sb.AppendLine("WULA_ConversionEfficiencyNormal".Translate());
            }
            
            return sb.ToString();
        }

        /// <summary>
        /// 在检查器中显示转换信息
        /// </summary>
        public override string CompInspectStringExtra()
        {
            StringBuilder sb = new StringBuilder();
            
            if (Transporter != null && Transporter.innerContainer.Any)
            {
                // 显示容器内物品总价值
                float currentValue = CalculateTotalValue();
                int potentialSilver = CalculateSilverAmount(currentValue);
                
                sb.AppendLine("WULA_CurrentValueInContainer".Translate(currentValue.ToString("F0")));
                sb.AppendLine("WULA_PotentialSilver".Translate(potentialSilver));
                sb.AppendLine("WULA_ConversionRate".Translate(Props.conversionRate.ToStringPercent()));
            }
            
            return sb.ToString().TrimEndNewlines();
        }
    }
}
