using UnityEngine;
using UnityEngine.UI;

//上辺を下辺よりSkewだけ右にずらした平行四辺形で描くRawImage（最終リザルトの斜めの帯）
//textureが無ければ単色の帯になる。同じ高さ・同じSkewの帯を横に並べると、斜めの辺がぴったり重なる
public class SkewedRawImage : RawImage
{
    private float skew;

    public float Skew
    {
        get => skew;
        set
        {
            skew = value;
            SetVerticesDirty();
        }
    }

    //普通の四角形を作ってから、上の頂点ほど右へずらす（真ん中の高さはそのまま）
    protected override void OnPopulateMesh(VertexHelper vh)
    {
        base.OnPopulateMesh(vh);
        Rect rect = GetPixelAdjustedRect();
        if (rect.height <= 0f) return;

        var vertex = new UIVertex();
        for (int i = 0; i < vh.currentVertCount; i++)
        {
            vh.PopulateUIVertex(ref vertex, i);
            float t = (vertex.position.y - rect.yMin) / rect.height;
            vertex.position.x += skew * (t - 0.5f);
            vh.SetUIVertex(vertex, i);
        }
    }
}
