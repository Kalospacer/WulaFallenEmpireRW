using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using RimWorld;
using UnityEngine;
using Verse;
using WulaFallenEmpire.EventSystem.AI.Tools;

namespace WulaFallenEmpire.EventSystem.AI
{
    public class MapComponent_AIOverwatch : MapComponent
    {
        private bool enabled = false;
        private int durationTicks = 0;
        private int tickCounter = 0;
        private int checkInterval = 180; // Check every 3 seconds (180 ticks)

        // Configurable cooldown to prevent spamming too many simultaneous strikes
        private int globalCooldownTicks = 0;

        public bool IsEnabled => enabled;
        public int DurationTicks => durationTicks;

        public MapComponent_AIOverwatch(Map map) : base(map)
        {
        }

        // useArtilleryVersion: false = WULA_MotherShip (normal), true = WULA_MotherShip_Planet_Interdiction (artillery)
        public void EnableOverwatch(int durationSeconds, bool useArtilleryVersion = false)
        {
            if (this.enabled)
            {
                 Messages.Message("WULA_AIOverwatch_AlreadyActive".Translate(this.durationTicks / 60), MessageTypeDefOf.RejectInput);
                 return;
            }

            // Hard limit: 3 minutes (180 seconds)
            int clampedDuration = Math.Min(durationSeconds, 180);

            this.enabled = true;
            this.durationTicks = clampedDuration * 60;
            this.tickCounter = 0;
            this.globalCooldownTicks = 0;

            // Call fleet when overwatch starts
            TryCallFleet(useArtilleryVersion);

            Messages.Message("WULA_AIOverwatch_Engaged".Translate(clampedDuration), MessageTypeDefOf.PositiveEvent);
        }

        public void DisableOverwatch()
        {
            this.enabled = false;
            this.durationTicks = 0;

            // Clear flight path when overwatch ends
            TryClearFlightPath();

            Messages.Message("WULA_AIOverwatch_Disengaged".Translate(), MessageTypeDefOf.NeutralEvent);
        }

        private void TryCallFleet(bool useArtilleryVersion)
        {
            try
            {
                // Choose mothership version based on parameter
                string defName = useArtilleryVersion ? "WULA_MotherShip_Planet_Interdiction" : "WULA_MotherShip";
                var flyOverDef = DefDatabase<ThingDef>.GetNamedSilentFail(defName);
                if (flyOverDef == null)
                {
                    WulaLog.Debug($"[AI Overwatch] Could not find {defName} ThingDef.");
                    return;
                }

                // Calculate proper start and end positions (edge to opposite edge)
                IntVec3 startPos = GetRandomMapEdgePosition(map);
                IntVec3 endPos = GetOppositeMapEdgePosition(map, startPos);

                // Use the proper FlyOver.MakeFlyOver static method
                FlyOver flyOver = FlyOver.MakeFlyOver(
                    flyOverDef,
                    startPos,
                    endPos,
                    map,
                    speed: useArtilleryVersion ? 0.02f : 0.01f,  // Artillery version slower
                    height: 20f
                );

                if (flyOver != null)
                {
                    Messages.Message("WULA_AIOverwatch_FleetCalled".Translate(), MessageTypeDefOf.PositiveEvent);
                    WulaLog.Debug($"[AI Overwatch] Called fleet: {defName} spawned from {startPos} to {endPos}.");
                }
            }
            catch (Exception ex)
            {
                WulaLog.Debug($"[AI Overwatch] Failed to call fleet: {ex.Message}");
            }
        }

        private IntVec3 GetRandomMapEdgePosition(Map map)
        {
            int edge = Rand.Range(0, 4);
            int x, z;

            switch (edge)
            {
                case 0: // Bottom
                    x = Rand.Range(5, map.Size.x - 5);
                    z = 0;
                    break;
                case 1: // Right
                    x = map.Size.x - 1;
                    z = Rand.Range(5, map.Size.z - 5);
                    break;
                case 2: // Top
                    x = Rand.Range(5, map.Size.x - 5);
                    z = map.Size.z - 1;
                    break;
                case 3: // Left
                default:
                    x = 0;
                    z = Rand.Range(5, map.Size.z - 5);
                    break;
            }

            return new IntVec3(x, 0, z);
        }

