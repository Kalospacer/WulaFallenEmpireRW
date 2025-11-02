using RimWorld;
using Verse;
using System.Linq;
using UnityEngine;
using System.Reflection; // For InnerThing reflection if needed, but innerContainer is directly accessible

namespace WulaFallenEmpire
{
    // ArmedShuttleIncoming now directly implements the logic from PassengerShuttleIncoming
    // It should inherit from ShuttleIncoming, as PassengerShuttleIncoming does.
    public class ArmedShuttleIncoming : ShuttleIncoming // Changed from PassengerShuttleIncoming
    {
        private static readonly SimpleCurve AngleCurve = new SimpleCurve
        {
            new CurvePoint(0f, 30f),
            new CurvePoint(1f, 0f)
        };

        // innerContainer is a protected field in Skyfaller, accessible to derived classes like ShuttleIncoming
        // So we can directly use innerContainer here.
        public Building_ArmedShuttle Shuttle => (Building_ArmedShuttle)innerContainer.FirstOrDefault();

        public override Color DrawColor => Shuttle.DrawColor;

        protected override void Impact()
        {
            // Re-adding debug logs for stage 6
            Log.Message($"[WULA] Stage 6: Impact - ArmedShuttleIncoming Impact() called. InnerThing (via innerContainer) is: {innerContainer.FirstOrDefault()?.ToString() ?? "NULL"}");
            
            Thing innerThing = innerContainer.FirstOrDefault();
            if (innerThing is Building_ArmedShuttle shuttle)
            {
                Log.Message("[WULA] Stage 6: Impact - InnerThing is a Building_ArmedShuttle. Attempting to notify arrival.");
                shuttle.TryGetComp<CompLaunchable>()?.Notify_Arrived();
            }
            else
            {
                Log.Warning($"[WULA] Stage 6: Impact - InnerThing is NOT a Building_ArmedShuttle or is NULL. Type: {innerThing?.GetType().Name ?? "NULL"}. This is the cause of the issue.");
            }
            
            // Calling base.Impact() will handle the actual spawning of the innerThing.
            // This is crucial for "unpacking" the shuttle.
            base.Impact(); 
        }

        public override void SpawnSetup(Map map, bool respawningAfterLoad)
        {
            base.SpawnSetup(map, respawningAfterLoad);
            // Re-adding debug logs for stage 5
            Log.Message($"[WULA] Stage 5: Landing Sequence - ArmedShuttleIncoming spawned. InnerThing (via innerContainer) is: {innerContainer.FirstOrDefault()?.ToString() ?? "NULL"}");
            if (!respawningAfterLoad && !base.BeingTransportedOnGravship)
            {
                angle = GetAngle(0f, base.Rotation);
            }
        }

        public override void Destroy(DestroyMode mode = DestroyMode.Vanish)
        {
            if (!hasImpacted)
            {
                Log.Error("Destroying armed shuttle skyfaller without ever having impacted"); // Changed log message
            }
            base.Destroy(mode);
        }

        protected override void GetDrawPositionAndRotation(ref Vector3 drawLoc, out float extraRotation)
        {
            extraRotation = 0f;
            angle = GetAngle(base.TimeInAnimation, base.Rotation);
            switch (base.Rotation.AsInt)
            {
            case 1:
                extraRotation += def.skyfaller.rotationCurve.Evaluate(base.TimeInAnimation);
                break;
            case 3:
                extraRotation -= def.skyfaller.rotationCurve.Evaluate(base.TimeInAnimation);
                break;
            }
            drawLoc.z += def.skyfaller.zPositionCurve.Evaluate(base.TimeInAnimation);
        }

        public override float DrawAngle()
        {
            float num = 0f;
            switch (base.Rotation.AsInt)
            {
            case 1:
                num += def.skyfaller.rotationCurve.Evaluate(base.TimeInAnimation);
                break;
            case 3:
                num -= def.skyfaller.rotationCurve.Evaluate(base.TimeInAnimation);
                break;
            }
            return num;
        }

        private static float GetAngle(float timeInAnimation, Rot4 rotation)
        {
            return rotation.AsInt switch
            {
                1 => rotation.Opposite.AsAngle + AngleCurve.Evaluate(timeInAnimation), 
                3 => rotation.Opposite.AsAngle - AngleCurve.Evaluate(timeInAnimation), 
                _ => rotation.Opposite.AsAngle, 
            };
        }
    }
}