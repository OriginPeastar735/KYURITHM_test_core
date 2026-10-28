using UnityEngine;

//Cubeのノーツを傾斜したフィールド表面に沿わせて描画する
//ノーツが折れ目をまたぐ場合は折れ目で分割し、区間ごとに傾けた板を並べる
//このコンポーネントを付けたオブジェクト自身のRendererは非表示にし、子に作った板で見た目を作る
//（オブジェクト自身は回転0・x=y=0に置き、z位置だけを動かす）
//
//板はNoteMeshで作る、角を丸めた板。SetStyleで角の丸みと中心の線を指定できる（指定しなければ四角い板）
public class LaneSurfaceShape : MonoBehaviour
{
    private const float CenterLineThickness = 0.002f;//中心の線の厚み
    private const float CenterLineLift = 0.001f;//中心の線をノーツの上面から浮かせる高さ（チラつき防止）
    private const float OutlineDrop = 0.002f;//縁の板をノーツより下げる高さ（上面が重なってチラつかないように）

    private Transform[] pieces;//区間ごとの板（左の傾斜・水平・右の傾斜）
    private MeshFilter[] pieceFilters;
    private Vector3 baseScale;//プレハブ元々のスケール。yを厚み、zを奥行きとして使う

    //板の奥行き（z方向の長さ）。SetStyleより後に読む
    public float Depth => baseScale.z;

    private float cornerRadius;//上から見た角の丸みの半径（ワールド座標）。0なら四角
    private LaneSurfaceShape centerLine;//中心の線（無ければnull）
    private float centerLineInset;//線の端からノーツの端までの長さ（ワールド座標）
    private LaneSurfaceShape outline;//ノーツの周りの縁（無ければnull）
    private float outlineWidth;//縁の太さ（ワールド座標）

    private float lastLane = float.NaN, lastWidth, lastHeight;//前回のSetRange（同じなら作り直さない）

    private void Setup()
    {
        if (pieces != null) return;

        baseScale = transform.localScale;
        transform.localScale = Vector3.one;//子の板にスケールが掛からないようにする

        MeshRenderer rend = GetComponent<MeshRenderer>();

        pieces = new Transform[LaneLayout.SegmentCount];
        pieceFilters = new MeshFilter[LaneLayout.SegmentCount];
        for (int i = 0; i < pieces.Length; i++)
        {
            GameObject obj = new GameObject($"Piece_{i}");
            obj.transform.SetParent(transform, false);
            pieceFilters[i] = obj.AddComponent<MeshFilter>();
            obj.AddComponent<MeshRenderer>().sharedMaterials = rend.sharedMaterials;
            pieces[i] = obj.transform;
        }
        rend.enabled = false;
    }

    //見た目の指定。SetRangeより前に呼ぶ。長さはすべてノーツの奥行きに対する割合
    //cornerRadiusRatio: 角の丸みの半径
    //centerLineMaterial: 中心の線のマテリアル。nullなら線を入れない
    //centerLineWidthRatio: 線の太さ   centerLineInsetRatio: 線の端からノーツの端までの長さ
    public void SetStyle(float cornerRadiusRatio, Material centerLineMaterial = null, float centerLineWidthRatio = 0f, float centerLineInsetRatio = 0f)
    {
        Setup();
        float depth = baseScale.z;
        cornerRadius = depth * cornerRadiusRatio;
        lastLane = float.NaN;//次のSetRangeで作り直す

        if (centerLineMaterial == null || centerLine != null) return;

        //線も同じ仕組みで傾斜に沿わせる（角は丸めない）
        GameObject obj = new GameObject("CenterLine");
        obj.transform.SetParent(transform, false);
        obj.transform.localScale = new Vector3(1f, CenterLineThickness, depth * centerLineWidthRatio);
        obj.AddComponent<MeshRenderer>().sharedMaterial = centerLineMaterial;
        centerLine = obj.AddComponent<LaneSurfaceShape>();
        centerLineInset = depth * centerLineInsetRatio;
    }