        private IntVec3 GetOppositeMapEdgePosition(Map map, IntVec3 startPos)
        {
            // Calculate direction from start to center, then extend to opposite edge
            IntVec3 center = map.Center;
            Vector3 toCenter = (center.ToVector3() - startPos.ToVector3()).normalized;
            
            // Extend to the opposite edge
            float maxDistance = Mathf.Max(map.Size.x, map.Size.z) * 1.5f;
            Vector3 endVec = startPos.ToVector3() + toCenter * maxDistance;
            
            // Clamp to map bounds
            int endX = Mathf.Clamp((int)endVec.x, 0, map.Size.x - 1);
            int endZ = Mathf.Clamp((int)endVec.z, 0, map.Size.z - 1);
            
            return new IntVec3(endX, 0, endZ);
        }

        private void TryClearFlightPath()
        {
            try
            {
                // Find all FlyOver entities on the map and use EmergencyDestroy for smooth exit
                var flyOvers = map.listerThings.AllThings
                    .Where(t => t is FlyOver)
                    .Cast<FlyOver>()
                    .ToList();

                foreach (var flyOver in flyOvers)
                {
                    if (!flyOver.Destroyed)
                    {
                        flyOver.EmergencyDestroy(); // Use smooth accelerated exit instead of instant destroy
                    }
                }

                if (flyOvers.Count > 0)
                {
                    Messages.Message("WULA_AIOverwatch_FleetCleared".Translate(), MessageTypeDefOf.NeutralEvent);
                    WulaLog.Debug($"[AI Overwatch] Cleared flight path: {flyOvers.Count} entities set to emergency exit.");
                }
            }
            catch (Exception ex)
            {
                WulaLog.Debug($"[AI Overwatch] Failed to clear flight path: {ex.Message}");
            }
        }

        public override void MapComponentTick()
        {
            base.MapComponentTick();
            if (!enabled) return;

            durationTicks--;
            if (durationTicks <= 0)
            {
                DisableOverwatch();
                return;
            }

            if (globalCooldownTicks > 0)
            {
                globalCooldownTicks--;
            }

            tickCounter++;
            if (tickCounter >= checkInterval)
            {
                tickCounter = 0;
                
                // Optional: Notify user every 30 seconds (1800 ticks) that system is still active
                if ((durationTicks % 1800) < checkInterval)
                {
                    Messages.Message("WULA_AIOverwatch_SystemActive".Translate(durationTicks / 60), MessageTypeDefOf.NeutralEvent);
                }

                PerformScanAndStrike();
            }
        }

        private void PerformScanAndStrike()
        {
            // Gather all valid hostile pawn targets
            List<Pawn> hostilePawns = map.mapPawns.AllPawnsSpawned
                .Where(p => !p.Dead && !p.Downed && p.HostileTo(Faction.OfPlayer) && !p.IsPrisoner)
                .ToList();

            // Gather all hostile buildings (turrets, etc.)
            List<Building> hostileBuildings = map.listerBuildings.allBuildingsColonist
                .Concat(map.listerThings.ThingsInGroup(ThingRequestGroup.BuildingArtificial).OfType<Building>())
                .Where(b => b != null && !b.Destroyed && b.Faction != null && b.Faction.HostileTo(Faction.OfPlayer))
                .Distinct()
                .ToList();

            // Convert building positions to "virtual targets" for processing
            List<IntVec3> buildingTargets = hostileBuildings.Select(b => b.Position).ToList();

            _strikesThisScan = 0;

            // Process hostile pawns first (clustered)
            if (hostilePawns.Count > 0)
            {
                var clusters = ClusterPawns(hostilePawns, 12f);
                clusters.Sort((a, b) => b.Count.CompareTo(a.Count));

                foreach (var cluster in clusters)
                {
                    if (globalCooldownTicks > 0) break;
                    if (_strikesThisScan >= 3) break;
                    ProcessCluster(cluster);
                }
            }

            // Process hostile buildings (each as individual target)
            foreach (var buildingPos in buildingTargets)
            {
                if (globalCooldownTicks > 0) break;
                if (_strikesThisScan >= 3) break;
                ProcessBuildingTarget(buildingPos);
            }
        }

