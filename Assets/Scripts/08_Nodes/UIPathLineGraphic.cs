using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

[RequireComponent(typeof(RectTransform))]
public class UIPathLineGraphic : Graphic
{
    [Header("Line")]
    [Min(0.5f)] public float thickness = 14f;
    [Range(0f, 1f)] public float alpha = 1f;

    [Header("Smoothing / Rounding")]
    [Tooltip("0 = sharp corners. 2-4 is usually good.")]
    [Range(0, 10)] public int cornerSmoothness = 3;

    [Tooltip("Rounded ends on both ends.")]
    public bool roundCaps = true;

    [Tooltip("Skip points to reduce triangles. 1 = all points, 2 = every other point, etc.")]
    [Min(1)] public int step = 1;

    private readonly List<Vector2> _points = new List<Vector2>();

    /// <summary>
    /// Points must be in the same local/anchored space as the panel (which your sampledPoints are).
    /// </summary>
    public void SetPoints(IReadOnlyList<Vector2> pts)
    {
        _points.Clear();

        if (pts != null)
        {
            int s = Mathf.Max(1, step);
            for (int i = 0; i < pts.Count; i += s)
                _points.Add(pts[i]);
        }

        SetVerticesDirty();
    }

    protected override void OnPopulateMesh(VertexHelper vh)
    {
        vh.Clear();
        if (_points.Count < 2) return;

        // ✅ FIXED alpha (no byte math)
        Color32 col = color;
        col.a = (byte)Mathf.RoundToInt(Mathf.Clamp01(alpha) * 255f);

        float half = thickness * 0.5f;

        // Main segments
        for (int i = 0; i < _points.Count - 1; i++)
        {
            Vector2 p0 = _points[i];
            Vector2 p1 = _points[i + 1];

            Vector2 dir = (p1 - p0);
            float len = dir.magnitude;
            if (len <= 0.0001f) continue;
            dir /= len;

            Vector2 n = new Vector2(-dir.y, dir.x) * half;

            AddQuad(vh, p0 - n, p0 + n, p1 + n, p1 - n, col);
        }

        // Corner smoothing (triangle fan at each joint)
        if (cornerSmoothness > 0)
        {
            for (int i = 1; i < _points.Count - 1; i++)
            {
                Vector2 prev = _points[i - 1];
                Vector2 mid = _points[i];
                Vector2 next = _points[i + 1];

                Vector2 dirA = (mid - prev);
                Vector2 dirB = (next - mid);

                if (dirA.sqrMagnitude < 0.0001f || dirB.sqrMagnitude < 0.0001f)
                    continue;

                dirA.Normalize();
                dirB.Normalize();

                Vector2 nA = new Vector2(-dirA.y, dirA.x) * half;
                Vector2 nB = new Vector2(-dirB.y, dirB.x) * half;

                float cross = dirA.x * dirB.y - dirA.y * dirB.x;

                Vector2 fromN = (cross >= 0f) ? nA : -nA;
                Vector2 toN = (cross >= 0f) ? nB : -nB;

                if ((fromN - toN).sqrMagnitude < 0.0001f)
                    continue;

                AddCornerArc(vh, mid, fromN, toN, cornerSmoothness, col);
            }
        }

        // Rounded caps
        if (roundCaps)
        {
            AddCap(vh, _points[0], _points[1], half, Mathf.Max(3, cornerSmoothness), col, startCap: true);
            AddCap(vh, _points[_points.Count - 1], _points[_points.Count - 2], half, Mathf.Max(3, cornerSmoothness), col, startCap: false);
        }
    }

    // ---------------- helpers ----------------

    private static void AddQuad(VertexHelper vh, Vector2 v0, Vector2 v1, Vector2 v2, Vector2 v3, Color32 col)
    {
        int idx = vh.currentVertCount;
        vh.AddVert(v0, col, Vector2.zero);
        vh.AddVert(v1, col, Vector2.zero);
        vh.AddVert(v2, col, Vector2.zero);
        vh.AddVert(v3, col, Vector2.zero);
        vh.AddTriangle(idx + 0, idx + 1, idx + 2);
        vh.AddTriangle(idx + 2, idx + 3, idx + 0);
    }

    private static void AddCornerArc(VertexHelper vh, Vector2 center, Vector2 fromN, Vector2 toN, int steps, Color32 col)
    {
        float a0 = Mathf.Atan2(fromN.y, fromN.x);
        float a1 = Mathf.Atan2(toN.y, toN.x);

        float delta = Mathf.DeltaAngle(a0 * Mathf.Rad2Deg, a1 * Mathf.Rad2Deg) * Mathf.Deg2Rad;

        int fanStart = vh.currentVertCount;
        vh.AddVert(center, col, Vector2.zero);

        float r = fromN.magnitude;

        for (int i = 0; i <= steps; i++)
        {
            float t = i / (float)steps;
            float a = a0 + delta * t;
            Vector2 p = center + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * r;
            vh.AddVert(p, col, Vector2.zero);

            if (i > 0)
                vh.AddTriangle(fanStart, fanStart + i, fanStart + i + 1);
        }
    }

    private static void AddCap(VertexHelper vh, Vector2 endPoint, Vector2 towardPoint, float radius, int steps, Color32 col, bool startCap)
    {
        Vector2 dir = (endPoint - towardPoint);
        if (dir.sqrMagnitude < 0.0001f) return;
        dir.Normalize();

        if (startCap) dir = -dir;

        float baseAngle = Mathf.Atan2(dir.y, dir.x);

        float a0 = baseAngle - Mathf.PI * 0.5f;
        float a1 = baseAngle + Mathf.PI * 0.5f;

        int fanStart = vh.currentVertCount;
        vh.AddVert(endPoint, col, Vector2.zero);

        for (int i = 0; i <= steps; i++)
        {
            float t = i / (float)steps;
            float a = Mathf.Lerp(a0, a1, t);
            Vector2 p = endPoint + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * radius;
            vh.AddVert(p, col, Vector2.zero);

            if (i > 0)
                vh.AddTriangle(fanStart, fanStart + i, fanStart + i + 1);
        }
    }
}
