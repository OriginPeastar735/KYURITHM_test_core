using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class EffectManager : MonoBehaviour
{
    //ここにエフェクトのVFXを格納する
    public GameObject perfectEffectPrefab;
    public GameObject greatEffectPrefab;
    public GameObject goodEffectPrefab;
    public GameObject holdEffectPrefab;

    public static EffectManager instance;

    public float effectY = 0.002f;//エフェクトを出す高さ。床と同じ高さだとチラつく(Zファイティング)ので少し浮かせる
    public float judgeLineZ = 0f;//判定ラインのz座標

    void Awake()
    {
        if (instance == null)
        {
            instance = this;
        }
    }

    //判定ライン上の、レーン位置laneの面上の点
    private Vector3 EffectPos(float lane)
    {
        return LaneLayout.SurfacePoint(lane, effectY, judgeLineZ);
    }

    // レーン位置(ノーツの中心レーン)を受け取り、傾斜面に合わせた位置・向きでエフェクトをInstantiate
    public void PerfectEffect(float lane)
    {
        Instantiate(perfectEffectPrefab, EffectPos(lane), LaneLayout.SurfaceRotation(lane));
    }

    public void GreatEffect(float lane)
    {
        Instantiate(greatEffectPrefab, EffectPos(lane), LaneLayout.SurfaceRotation(lane));
    }

    public void GoodEffect(float lane)
    {
        Instantiate(goodEffectPrefab, EffectPos(lane), LaneLayout.SurfaceRotation(lane));
    }

    //押している間のエフェクト。消すのは呼び出し側（LongNote.Finish）が行う
    public GameObject HoldEffect(float lane)
    {
        return Instantiate(holdEffectPrefab, EffectPos(lane), LaneLayout.SurfaceRotation(lane));
    }

    //押している間のエフェクトをレーン位置laneに動かす（slideの移動に追従させる）
    public void FollowLane(GameObject effect, float lane)
    {
        effect.transform.SetPositionAndRotation(EffectPos(lane), LaneLayout.SurfaceRotation(lane));
    }

}
