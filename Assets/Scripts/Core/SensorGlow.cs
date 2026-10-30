using UnityEngine;
using UnityEngine.UI;
using static UIUtil;

//筐体のセンサーの位置に合わせて画面下部に出す光の帯（選曲画面・オープニングの質問・チュートリアルで使う）
//位置はキャンバス座標で計算するので、横幅基準（matchWidthOrHeight = 0）のCanvasの子に置く
//
//  底面: 画面下辺の中央に、底面センサーの長さぶん
//  斜面: 画面の左下・右下の角から45°の線に沿って、斜面センサー1本ぶん
public static class SensorGlow
{
    public static readonly float SlopeLength = RefWidth * SensorLayout.SlopeWidthRatio;//斜面センサー1本分の長さ（キャンバス座標）
    public static readonly float SlopeSpan = SlopeLength / Mathf.Sqrt(2f);//斜面が画面下辺・側辺を占める長さ
    public static readonly float BottomWidth = SlopeLength * 2f;//底面センサーの長さ
    public const float BottomHeight = 70f;//底面の光の太さ
    public const float SlopeHeight = 50f;//斜面の光の太さ
    const float Gap = 4f;//隣の帯との境目

    //触れていないときの明るさ。ゆっくり明滅して操作できる場所を示す
    public static float IdleLevel => 0.25f + 0.06f * Mathf.Sin(Time.time * 2.5f);

    //底面の帯。x: 底面センサーの中央から帯の左端までの位置、width: 帯の幅
    public static Image Bottom(RectTransform parent, string name, float x, float width, Color color, Sprite gradient)
    {
        var image = CreateImage(name, parent, color, gradient);
        Place(image.rectTransform, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), Vector2.zero, new Vector2(x + Gap, 0f), new Vector2(width - Gap * 2f, BottomHeight));
        return image;
    }

    //底面センサー全体の帯
    public static Image WholeBottom(RectTransform parent, string name, Color color, Sprite gradient)
    {
        return Bottom(parent, name, -BottomWidth / 2f, BottomWidth, color, gradient);
    }

    //斜面に沿って回した入れ物。幅は斜面センサー1本ぶん、中心がセンサーの線の中央。+yが画面の内側
    //左斜面は画面左辺から右下へ下る線、右斜面は画面下辺から右上へ上る線。どちらもレーン番号が小さい側がローカルの-x
    public static RectTransform SlopeRect(RectTransform parent, string name, bool left)
    {
        var slope = CreateRect(name, parent);
        Vector2 corner = left ? new Vector2(0f, 0f) : new Vector2(1f, 0f);
        Vector2 center = new Vector2(left ? SlopeSpan / 2f : -SlopeSpan / 2f, SlopeSpan / 2f);
        Place(slope, corner, corner, new Vector2(0.5f, 0.5f), center, new Vector2(SlopeLength, 0f));
        slope.localRotation = Quaternion.Euler(0f, 0f, left ? -45f : 45f);
        return slope;
    }

    //斜面センサー全体の帯（SlopeRectの子）。帯の下辺がセンサーの線に重なり、内側に向かって薄くなる
    public static Image WholeSlope(RectTransform slope, string name, Color color, Sprite gradient)
    {
        var image = CreateImage(name, slope, color, gradient);
        Place(image.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0f), Vector2.zero, new Vector2(SlopeLength - Gap * 2f, SlopeHeight));
        return image;
    }

    //下が不透明で上に向かって透明になる縦グラデーション
    public static Sprite CreateGradientSprite()
    {
        const int height = 64;
        var tex = new Texture2D(1, height, TextureFormat.RGBA32, false)
        {
            wrapMode = TextureWrapMode.Clamp,
            filterMode = FilterMode.Bilinear
        };
        for (int y = 0; y < height; y++)
        {
            float t = y / (float)(height - 1);
            tex.SetPixel(0, y, new Color(1f, 1f, 1f, (1f - t) * (1f - t)));
        }
        tex.Apply();
        return Sprite.Create(tex, new Rect(0, 0, 1, height), new Vector2(0.5f, 0.5f));
    }
}
