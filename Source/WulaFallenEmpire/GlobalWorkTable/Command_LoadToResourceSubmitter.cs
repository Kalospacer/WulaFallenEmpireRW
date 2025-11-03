using RimWorld;
using UnityEngine;
using Verse;

namespace WulaFallenEmpire
{
    public class Command_LoadToResourceSubmitter : Command
    {
        public CompResourceSubmitter submitterComp;
        
        public override void ProcessInput(Event ev)
        {
            base.ProcessInput(ev);
            
            if (submitterComp?.parent == null) return;
            
            // 打开装载界面，类似运输舱的界面
            Find.WindowStack.Add(new Dialog_LoadResourceSubmitter(submitterComp));
        }
    }
}
