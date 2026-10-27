using System.Collections.Generic;
using UnityEngine;

//ノーツの板のメッシュを作るstaticクラス
//上から見て角を丸めた板（厚みあり）を作る。同じ大きさのメッシュは使い回す
//
//  上から見た図（+xがlaneの小さい側。LaneLayoutではlaneが増えるほどxは小さくなる）
//     ╭──────────────╮   ← 角の丸みは +x側・-x側で別々に指定できる
//     │              │     （傾斜の折れ目で分割した板は、折れ目側を丸めない）
//     ╰──────────────╯
public static class NoteMesh
{
    private const int CornerSegments = 8;//角1つあたりの分割数（多いほど滑らか）

    private static readonly Dictionary<(int, int, int, int, int), Mesh> cache = new Dictionary<(int, int, int, int, int), Mesh>();

    //width(x)・height(y)・depth(z)の板。中心が原点で、Cubeを同じ大きさに拡大したものと同じ位置になる
    //radiusPlusX / radiusMinusX: +x側・-x側の角の丸みの半径。0なら四角
    public static Mesh Box(float width, float height, float depth, float radiusPlusX, float radiusMinusX)
    {
        //半径は奥行きの半分まで。左右の丸みが幅に収まらない場合は同じ割合で小さくする
        radiusPlusX = Mathf.Clamp(radiusPlusX, 0f, depth / 2f);
        radiusMinusX = Mathf.Clamp(radiusMinusX, 0f, depth / 2f);
        float sum = radiusPlusX + radiusMinusX;
        if (sum > width && sum > 0f)
        {
            radiusPlusX *= width / sum;
            radiusMinusX *= width / sum;
        }

        var key = (Quantize(width), Quantize(height), Quantize(depth), Quantize(radiusPlusX), Quantize(radiusMinusX));
        //シーンの切り替えで破棄されている場合もあるので、nullなら作り直す
        if (cache.TryGetValue(key, out Mesh mesh) && mesh != null) return mesh;

        mesh = Build(width, height, depth, radiusPlusX, radiusMinusX);
        cache[key] = mesh;
        return mesh;
    }

    private static int Quantize(float value)
    {
        return Mathf.RoundToInt(value * 10000f);
    }

    private static Mesh Build(float width, float height, float depth, float radiusPlusX, float radiusMinusX)
    {
        float hw = width / 2f;
        float hh = height / 2f;
        float hd = depth / 2f;

        //上から見た輪郭(x, z)。+x側から反時計回り（角度が増える向き）に並べる
        var outline = new List<Vector2>();
        AddCorner(outline, hw - radiusPlusX, hd - radiusPlusX, radiusPlusX, 0f);
        AddCorner(outline, -hw + radiusMinusX, hd - radiusMinusX, radiusMinusX, 90f);
        AddCorner(outline, -hw + radiusMinusX, -hd + radiusMinusX, radiusMinusX, 180f);
        AddCorner(outline, hw - radiusPlusX, -hd + radiusPlusX, radiusPlusX, 270f);
        if ((outline[outline.Count - 1] - outline[0]).sqrMagnitude < 1e-10f) outline.RemoveAt(outline.Count - 1);
        int n = outline.Count;

        var vertices = new List<Vector3>();
        var normals = new List<Vector3>();
        var uvs = new List<Vector2>();
        var triangles = new List<int>();

        //上面と底面は中心から扇形に三角形を並べる（輪郭は凸なのでこれで埋まる）
        //Unityは「三角形の頂点が時計回りに見える側」が表なので、上面と底面で並び順を逆にする
        for (int face = 0; face < 2; face++)
        {
            bool top = face == 0;
            float y = top ? hh : -hh;
            int center = vertices.Count;
            vertices.Add(new Vector3(0f, y, 0f));
            normals.Add(top ? Vector3.up : Vector3.down);
            uvs.Add(new Vector2(0.5f, 0.5f));
            for (int i = 0; i < n; i++)
            {
                vertices.Add(new Vector3(outline[i].x, y, outline[i].y));
                normals.Add(top ? Vector3.up : Vector3.down);
                uvs.Add(new Vector2(outline[i].x / width + 0.5f, outline[i].y / depth + 0.5f));
            }
            for (int i = 0; i < n; i++)
            {
                int a = center + 1 + i;
                int b = center + 1 + (i + 1) % n;
                triangles.Add(center);
                triangles.Add(top ? b : a);
                triangles.Add(top ? a : b);
            }
        }

        //側面は輪郭の1辺ごとに四角形を作る（辺ごとに頂点を分けて、外向きの法線にする）
        //UVは上面と同じく上から見た位置にする（縁のグラデーションが奥行き方向に掛かるように）
        for (int i = 0; i < n; i++)
        {
            Vector2 p = outline[i];
            Vector2 q = outline[(i + 1) % n];
            Vector3 normal = new Vector3(q.y - p.y, 0f, -(q.x - p.x)).normalized;
            int s = vertices.Count;
            vertices.Add(new Vector3(p.x, -hh, p.y));//s  : 底のp
            vertices.Add(new Vector3(p.x, hh, p.y));//s+1: 上のp
            vertices.Add(new Vector3(q.x, hh, q.y));//s+2: 上のq
            vertices.Add(new Vector3(q.x, -hh, q.y));//s+3: 底のq
            Vector2 uvP = new Vector2(p.x / width + 0.5f, p.y / depth + 0.5f);
            Vector2 uvQ = new Vector2(q.x / width + 0.5f, q.y / depth + 0.5f);
            for (int k = 0; k < 4; k++)
            {
                normals.Add(normal);
                uvs.Add(k == 0 || k == 1 ? uvP : uvQ);
            }
            triangles.Add(s); triangles.Add(s + 1); triangles.Add(s + 2);
            triangles.Add(s); triangles.Add(s + 2); triangles.Add(s + 3);
        }

        var mesh = new Mesh { name = "NoteBox" };
        mesh.SetVertices(vertices);
        mesh.SetNormals(normals);
        mesh.SetUVs(0, uvs);
        mesh.SetTriangles(triangles, 0);
        mesh.RecalculateBounds();
        return mesh;
    }

    //中心(cx, cz)・半径radiusの角を、startDeg度から90度分の円弧で輪郭に加える。半径0なら角の点だけ
    private static void AddCorner(List<Vector2> outline, float cx, float cz, float radius, float startDeg)
    {
        int segments = radius > 0f ? CornerSegments : 0;
        for (int i = 0; i <= segments; i++)
        {
            float rad = (startDeg + 90f * i / Mathf.Max(segments, 1)) * Mathf.Deg2Rad;
            Vector2 p = new Vector2(cx + radius * Mathf.Cos(rad), cz + radius * Mathf.Sin(rad));
            //角が四角のときに同じ点が続かないようにする
            if (outline.Count == 0 || (p - outline[outline.Count - 1]).sqrMagnitude > 1e-10f) outline.Add(p);
        }
    }
}
