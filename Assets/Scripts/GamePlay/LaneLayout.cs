using UnityEngine;

//レーンの論理位置(lane)とワールド座標(x)の変換をまとめたクラス
//ノーツ・ライト・区切り線はすべてここを基準に配置する
public static class LaneLayout
{
    public static float FieldWidth = 8f;//フィールド全体の幅（見た目に合わせて調整）
    public static float FieldStartX = 0f;//lane0側の端のx座標

    public static float LaneWidth => FieldWidth / InputManager.LaneCount;
    //=>は「読むたびに右辺を計算し直すプロパティ」（ポインタではなく、中身は計算する関数）。
    //{ get { return FieldWidth / InputManager.LaneCount; } } の省略形。
    //値を保存していないので、FieldWidthを変えると次に読んだ時から新しい値になる。
    //=で書くと最初に1回計算した値が保存され、後からFieldWidthを変えても変わらない。

    //laneの端のx座標を返す（laneが増えるほどxは小さくなる）
    //レーンの中心が欲しいときは lane + 0.5f、ノーツの中心なら lane + width / 2f を渡す
    public static float LanePos(float lane)
    {
        return FieldStartX - lane * LaneWidth;
    }
}
