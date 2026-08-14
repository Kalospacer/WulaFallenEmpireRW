using System;

namespace WulaFallenEmpire
{
    /// <summary>
    /// 主程序集对外暴露的静态事件钩子。AI 等独立程序集在此订阅，
    /// 避免主程序集对它们的编译期引用。
    /// </summary>
    public static class WulaEventHooks
    {
        /// <summary>运输舱发往舰队/全局仓储完成时触发。参数：事件标题、事件 DefName、详情文本。</summary>
        public static event Action<string, string, string> TransportPodsSentToFleet;

        internal static void OnTransportPodsSentToFleet(string eventLabel, string eventDefName, string details)
        {
            TransportPodsSentToFleet?.Invoke(eventLabel, eventDefName, details);
        }
    }
}
