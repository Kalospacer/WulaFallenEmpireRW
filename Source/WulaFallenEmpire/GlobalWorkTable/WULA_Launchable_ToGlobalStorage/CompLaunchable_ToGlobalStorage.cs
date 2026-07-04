using RimWorld;
using System.Collections.Generic;
using UnityEngine;
using Verse;
using Verse.Sound;
using System.Text;
using System.Linq;
using WulaFallenEmpire.EventSystem.AI;

namespace WulaFallenEmpire
{
    public class CompLaunchable_ToGlobalStorage : CompLaunchable_TransportPod
    {
        public new CompProperties_Launchable_ToGlobalStorage Props => (CompProperties_Launchable_ToGlobalStorage)this.props;

        // 获取垃圾屏蔽组件
        public CompGarbageShield GarbageShieldComp => this.parent.GetComp<CompGarbageShield>();

        public override IEnumerable<Gizmo> CompGetGizmosExtra()
        {
            // 不继承基类的任何 gizmo：原版发射按钮允许把货物发射到世界任意位置，
            // 无人驾驶时货物会按原版语义直接丢失（TransportPodsContentsWillBeLost）。
            // 此前按 defaultDesc 翻译串过滤原版按钮的方式不可靠，两个按钮同图标并存，
            // 玩家极易点错。本建筑只允许发射到全局存储，因此完整自建 gizmo。
            if (this.Transporter.LoadingInProgressOrReadyToLaunch)
            {
                Command_Action command = new Command_Action();
                command.defaultLabel = "WULA_LaunchToGlobalStorage".Translate();
                command.defaultDesc = "WULA_LaunchToGlobalStorageDesc".Translate();
                command.icon = ContentFinder<Texture2D>.Get("UI/Commands/LaunchShip");
                command.alsoClickIfOtherInGroupClicked = false; // 发射即整组行为，避免多选时重复触发
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
                WulaLog.Debug("Tried to launch " + this.parent + " but it's not spawned.");
                return;
            }

            var globalStorage = Find.World.GetComponent<GlobalStorageWorldComponent>();
            if (globalStorage == null)
            {
                WulaLog.Debug("Could not find GlobalStorageWorldComponent.");
                return;
            }

            Map map = this.parent.Map;

            // 发射是整组行为：一次装载指令编成一组的运输舱（groupID 相同）会一起起飞。
            // 必须把整组的货物都转入全局存储，否则组内其他舱的货物会被发射到别处而"丢失"。
            List<CompTransporter> group = this.TransportersInGroup;
            if (group == null || group.Count == 0)
            {
                group = new List<CompTransporter> { this.Transporter };
            }

            bool anyItems = false;
            foreach (CompTransporter tr in group)
            {
                if (tr != null && tr.innerContainer.Any)
                {
                    anyItems = true;
                    break;
                }
            }
            if (!anyItems)
            {
                Messages.Message("WULA_NoItemsToSendToGlobalStorage".Translate(), this.parent, MessageTypeDefOf.RejectInput);
                return;
            }

            // 检查垃圾屏蔽 - 如果启用了垃圾屏蔽并且组内有禁止物品，取消发射
            if (GarbageShieldComp != null && GarbageShieldComp.GarbageShieldEnabled)
            {
                List<Thing> forbiddenItems = new List<Thing>();
                foreach (CompTransporter tr in group)
                {
                    if (tr == null) continue;
                    forbiddenItems.AddRange(GarbageShieldComp.GetForbiddenItems(tr.innerContainer));
                }
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

            // 1. 将整组的物品分类转移到相应的存储（按实际转移数量计数）
            foreach (CompTransporter tr in group)
            {
                if (tr == null) continue;

                foreach (Thing item in tr.innerContainer.ToList())
                {
                    bool toOutput = ShouldGoToOutputStorage(item);
                    ThingOwner destination = toOutput ? globalStorage.outputContainer : globalStorage.inputContainer;

                    string label = item.LabelCap; // 转移合并时源物品可能被销毁，先取标签
                    int moved = tr.innerContainer.TryTransferToContainer(item, destination, item.stackCount, true);
                    if (moved <= 0)
                    {
                        WulaLog.Debug($"Failed to transfer {label} to global storage; it will be dropped on the ground.");
                        continue;
                    }

                    if (toOutput)
                    {
                        outputItemsCount += moved;
                        if (outputItemsList.Length > 0) outputItemsList.Append(", ");
                        outputItemsList.Append($"{label} x{moved}");
                    }
                    else
                    {
                        inputItemsCount += moved;
                        if (inputItemsList.Length > 0) inputItemsList.Append(", ");
                        inputItemsList.Append($"{label} x{moved}");
                    }
                }

                // 2. 转移失败的残留物品掉回地面，绝不静默销毁
                if (tr.innerContainer.Any)
                {
                    tr.innerContainer.TryDropAll(tr.parent.Position, map, ThingPlaceMode.Near);
                }
            }

            // 3. 显示发送结果消息
            string message = BuildTransferMessage(inputItemsCount, outputItemsCount,
                inputItemsList.ToString(), outputItemsList.ToString());
            Messages.Message(message, this.parent, MessageTypeDefOf.PositiveEvent);
            SendTransferAutoCommentary(map, group, inputItemsCount, outputItemsCount,
                inputItemsList.ToString(), outputItemsList.ToString());

            // 4. 播放发射动画并销毁整组运输舱。
            // 货物已直接进入全局存储，天降物只是视觉效果：createWorldObject = false，
            // 否则空舱会作为 TravellingTransporters 世界物体飞回本地图并坠落在随机位置。
            this.Transporter.TryRemoveLord(map);
            foreach (CompTransporter tr in group)
            {
                if (tr == null || tr.parent == null || !tr.parent.Spawned) continue;

                ActiveTransporter activeTransporter = (ActiveTransporter)ThingMaker.MakeThing(Props.activeTransporterDef ?? ThingDefOf.ActiveDropPod);
                activeTransporter.Contents = new ActiveTransporterInfo();
                activeTransporter.Rotation = tr.parent.Rotation;

                FlyShipLeaving flyShipLeaving = (FlyShipLeaving)SkyfallerMaker.MakeSkyfaller(Props.skyfallerLeaving ?? ThingDefOf.DropPodLeaving, activeTransporter);
                flyShipLeaving.createWorldObject = false;

                IntVec3 position = tr.parent.Position;
                tr.CleanUpLoadingVars(map);
                tr.parent.Destroy();
                GenSpawn.Spawn(flyShipLeaving, position, map);
            }
        }

        private void SendTransferAutoCommentary(Map map, List<CompTransporter> group,
            int inputItemsCount, int outputItemsCount, string inputList, string outputList)
        {
            try
            {
                StringBuilder details = new StringBuilder();
                details.AppendLine("乌拉帝国物资运输舱已将货物发送到舰队/全局仓储。");
                details.AppendLine($"地图: {map?.Parent?.LabelCap ?? map?.ToString() ?? "Unknown"}");
                details.AppendLine($"运输舱数量: {Mathf.Max(1, group?.Count ?? 1)}");
                details.AppendLine($"总发送数量: {inputItemsCount + outputItemsCount}");

                if (inputItemsCount > 0)
                {
                    details.AppendLine($"输入仓物资 ({inputItemsCount}): {inputList}");
                }
                if (outputItemsCount > 0)
                {
                    details.AppendLine($"输出仓/装备武器 ({outputItemsCount}): {outputList}");
                }
                if (inputItemsCount <= 0 && outputItemsCount <= 0)
                {
                    details.AppendLine("没有物资成功转移。");
                }

                AIAutoCommentary.ProcessEvent(
                    "乌拉帝国物资运输舱发送到舰队",
                    "WULA_TransportPodsSentToFleet",
                    details.ToString());
            }
            catch (System.Exception ex)
            {
                WulaLog.Debug($"[AI Commentary] Failed to send transport pod transfer event: {ex}");
            }
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
