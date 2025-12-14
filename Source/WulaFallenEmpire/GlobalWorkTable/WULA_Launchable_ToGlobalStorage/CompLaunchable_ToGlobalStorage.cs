using RimWorld;
using System.Collections.Generic;
using UnityEngine;
using Verse;
using Verse.Sound;
using System.Text;
using System.Linq;

namespace WulaFallenEmpire
{
    public class CompLaunchable_ToGlobalStorage : CompLaunchable_TransportPod
    {
        public new CompProperties_Launchable_ToGlobalStorage Props => (CompProperties_Launchable_ToGlobalStorage)this.props;

        // 获取垃圾屏蔽组件
        public CompGarbageShield GarbageShieldComp => this.parent.GetComp<CompGarbageShield>();

        public override IEnumerable<Gizmo> CompGetGizmosExtra()
        {
            // 移除原有的发射按钮，替换为我们自己的
            foreach (Gizmo gizmo in base.CompGetGizmosExtra())
            {
                if (gizmo is Command_Action launchCommand && (launchCommand.defaultDesc == "CommandLaunchGroupDesc".Translate() || launchCommand.defaultDesc == "CommandLaunchSingleDesc".Translate()))
                {
                    continue; // 跳过原版的发射按钮
                }
                yield return gizmo;
            }

            if (this.Transporter.LoadingInProgressOrReadyToLaunch)
            {
                Command_Action command = new Command_Action();
                command.defaultLabel = "WULA_LaunchToGlobalStorage".Translate();
                command.defaultDesc = "WULA_LaunchToGlobalStorageDesc".Translate();
                command.icon = ContentFinder<Texture2D>.Get("UI/Commands/LaunchShip");
                command.action = delegate
                {
                    this.TryLaunch();
                };
                yield return command;
            }
        }

        public void TryLaunch()
        {
            if (!this.parent.Spawned)
            {
                Log.Error("Tried to launch " + this.parent + " but it's not spawned.");
                return;
            }

            var globalStorage = Find.World.GetComponent<GlobalStorageWorldComponent>();
            if (globalStorage == null)
            {
                Log.Error("Could not find GlobalStorageWorldComponent.");
                return;
            }

            CompTransporter transporter = this.Transporter;
            if (transporter == null || !transporter.innerContainer.Any)
            {
                Messages.Message("WULA_NoItemsToSendToGlobalStorage".Translate(), this.parent, MessageTypeDefOf.RejectInput);
                return;
            }

            // 检查垃圾屏蔽 - 如果启用了垃圾屏蔽并且有禁止物品，取消发射
            if (GarbageShieldComp != null && GarbageShieldComp.GarbageShieldEnabled)
            {
                List<Thing> forbiddenItems = GarbageShieldComp.GetForbiddenItems(transporter.innerContainer);
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
                    GarbageShieldComp.ProcessGarbageShieldTrigger(forbiddenItems);
                    
                    return; // 取消发射
                }
            }

            // 统计发送的物品
            int inputItemsCount = 0;
            int outputItemsCount = 0;
            StringBuilder inputItemsList = new StringBuilder();
            StringBuilder outputItemsList = new StringBuilder();

            // 1. 将物品分类转移到相应的存储
            foreach (Thing item in transporter.innerContainer.ToList())
            {
                if (ShouldGoToOutputStorage(item))
                {
                    int moved = item.stackCount;
                    transporter.innerContainer.TryTransferToContainer(item, globalStorage.outputContainer, moved, true);
                    outputItemsCount += moved;
                    if (outputItemsList.Length > 0) outputItemsList.Append(", ");
                    outputItemsList.Append($"{item.LabelCap} x{moved}");
                }
                else
                {
                    int moved = item.stackCount;
                    transporter.innerContainer.TryTransferToContainer(item, globalStorage.inputContainer, moved, true);
                    inputItemsCount += moved;
                    if (inputItemsList.Length > 0) inputItemsList.Append(", ");
                    inputItemsList.Append($"{item.LabelCap} x{moved}");
                }
            }

            // 2. 显示发送结果消息
            string message = BuildTransferMessage(inputItemsCount, outputItemsCount, 
                inputItemsList.ToString(), outputItemsList.ToString());
            Messages.Message(message, this.parent, MessageTypeDefOf.PositiveEvent);

            // 3. 清空容器，防止物品掉落
            transporter.innerContainer.ClearAndDestroyContents();

            // 4. 调用基类的发射方法，让它处理动画和销毁
            base.TryLaunch(this.parent.Map.Tile, null);
        }

        // 判断物品是否应该发送到输出存储器
        private bool ShouldGoToOutputStorage(Thing item)
        {
            // 武器
            if (item.def.IsWeapon)
                return true;

            // 装备
            if (item.def.IsApparel)
                return true;

            // 其他物品发送到输入存储器
            return false;
        }

        // 构建转移消息
        private string BuildTransferMessage(int inputCount, int outputCount, 
            string inputList, string outputList)
        {
            StringBuilder message = new StringBuilder();

            if (inputCount > 0 && outputCount > 0)
            {
                // 既有输入又有输出物品
                message.Append("WULA_ItemsSentToBothStorages".Translate(inputCount, outputCount));
                if (!string.IsNullOrEmpty(inputList))
                {
                    message.Append("\n").Append("WULA_InputStorageItems".Translate(inputList));
                }
                if (!string.IsNullOrEmpty(outputList))
                {
                    message.Append("\n").Append("WULA_OutputStorageItems".Translate(outputList));
                }
            }
            else if (inputCount > 0)
            {
                // 只有输入物品
                message.Append("WULA_ItemsSentToInputStorage".Translate(inputCount));
                if (!string.IsNullOrEmpty(inputList))
                {
                    message.Append(": ").Append(inputList);
                }
            }
            else if (outputCount > 0)
            {
                // 只有输出物品
                message.Append("WULA_ItemsSentToOutputStorage".Translate(outputCount));
                if (!string.IsNullOrEmpty(outputList))
                {
                    message.Append(": ").Append(outputList);
                }
            }
            else
            {
                // 没有任何物品
                message.Append("WULA_NoItemsProcessed".Translate());
            }

            return message.ToString();
        }
    }
}