    //ノーツの縁を付ける。SetStyleの後、SetRangeより前に呼ぶ
    //outlineMaterial: 縁のマテリアル   outlineWidthRatio: 縁の太さ（ノーツの奥行きに対する割合）
    //
    //  横から見た図。ノーツと同じ大きさの縁の板を少し下げて置き、色の板は縁の太さの分だけ内側に縮める
    //  （縁を外側に広げると、隣のノーツの下に隠れてノーツ同士がつながって見えるため）
    //     ┌─┬──────────┬─┐ ← 色の板
    //     └─┴──────────┴─┘ ← 縁の板（ノーツ本来の大きさ）
    public void SetOutline(Material outlineMaterial, float outlineWidthRatio)
    {
        Setup();
        if (outline != null) return;
        float depth = baseScale.z;
        outlineWidth = depth * outlineWidthRatio;
        lastLane = float.NaN;//次のSetRangeで作り直す

        //縁も同じ仕組みで傾斜に沿わせる
        GameObject obj = new GameObject("Outline");
        obj.transform.SetParent(transform, false);
        obj.transform.localScale = new Vector3(1f, baseScale.y, depth);
        obj.AddComponent<MeshRenderer>().sharedMaterial = outlineMaterial;
        outline = obj.AddComponent<LaneSurfaceShape>();
        outline.SetStyle(cornerRadius / depth);
    }

    //lane〜lane+widthの範囲に、面からheightだけ浮かせて板を並べる
    public void SetRange(float lane, float width, float height)
    {
        Setup();
        if (lane == lastLane && width == lastWidth && height == lastHeight) return;
        lastLane = lane;
        lastWidth = width;
        lastHeight = height;

        //縁があるときは、色の板を縁の太さの分だけ内側に縮める。角の丸みも同じ中心になるように小さくする
        float inset = outline != null ? outlineWidth / LaneLayout.LaneWidth : 0f;
        float bodyLane = lane + inset;
        float bodyEnd = lane + width - inset;
        float bodyDepth = Mathf.Max(baseScale.z - outlineWidth * 2f, 0.001f);
        float bodyRadius = Mathf.Max(cornerRadius - outlineWidth, 0f);

        for (int i = 0; i < pieces.Length; i++)
        {
            //縮めた結果幅が無くなる細いノーツは、ClipToSegmentがfalseになり縁だけが表示される
            bool visible = LaneLayout.ClipToSegment(i, bodyLane, bodyEnd, out float from, out float to);
            pieces[i].gameObject.SetActive(visible);
            if (!visible) continue;

            float center = (from + to) / 2f;
            pieces[i].localPosition = LaneLayout.SurfacePoint(center, height);
            pieces[i].localRotation = LaneLayout.SurfaceRotation(center);

            //ノーツの両端だけ角を丸める（折れ目で切った側は丸めない）
            //laneが小さい側(from)が+x側になる
            float radiusFrom = Mathf.Approximately(from, bodyLane) ? bodyRadius : 0f;
            float radiusTo = Mathf.Approximately(to, bodyEnd) ? bodyRadius : 0f;
            pieceFilters[i].sharedMesh = NoteMesh.Box((to - from) * LaneLayout.LaneWidth, baseScale.y, bodyDepth, radiusFrom, radiusTo);
        }

        if (centerLine != null)
        {
            //線は両端をcenterLineInsetだけ内側に縮める。縮めた結果長さが無くなる細いノーツには出さない
            float lineInset = centerLineInset / LaneLayout.LaneWidth;
            float lineWidth = width - lineInset * 2f;
            centerLine.gameObject.SetActive(lineWidth > 0f);
            if (lineWidth > 0f) centerLine.SetRange(lane + lineInset, lineWidth, height + baseScale.y / 2f + CenterLineThickness / 2f + CenterLineLift);
        }

        if (outline != null)
        {
            outline.SetRange(lane, width, height - OutlineDrop);
        }
    }
}
