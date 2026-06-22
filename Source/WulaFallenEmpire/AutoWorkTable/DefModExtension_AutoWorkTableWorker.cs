using Verse;

namespace WulaFallenEmpire.AutoWorkTable
{
    // 自动工作台的“虚拟工人”参数。
    // 这些值用于把 realWorkAmount（总工作量）换算成每 tick 消耗量。
    public sealed class DefModExtension_AutoWorkTableWorker : DefModExtension
    {
        public float workerBaseSpeed = 1f;
        public int skillLevel = 10;
        public float workSpeedGlobal = 1f;
    }
}