        private void ProcessBuildingTarget(IntVec3 target)
        {
            if (!target.InBounds(map)) return;

            float safetyRadius = 9.9f; // Medium safety for building strikes
            if (IsFriendlyFireRisk(target, safetyRadius))
            {
                Messages.Message("WULA_AIOverwatch_FriendlyFireAbort".Translate(target.ToString()), new TargetInfo(target, map), MessageTypeDefOf.CautionInput);
                return;
            }

            // Use cannon salvo for buildings (good balance of damage and precision)
            var cannonDef = DefDatabase<AbilityDef>.GetNamedSilentFail("WULA_Firepower_Cannon_Salvo");
            if (cannonDef != null)
            {
                Messages.Message("WULA_AIOverwatch_EngagingBuilding".Translate(), new TargetInfo(target, map), MessageTypeDefOf.PositiveEvent);
                WulaLog.Debug($"[AI Overwatch] Engaging hostile building at {target} with Cannon Salvo.");
                FireAbility(cannonDef, target, Rand.Range(0, 360));
                _strikesThisScan++;
            }
        }
        
        private int _strikesThisScan = 0;

        private void ProcessCluster(List<Pawn> cluster)
        {
            if (cluster.Count == 0) return;
            if (_strikesThisScan >= 3) return; // Self-limit

            // Calculate center of mass
            float x = 0, z = 0;
            foreach (var p in cluster)
            {
                x += p.Position.x;
                z += p.Position.z;
            }
            IntVec3 center = new IntVec3((int)(x / cluster.Count), 0, (int)(z / cluster.Count));

            if (!center.InBounds(map)) return;

            float angle = Rand.Range(0, 360);

            // NEW Decision Logic:
            // >= 20: Heavy (Energy Lance + Primary Cannon together)
            // >= 10: Energy Lance only
            // >= 3: Cannon Salvo (Medium)
            // < 3: Minigun (Light)

            if (cluster.Count >= 20)
            {
                // Ultra Heavy: Fire BOTH Energy Lance AND Primary Cannon
                float safetyRadius = 18.9f;
                if (IsFriendlyFireRisk(center, safetyRadius))
                {
                    Messages.Message("WULA_AIOverwatch_FriendlyFireAbort".Translate(center.ToString()), new TargetInfo(center, map), MessageTypeDefOf.CautionInput);
                    return;
                }

                var lanceDef = DefDatabase<AbilityDef>.GetNamedSilentFail("WULA_Firepower_EnergyLance_Strafe");
                var cannonDef = DefDatabase<AbilityDef>.GetNamedSilentFail("WULA_Firepower_Primary_Cannon_Strafe");

                Messages.Message("WULA_AIOverwatch_MassiveCluster".Translate(cluster.Count), new TargetInfo(center, map), MessageTypeDefOf.ThreatBig);
                WulaLog.Debug($"[AI Overwatch] MASSIVE cluster ({cluster.Count}), executing combined strike at {center}.");

                if (lanceDef != null) FireAbility(lanceDef, center, angle);
                if (cannonDef != null) FireAbility(cannonDef, center, angle + 45f); // Offset angle for variety

                _strikesThisScan++;
                return;
            }

            if (cluster.Count >= 10)
            {
                // Heavy: Energy Lance only
                float safetyRadius = 16.9f;
                if (IsFriendlyFireRisk(center, safetyRadius))
                {
                    Messages.Message("WULA_AIOverwatch_FriendlyFireAbort".Translate(center.ToString()), new TargetInfo(center, map), MessageTypeDefOf.CautionInput);
                    return;
                }

                var lanceDef = DefDatabase<AbilityDef>.GetNamedSilentFail("WULA_Firepower_EnergyLance_Strafe");
                if (lanceDef != null)
                {
                    Messages.Message("WULA_AIOverwatch_EngagingLance".Translate(cluster.Count), new TargetInfo(center, map), MessageTypeDefOf.PositiveEvent);
                    WulaLog.Debug($"[AI Overwatch] Engaging {cluster.Count} hostiles with Energy Lance at {center}.");
                    FireAbility(lanceDef, center, angle);
                    _strikesThisScan++;
                }
                return;
            }

            if (cluster.Count >= 3)
            {
                // Medium: Cannon Salvo
                float safetyRadius = 9.9f;
                if (IsFriendlyFireRisk(center, safetyRadius))
                {
                    Messages.Message("WULA_AIOverwatch_FriendlyFireAbort".Translate(center.ToString()), new TargetInfo(center, map), MessageTypeDefOf.CautionInput);
                    return;
                }

                var cannonDef = DefDatabase<AbilityDef>.GetNamedSilentFail("WULA_Firepower_Cannon_Salvo");
                if (cannonDef != null)
                {
                    Messages.Message("WULA_AIOverwatch_EngagingCannon".Translate(cluster.Count), new TargetInfo(center, map), MessageTypeDefOf.PositiveEvent);
                    WulaLog.Debug($"[AI Overwatch] Engaging {cluster.Count} hostiles with Cannon Salvo at {center}.");
                    FireAbility(cannonDef, center, angle);
                    _strikesThisScan++;
                }
                return;
            }

            // Light: Minigun Strafe
            {
                float safetyRadius = 5.9f;
                if (IsFriendlyFireRisk(center, safetyRadius))
                {
                    Messages.Message("WULA_AIOverwatch_FriendlyFireAbort".Translate(center.ToString()), new TargetInfo(center, map), MessageTypeDefOf.CautionInput);
                    return;
                }

                var minigunDef = DefDatabase<AbilityDef>.GetNamedSilentFail("WULA_Firepower_Minigun_Strafe");
                if (minigunDef != null)
                {
                    Messages.Message("WULA_AIOverwatch_EngagingMinigun".Translate(cluster.Count), new TargetInfo(center, map), MessageTypeDefOf.PositiveEvent);
                    WulaLog.Debug($"[AI Overwatch] Engaging {cluster.Count} hostiles with Minigun Strafe at {center}.");
                    FireAbility(minigunDef, center, angle);
                    _strikesThisScan++;
                }
            }
        }

