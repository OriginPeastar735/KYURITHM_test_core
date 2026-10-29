using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Serialization;

//判定に応じたエフェクトを、判定ライン上のノーツの位置に傾斜に合わせて出す
public class EffectManager : MonoBehaviour
{
    //判定ごとのエフェクトのプレハブ
    //FormerlySerializedAs: 旧名(perfect/great/good)でシーンに保存された割り当てを引き継ぐ
    [FormerlySerializedAs("perfectEffectPrefab")] public GameObject precisePlusEffectPrefab;
    [FormerlySerializedAs("greatEffectPrefab")] public GameObject preciseEffectPrefab;
    [FormerlySerializedAs("goodEffectPrefab")] public GameObject approxEffectPrefab;
    public GameObject holdEffectPrefab;

    public static EffectManager instance;

    public float effectY = 0.002f;//エフェクトを出す高さ。床と同じ高さだとチラつく(Zファイティング)ので少し浮かせる
    public float judgeLineZ = 0f;//判定ラインのz座標
    //エフェクトのOrder in Layer。判定ラインはLaneFieldBuilderで描く順番(renderQueue)を遅くしてあり、そのままだとエフェクトの上に描かれてしまう
    //Order in LayerはrenderQueueより優先されるので、0より大きくすれば判定ライン・区切り線・床より後に描かれる
    //ロングノーツの帯(1)よりも上に出すため2にする
    public int effectSortingOrder = 2;

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

    //傾斜面に合わせた位置・向きでInstantiateし、判定ラインより手前に描かれるようにする
    private GameObject Spawn(GameObject prefab, float lane)
    {
        GameObject effect = Instantiate(prefab, EffectPos(lane), LaneLayout.SurfaceRotation(lane));
        foreach (Renderer rend in effect.GetComponentsInChildren<Renderer>(true))
        {
            rend.sortingOrder = effectSortingOrder;
        }
        return effect;
    }

    // レーン位置(ノーツの中心レーン)を受け取り、エフェクトを出す
    public void PrecisePlusEffect(float lane)
    {
        Spawn(precisePlusEffectPrefab, lane);
    }

    public void PreciseEffect(float lane)
    {
        Spawn(preciseEffectPrefab, lane);
    }

    public void ApproxEffect(float lane)
    {
        Spawn(approxEffectPrefab, lane);
    }

    //押している間のエフェクト。消すのは呼び出し側（LongNote.Finish）が行う
    public GameObject HoldEffect(float lane)
    {
        return Spawn(holdEffectPrefab, lane);
    }

    //押している間のエフェクトをレーン位置laneに動かす（slideの移動に追従させる）
    public void FollowLane(GameObject effect, float lane)
    {
        effect.transform.SetPositionAndRotation(EffectPos(lane), LaneLayout.SurfaceRotation(lane));
    }

}
