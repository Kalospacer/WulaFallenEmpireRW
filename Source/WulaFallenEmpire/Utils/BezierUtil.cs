using System.Collections.Generic;
using UnityEngine;

namespace WulaFallenEmpire.Utils
{
    public static class BezierUtil
    {
        // Generates points for a quadratic Bezier curve.
        public static List<Vector3> GenerateQuadraticPoints(Vector3 start, Vector3 control, Vector3 end, int segments)
        {
            List<Vector3> points = new List<Vector3>();
            if (segments <= 0) segments = 1;

            for (int i = 0; i <= segments; i++)
            {
                float t = (float)i / segments;
                float u = 1f - t;
                float tt = t * t;
                float uu = u * u;
                
                Vector3 p = uu * start;      // (1-t)^2 * P0
                p += 2 * u * t * control;  // 2(1-t)t * P1
                p += tt * end;             // t^2 * P2
                
                points.Add(p);
            }
            return points;
        }
    }
}