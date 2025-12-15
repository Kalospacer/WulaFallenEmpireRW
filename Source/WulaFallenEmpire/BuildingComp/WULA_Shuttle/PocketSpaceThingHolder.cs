using System;
using System.Collections.Generic;
using Verse;

namespace WulaFallenEmpire
{
    /// <summary>
    /// 用于武装穿梭机口袋空间的IThingHolder实现，与CompTransporter的容器分离
    /// </summary>
    public class PocketSpaceThingHolder : IThingHolder, IExposable
    {
        /// <summary>持有的物品容器</summary>
        public ThingOwner<Thing> innerContainer;
    
        /// <summary>该容器的拥有者（通常是Building_ArmedShuttleWithPocket）</summary>
        private IThingHolder owner;
    
        /// <summary>实现IThingHolder.ParentHolder属性</summary>
        public IThingHolder ParentHolder => owner;
    
        public PocketSpaceThingHolder()
        {
            innerContainer = new ThingOwner<Thing>(this);
        }

        public PocketSpaceThingHolder(IThingHolder owner) : this()
        {
            this.owner = owner;
        }

        /// <summary>
        /// 获取直接持有的物品
        /// </summary>
        public ThingOwner GetDirectlyHeldThings()
        {
            return innerContainer;
        }

        /// <summary>
        /// 获取子持有者
        /// </summary>
        public void GetChildHolders(List<IThingHolder> outChildren)
        {
            // 目前没有子持有者，留空
        }

        /// <summary>
        /// 通知物品被添加
        /// </summary>
        public void Notify_ThingAdded(Thing t)
        {
            // 这里可以添加逻辑来处理物品被添加到口袋空间的情况
            WulaLog.Debug($"[WULA] Item {t.LabelCap} added to pocket space container.");
        }

        /// <summary>
        /// 通知物品被移除
        /// </summary>
        public void Notify_ThingRemoved(Thing t)
        {
            // 这里可以添加逻辑来处理物品被从口袋空间移除的情况
            WulaLog.Debug($"[WULA] Item {t.LabelCap} removed from pocket space container.");
        }

        public void ExposeData()
        {
            Scribe_Deep.Look(ref innerContainer, "innerContainer", this);
            // owner 通常在构造函数中设置，不需要序列化
        }
    }
}