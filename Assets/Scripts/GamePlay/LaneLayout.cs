using UnityEngine;

//レーンの論理位置(lane)とワールド座標の変換をまとめたクラス
//ノーツ・ライト・区切り線・エフェクトはすべてここを基準に配置する
//
//フィールドの断面（カメラ側から見た図。laneが増えるほどxは小さくなる）
//  lane0                                  lane32
//    \                                    /
//     \ 0〜8                      24〜32 /   ← 左右8レーンはSlopeAngleだけ持ち上がった傾斜
//      \____________8〜24_______________/    ← 真ん中16レーンは水平(y=0)
public static class LaneLayout
{
    public static float FieldWidth = 8f;//フィールド全体の幅（傾斜を平らに伸ばしたときの長さ。見た目に合わせて調整）
    public static float FieldStartX = 0f;//水平に並べたときのlane0側の端のx座標
    public static int SideLaneCount = 8;//左右それぞれの傾斜レーンの本数
    public static float SlopeAngle = SensorLayout.SlopeAngle;//傾斜レーンが真ん中のレーンとなす角度（度）。判定ラインをセンサーに重ねるため、斜面センサーと同じ角度にする
    public static float LaneLength = 40f;//判定ラインからレーンの奥端までの長さ。LaneFieldBuilderのInspectorの値で上書きされる
    //判定ラインから壁（ノーツが見え始める位置）までの距離。選曲画面の設定で、レーンの奥側から何%を見えなくするか決める
    public static float VisibleLength => LaneLength * (100 - PlaySession.WallPercent) / 100f;

    public static float LaneWidth => FieldWidth / InputManager.LaneCount;
    //=>は「読むたびに右辺を計算し直すプロパティ」（ポインタではなく、中身は計算する関数）。
    //{ get { return FieldWidth / InputManager.LaneCount; } } の省略形。
    //値を保存していないので、FieldWidthを変えると次に読んだ時から新しい値になる。
    //=で書くと最初に1回計算した値が保存され、後からFieldWidthを変えても変わらない。

    //水平部分と傾斜部分の境目（折れ目）のレーン位置
    public static float LeftFold => SideLaneCount;//8
    public static float RightFold => InputManager.LaneCount - SideLaneCount;//24

    //フィールドは「左の傾斜」「真ん中の水平」「右の傾斜」の3区間に分かれる
    public const int SegmentCount = 3;

    //水平に並べたと仮定したときのlaneの端のx座標（laneが増えるほどxは小さくなる）
    //傾斜を含めた実際の位置はSurfacePosを使う
    public static float LanePos(float lane)
    {
        return FieldStartX - lane * LaneWidth;
    }

    //laneの位置の、フィールド表面上の点（x, y）。zは0
    //レーンの中心が欲しいときは lane + 0.5f、ノーツの中心なら lane + width / 2f を渡す
    public static Vector3 SurfacePos(float lane)
    {
        float rad = SlopeAngle * Mathf.Deg2Rad;
        if (lane < LeftFold)
        {
            //折れ目から外側へdだけ進むと、斜めに持ち上がる（横にcos、上にsin）
            float d = (LeftFold - lane) * LaneWidth;
            return new Vector3(LanePos(LeftFold) + d * Mathf.Cos(rad), d * Mathf.Sin(rad), 0);
        }
        if (lane > RightFold)
        {
            float d = (lane - RightFold) * LaneWidth;
            return new Vector3(LanePos(RightFold) - d * Mathf.Cos(rad), d * Mathf.Sin(rad), 0);
        }
        return new Vector3(LanePos(lane), 0, 0);
    }

    //laneの位置の面の傾き（z軸まわりの角度、度）
    //左の傾斜は+x側が上がるので+、右の傾斜は-x側が上がるので-
    public static float SurfaceAngle(float lane)
    {
        if (lane < LeftFold) return SlopeAngle;
        if (lane > RightFold) return -SlopeAngle;
        return 0f;
    }

    //レーンの境目i（0〜LaneCount）の角度。折れ目では両側の平均にする（区切り線用）
    public static float BoundaryAngle(int i)
    {
        return (SurfaceAngle(i - 0.5f) + SurfaceAngle(i + 0.5f)) / 2f;
    }

    public static Quaternion SurfaceRotation(float lane)
    {
        return Quaternion.Euler(0, 0, SurfaceAngle(lane));
    }

    //laneの位置で、面からheightだけ浮かせた点（面に垂直な方向に浮かせる）
    public static Vector3 SurfacePoint(float lane, float height, float z = 0f)
    {
        Vector3 p = SurfacePos(lane) + SurfaceRotation(lane) * Vector3.up * height;
        p.z = z;
        return p;
    }

    //from〜toのレーン範囲のうち、区間segment(0:左の傾斜, 1:水平, 2:右の傾斜)に入る部分を返す
    //その区間に入る部分がなければfalse
    public static bool ClipToSegment(int segment, float from, float to, out float clippedFrom, out float clippedTo)
    {
        float segFrom = segment == 0 ? float.NegativeInfinity : segment == 1 ? LeftFold : RightFold;
        float segTo = segment == 0 ? LeftFold : segment == 1 ? RightFold : float.PositiveInfinity;
        clippedFrom = Mathf.Max(from, segFrom);
        clippedTo = Mathf.Min(to, segTo);
        return clippedTo > clippedFrom;
    }
}
