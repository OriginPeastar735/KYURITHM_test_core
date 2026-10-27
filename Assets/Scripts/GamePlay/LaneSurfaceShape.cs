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

    private Transform[] pieces;//区間ごとの板（左の傾斜・水平・右の傾斜）
    private MeshFilter[] pieceFilters;
    private Vector3 baseScale;//プレハブ元々のスケール。yを厚み、zを奥行きとして使う

    private float cornerRadius;//上から見た角の丸みの半径（ワールド座標）。0なら四角
    private LaneSurfaceShape centerLine;//中心の線（無ければnull）
    private float centerLineInset;//線の端からノーツの端までの長さ（ワールド座標）

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

    //lane〜lane+widthの範囲に、面からheightだけ浮かせて板を並べる
    public void SetRange(float lane, float width, float height)
    {
        Setup();
        if (lane == lastLane && width == lastWidth && height == lastHeight) return;
        lastLane = lane;
        lastWidth = width;
        lastHeight = height;

        for (int i = 0; i < pieces.Length; i++)
        {
            bool visible = LaneLayout.ClipToSegment(i, lane, lane + width, out float from, out float to);
            pieces[i].gameObject.SetActive(visible);
            if (!visible) continue;

            float center = (from + to) / 2f;
            pieces[i].localPosition = LaneLayout.SurfacePoint(center, height);
            pieces[i].localRotation = LaneLayout.SurfaceRotation(center);

            //ノーツの両端だけ角を丸める（折れ目で切った側は丸めない）
            //laneが小さい側(from)が+x側になる
            float radiusFrom = Mathf.Approximately(from, lane) ? cornerRadius : 0f;
            float radiusTo = Mathf.Approximately(to, lane + width) ? cornerRadius : 0f;
            pieceFilters[i].sharedMesh = NoteMesh.Box((to - from) * LaneLayout.LaneWidth, baseScale.y, baseScale.z, radiusFrom, radiusTo);
        }

        if (centerLine != null)
        {
            //線は両端をcenterLineInsetだけ内側に縮める。縮めた結果長さが無くなる細いノーツには出さない
            float inset = centerLineInset / LaneLayout.LaneWidth;
            float lineWidth = width - inset * 2f;
            centerLine.gameObject.SetActive(lineWidth > 0f);
            if (lineWidth > 0f) centerLine.SetRange(lane + inset, lineWidth, height + baseScale.y / 2f + CenterLineThickness / 2f + CenterLineLift);
        }
    }
}
