using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Verse;

namespace WulaFallenEmpire
{
    [StaticConstructorOnStartup]
    public static class WulaBeamUtility
    {
        private static readonly Material BeamMaterial = MaterialPool.MatFrom(GenDraw.LineTexPath, ShaderDatabase.Transparent, Color.white);

        // A more advanced method to get all cells in a rectangular area
        public static IEnumerable<IntVec3> GetCellsInBeamArea(IntVec3 start, IntVec3 end, int width)
        {
            if (width <= 1)
            {
                return GenGrid.PointsOnLine(start, end).Distinct();
            }

            var beamLine = GenGrid.PointsOnLine(start, end).ToList();
            var allCells = new HashSet<IntVec3>(beamLine);
            var halfWidth = (width - 1) / 2;

            if (halfWidth == 0) return allCells;

            var angle = (end - start).AngleFlat;
            var perpendicularAngle = angle - 90f;

            foreach (var cell in beamLine)
            {
                for (int i = 1; i <= halfWidth; i++)
                {
                    var offset = Vector3.forward.RotatedBy(perpendicularAngle) * i;
                    allCells.Add((cell.ToVector3() + offset).ToIntVec3());
                    allCells.Add((cell.ToVector3() - offset).ToIntVec3());
                }
            }
            return allCells;
        }

        // A shared drawing method
        public static void DrawBeam(Vector3 start, Vector3 end, Color color, float width)
        {
            var material = BeamMaterial;
            if (material.color != color)
            {
                material = MaterialPool.MatFrom(GenDraw.LineTexPath, ShaderDatabase.Transparent, color);
            }
            
            var matrix = default(Matrix4x4);
            var distance = Vector3.Distance(start, end);
            var angle = (end - start).AngleFlat();

            matrix.SetTRS(
                pos: start + (end - start) / 2f,
                q: Quaternion.AngleAxis(angle, Vector3.up),
                s: new Vector3(width, 1f, distance)
            );

            Graphics.DrawMesh(MeshPool.plane10, matrix, material, 0);
        }
    }
}