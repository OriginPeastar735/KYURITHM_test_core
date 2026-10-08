using System.Collections;
using System.Collections.Generic;
using UnityEngine;

//起動時にレーンごとのライトと区切り線をLaneLayoutに合わせて生成する
//このオブジェクトはワールド原点(位置0・回転0・スケール1)に置くこと
public class LaneFieldBuilder : MonoBehaviour
{
    public GameObject LaneLightPrefab;//railfadeが付いたライトのプレハブ
    public GameObject LaneLinePrefab;//区切り線のプレハブ

    public int thickLineInterval = 4;//この本数ごとに区切り線を太くする（キー単位）
    public float thickLineScale = 2f;//太い区切り線の太さ倍率

    void Awake()
    {
        BuildLights();
        BuildLines();
    }

    private void BuildLights()
    {
        if (LaneLightPrefab == null) return;

        for (int lane = 0; lane < InputManager.LaneCount; lane++)
        {
            GameObject obj = Instantiate(LaneLightPrefab, transform);
            obj.name = $"LaneLight_{lane}";
            SetX(obj.transform, LaneLayout.LanePos(lane + 0.5f));//レーンの中心
            FitWidth(obj, LaneLayout.LaneWidth);

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
            SetX(obj.transform, LaneLayout.LanePos(i));

            if (thickLineInterval > 0 && i % thickLineInterval == 0)
            {
                Vector3 scale = obj.transform.localScale;
                scale.x *= thickLineScale;
                obj.transform.localScale = scale;
            }
        }
    }

    //プレハブのy,zはそのままにxだけ設定する
    private void SetX(Transform t, float x)
    {
        Vector3 pos = t.localPosition;
        pos.x = x;
        t.localPosition = pos;
    }

    //見た目の横幅がwidthになるようにx方向のスケールを合わせる
    private void FitWidth(GameObject obj, float width)
    {
        Renderer rend = obj.GetComponentInChildren<Renderer>();
        if (rend == null) return;

        float currentWidth = rend.bounds.size.x;
        if (currentWidth <= 0) return;

        Vector3 scale = obj.transform.localScale;
        scale.x *= width / currentWidth;
        obj.transform.localScale = scale;
    }
}
