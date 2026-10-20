using UnityEngine;

//Cubeのノーツを傾斜したフィールド表面に沿わせて描画する
//ノーツが折れ目をまたぐ場合は折れ目で分割し、区間ごとに傾けた板を並べる
//このコンポーネントを付けたオブジェクト自身のRendererは非表示にし、子に作った板で見た目を作る
//（オブジェクト自身は回転0・x=y=0に置き、z位置だけを動かす）
public class LaneSurfaceShape : MonoBehaviour
{
    private Transform[] pieces;//区間ごとの板（左の傾斜・水平・右の傾斜）
    private Vector3 baseScale;//プレハブ元々のスケール。yを厚み、zを奥行きとして使う

    private void Setup()
    {
        if (pieces != null) return;

        baseScale = transform.localScale;
        transform.localScale = Vector3.one;//子の板にスケールが掛からないようにする

        MeshFilter filter = GetComponent<MeshFilter>();
        MeshRenderer rend = GetComponent<MeshRenderer>();

        pieces = new Transform[LaneLayout.SegmentCount];
        for (int i = 0; i < pieces.Length; i++)
        {
            GameObject obj = new GameObject($"Piece_{i}");
            obj.transform.SetParent(transform, false);
            obj.AddComponent<MeshFilter>().sharedMesh = filter.sharedMesh;
            obj.AddComponent<MeshRenderer>().sharedMaterials = rend.sharedMaterials;
            pieces[i] = obj.transform;
        }
        rend.enabled = false;
    }

    //lane〜lane+widthの範囲に、面からheightだけ浮かせて板を並べる
    public void SetRange(float lane, float width, float height)
    {
        Setup();

        for (int i = 0; i < pieces.Length; i++)
        {
            bool visible = LaneLayout.ClipToSegment(i, lane, lane + width, out float from, out float to);
            pieces[i].gameObject.SetActive(visible);
            if (!visible) continue;

            float center = (from + to) / 2f;
            pieces[i].localPosition = LaneLayout.SurfacePoint(center, height);
            pieces[i].localRotation = LaneLayout.SurfaceRotation(center);
            //Cubeは横幅1なので、scale.xがそのまま見た目の横幅になる
            pieces[i].localScale = new Vector3((to - from) * LaneLayout.LaneWidth, baseScale.y, baseScale.z);
        }
    }
}
