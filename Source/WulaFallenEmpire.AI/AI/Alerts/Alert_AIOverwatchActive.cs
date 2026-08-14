using System;
using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;
using WulaFallenEmpire.EventSystem.AI;

namespace WulaFallenEmpire.EventSystem.AI.Alerts
{
    public class Alert_AIOverwatchActive : Alert_Critical
    {
        public Alert_AIOverwatchActive()
        {
            this.defaultLabel = "WULA_AIOverwatch_Label".Translate();
            this.defaultExplanation = "WULA_AIOverwatch_Desc".Translate(0);
        }

        public override AlertReport GetReport()
        {
            var map = Find.CurrentMap;
            if (map == null) return AlertReport.Inactive;

            var comp = map.GetComponent<MapComponent_AIOverwatch>();
            if (comp != null && comp.IsEnabled)
            {
                return AlertReport.Active;
            }

            return AlertReport.Inactive;
        }

        public override string GetLabel()
        {
            var map = Find.CurrentMap;
            if (map != null)
            {
                var comp = map.GetComponent<MapComponent_AIOverwatch>();
                if (comp != null && comp.IsEnabled)
                {
                    int secondsLeft = comp.DurationTicks / 60;
                    return "WULA_AIOverwatch_Label".Translate() + $"\n({secondsLeft}s)";
                }
            }
            return "WULA_AIOverwatch_Label".Translate();
        }
        
        public override TaggedString GetExplanation()
        {
             var map = Find.CurrentMap;
             if (map != null)
             {
                 var comp = map.GetComponent<MapComponent_AIOverwatch>();
                 if (comp != null && comp.IsEnabled)
                 {
                     return "WULA_AIOverwatch_Desc".Translate(comp.DurationTicks / 60);
                 }
             }
             return base.GetExplanation();
        }
    }
}
