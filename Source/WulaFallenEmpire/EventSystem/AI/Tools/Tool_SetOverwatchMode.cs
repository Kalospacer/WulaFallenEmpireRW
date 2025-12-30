using System;
using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace WulaFallenEmpire.EventSystem.AI.Tools
{
    public class Tool_SetOverwatchMode : AITool
    {
        public override string Name => "set_overwatch_mode";
        public override string Description => "Enables or disables the AI Overwatch Combat Protocol. When enabled (enabled=true), the AI will autonomously scan for hostile targets every few seconds and launch appropriate orbital bombardments for a set duration. When disabled (enabled=false), it immediately stops any active overwatch and clears the flight path. Use enabled=false to stop overwatch early if the player requests it.";
        public override string UsageSchema => "{\"enabled\":true,\"durationSeconds\":60}";

        public override string Execute(string args)
        {
            var parsed = ParseJsonArgs(args);
            
            bool enabled = true;
            if (TryGetBool(parsed, "enabled", out bool e)) enabled = e;

            int duration = 60;
            if (TryGetInt(parsed, "durationSeconds", out int d)) duration = d;

            Map map = Find.CurrentMap;
            if (map == null) return "Error: No active map.";

            var overwatch = map.GetComponent<MapComponent_AIOverwatch>();
            if (overwatch == null)
            {
                overwatch = new MapComponent_AIOverwatch(map);
                map.components.Add(overwatch);
            }

            if (enabled)
            {
                overwatch.EnableOverwatch(duration);
                return $"Success: AI Overwatch Protocol ENABLED for {duration} seconds. Hostiles will be engaged automatically.";
            }
            else
            {
                overwatch.DisableOverwatch();
                return "Success: AI Overwatch Protocol DISABLED.";
            }
        }
    }
}