        private void FireAbility(AbilityDef ability, IntVec3 target, float angle)
        {
            // Route via BombardmentUtility
            // We need to check components again to know which method to call
            
            var circular = ability.comps?.OfType<CompProperties_AbilityCircularBombardment>().FirstOrDefault();
            if (circular != null)
            {
                BombardmentUtility.ExecuteCircularBombardment(map, target, ability, circular);
                return;
            }

            var bombard = ability.comps?.OfType<CompProperties_AbilityBombardment>().FirstOrDefault();
            if (bombard != null)
            {
                BombardmentUtility.ExecuteStrafeBombardmentDirect(map, target, ability, bombard, angle);
                return;
            }

            var lance = ability.comps?.OfType<CompProperties_AbilityEnergyLance>().FirstOrDefault();
            if (lance != null)
            {
                BombardmentUtility.ExecuteEnergyLanceDirect(map, target, ability, lance, angle);
                return;
            }
        }

        private bool IsFriendlyFireRisk(IntVec3 center, float radius)
        {
            var pawns = map.mapPawns.AllPawnsSpawned;
            foreach (var p in pawns)
            {
                if (p.Faction == Faction.OfPlayer || p.IsPrisonerOfColony)
                {
                    if (p.Position.InHorDistOf(center, radius)) return true;
                }
            }
            return false;
        }

        private List<List<Pawn>> ClusterPawns(List<Pawn> pawns, float radius)
        {
            var clusters = new List<List<Pawn>>();
            var assigned = new HashSet<Pawn>();

            foreach (var p in pawns)
            {
                if (assigned.Contains(p)) continue;
                
                var newCluster = new List<Pawn> { p };
                assigned.Add(p);

                // Find neighbors
                foreach (var neighbor in pawns)
                {
                    if (assigned.Contains(neighbor)) continue;
                    if (p.Position.InHorDistOf(neighbor.Position, radius))
                    {
                        newCluster.Add(neighbor);
                        assigned.Add(neighbor);
                    }
                }
                clusters.Add(newCluster);
            }
            return clusters;
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref enabled, "enabled", false);
            Scribe_Values.Look(ref durationTicks, "durationTicks", 0);
            Scribe_Values.Look(ref tickCounter, "tickCounter", 0);
            Scribe_Values.Look(ref globalCooldownTicks, "globalCooldownTicks", 0);
        }
    }
}
