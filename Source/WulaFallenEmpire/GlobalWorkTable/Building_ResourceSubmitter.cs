using RimWorld;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Verse;
using System;
using System.Text;

namespace WulaFallenEmpire
{
    public class Building_ResourceSubmitter : Building
    {
        public CompPowerTrader powerComp;
        public CompRefuelable refuelableComp;
        public CompFlickable flickableComp;
        public CompResourceSubmitter submitterComp;

        public override void SpawnSetup(Map map, bool respawningAfterLoad)
        {
            base.SpawnSetup(map, respawningAfterLoad);
            powerComp = GetComp<CompPowerTrader>();
            refuelableComp = GetComp<CompRefuelable>();
            flickableComp = GetComp<CompFlickable>();
            submitterComp = GetComp<CompResourceSubmitter>();
        }

        public bool IsOperational
        {
            get
            {
                if (powerComp != null && !powerComp.PowerOn)
                    return false;
                if (refuelableComp != null && !refuelableComp.HasFuel)
                    return false;
                if (flickableComp != null && !flickableComp.SwitchIsOn)
                    return false;
                return true;
            }
        }

        public string GetInoperativeReason()
        {
            if (powerComp != null && !powerComp.PowerOn)
                return "WULA_NoPower".Translate();
            if (refuelableComp != null && !refuelableComp.HasFuel)
                return "WULA_NoFuel".Translate();
            if (flickableComp != null && !flickableComp.SwitchIsOn)
                return "WULA_SwitchOff".Translate();
            return "WULA_UnknownReason".Translate();
        }

        public override string GetInspectString()
        {
            StringBuilder stringBuilder = new StringBuilder();
            
            string baseString = base.GetInspectString();
            if (!baseString.NullOrEmpty())
            {
                stringBuilder.Append(baseString);
            }
            
            if (!IsOperational)
            {
                if (stringBuilder.Length > 0) stringBuilder.AppendLine();
                stringBuilder.Append($"{"WULA_Status".Translate()}: {"WULA_Inoperative".Translate()} - {GetInoperativeReason()}");
            }

            return stringBuilder.ToString();
        }
    }
}
