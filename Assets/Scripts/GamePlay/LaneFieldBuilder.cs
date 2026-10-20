using System.Collections;
using System.Collections.Generic;
using UnityEngine;

//起動時にレーンごとのライトと区切り線（と判定ライン）をLaneLayoutに合わせて生成する
//左右の傾斜レーンの分は、面に沿って傾けて置く
//このオブジェクトはワールド原点(位置0・回転0・スケール1)に置くこと
public class LaneFieldBuilder : MonoBehaviour
{
    public GameObject LaneLightPrefab;//railfadeが付いたライトのプレハブ
    public GameObject LaneLinePrefab;//区切り線のプレハブ
    public GameObject JudgeLinePrefab;//判定ラインのプレハブ（未設定なら生成しない）。傾斜に沿うよう1レーンずつ並べる
    public GameObject RailVisualPrefab;//レーンの床のプレハブ（未設定なら生成しない）。判定ラインと同じく1レーンずつ並べる

    public int thickLineInterval = 4;//この本数ごとに区切り線を太くする（キー単位）
    public float thickLineScale = 2f;//太い区切り線の太さ倍率

    void Awake()
    {
        //プレイ開始時にレーンを構築する。
        //ライトは1レーンに1つで32個、区切り線は両端を含むので33本をプレハブから生成する
        BuildLights();
        BuildLines();
        BuildPerLane(JudgeLinePrefab, "JudgeLine");
        BuildPerLane(RailVisualPrefab, "RailVisual");
    }


    private void BuildLights()
    {
        if (LaneLightPrefab == null) return;

        for (int lane = 0; lane < InputManager.LaneCount; lane++)
        {
            GameObject obj = Instantiate(LaneLightPrefab, transform);
            obj.name = $"LaneLight_{lane}";
            //boundsで幅を測るので、傾ける前に幅を合わせる
            FitWidth(obj, LaneLayout.LaneWidth);
            PlaceOnSurface(obj.transform, lane + 0.5f, LaneLayout.SurfaceAngle(lane + 0.5f));//レーンの中心

            railfade fade = obj.GetComponentInChildren<railfade>();
            if (fade != null) fade.laneIndex = lane;
        }
    }

    private void BuildLines()
    {
        if (LaneLinePrefab == null) return;

        //両端を含めてLaneCount+1本
        for (int i = 0; i <= InputManager.LaneCount; i++)
        {
            GameObject obj = Instantiate(LaneLinePrefab, transform);
            obj.name = $"LaneLine_{i}";
            //折れ目の線は両側の面の中間の角度にする
            PlaceOnSurface(obj.transform, i, LaneLayout.BoundaryAngle(i));

            if (thickLineInterval > 0 && i % thickLineInterval == 0)
            {
                Vector3 scale = obj.transform.localScale;
                scale.x *= thickLineScale;
                obj.transform.localScale = scale;
            }
        }
    }

    //判定ラインや床は1本のCubeだと折り曲げられないので、1レーンずつ並べて面に沿わせる
    private void BuildPerLane(GameObject prefab, string name)
    {
        if (prefab == null) return;

        for (int lane = 0; lane < InputManager.LaneCount; lane++)
        {
            GameObject obj = Instantiate(prefab, transform);
            obj.name = $"{name}_{lane}";
            FitWidth(obj, LaneLayout.LaneWidth);
            PlaceOnSurface(obj.transform, lane + 0.5f, LaneLayout.SurfaceAngle(lane + 0.5f));
        }
    }

    //レーン位置laneの面上に置き、angle度だけ傾ける
    //プレハブのyは面からの高さ（面に垂直な方向）、zはそのまま使う
    private void PlaceOnSurface(Transform t, float lane, float angle)
    {
        Quaternion tilt = Quaternion.Euler(0, 0, angle);
        Vector3 prefabPos = t.localPosition;
        Vector3 pos = LaneLayout.SurfacePos(lane) + tilt * Vector3.up * prefabPos.y;
        pos.z = prefabPos.z;
        t.localPosition = pos;
        t.localRotation = tilt * t.localRotation;
    }

    //見た目の横幅がwidthになるようにx方向のスケールを合わせる
    private void FitWidth(GameObject obj, float width)
    {
        Renderer rend = obj.GetComponentInChildren<Renderer>();
        if (rend == null) return;

        //boundsは見た目をぴったり囲む箱(バウンディングボックス)。
        //bounds.size.xは、スケールや親の影響を反映した後の、ワールド空間での実際の横幅
        float currentWidth = rend.bounds.size.x;
        if (currentWidth <= 0) return;

        //「目標の幅 ÷ 今の幅」が、何倍にすればよいかの倍率になる。
        //例: 今の幅1.0で目標0.25なら0.25倍、今の幅0.5なら0.5倍 → どちらも結果は0.25
        //プレハブが元々どんな大きさでも、必ず目標の幅になる
        Vector3 scale = obj.transform.localScale;
        scale.x *= width / currentWidth;
        obj.transform.localScale = scale;
    }
}
