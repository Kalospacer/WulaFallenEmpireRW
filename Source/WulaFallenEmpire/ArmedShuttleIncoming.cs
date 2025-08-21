using RimWorld;
using Verse;
using System.Linq;
using UnityEngine;

namespace WulaFallenEmpire
{
    public class ArmedShuttleIncoming : PassengerShuttleIncoming
    {
        public new Building_ArmedShuttle Shuttle => (Building_ArmedShuttle)base.innerContainer.FirstOrDefault();

        public override Color DrawColor => Shuttle.DrawColor;

        protected override void Impact()
        {
            Shuttle.TryGetComp<CompLaunchable>()?.Notify_Arrived();
            // Do not call base.Impact(), as it leads to the InvalidCastException in the parent class.
            // The base Skyfaller.Tick() will handle the rest of the impact logic after this method returns.
        }
    }
}