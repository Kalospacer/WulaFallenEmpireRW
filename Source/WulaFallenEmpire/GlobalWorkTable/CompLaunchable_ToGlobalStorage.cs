using RimWorld;
using System.Collections.Generic;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace WulaFallenEmpire
{
    public class CompLaunchable_ToGlobalStorage : CompLaunchable_TransportPod
    {
        public new CompProperties_Launchable_ToGlobalStorage Props => (CompProperties_Launchable_ToGlobalStorage)this.props;

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

            // 1. 将物品转移到全局存储
            foreach (Thing item in transporter.innerContainer)
            {
                globalStorage.AddToInputStorage(item.def, item.stackCount);
            }
            Messages.Message("WULA_ItemsSentToGlobalStorage".Translate(transporter.innerContainer.ContentsString), this.parent, MessageTypeDefOf.PositiveEvent);

            // 2. 清空容器，防止物品掉落
            transporter.innerContainer.ClearAndDestroyContents();

            // 3. 调用基类的发射方法，让它处理动画和销毁
            // 我们给一个无效的目标和空的到达动作，让它飞出地图后就消失
            base.TryLaunch(this.parent.Map.Tile, null);
        }
    }
}