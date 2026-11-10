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

    //判定のエフェクトは判定のたびに複製せず、再生し終えたものを使い回す（プレハブごとに持つ）
    //プレハブは「再生し終えたら自分を消す」設定なので、複製したものは「再生し終えたら非アクティブにする」に変えておき、
    //次に使うときにアクティブに戻して最初から再生する
    private readonly Dictionary<GameObject, List<GameObject>> pools = new Dictionary<GameObject, List<GameObject>>();

    private void SpawnPooled(GameObject prefab, float lane)
    {
        if (!pools.TryGetValue(prefab, out List<GameObject> pool))
        {
            pool = new List<GameObject>();
            pools[prefab] = pool;
        }

        foreach (GameObject used in pool)
        {
            if (used == null || used.activeSelf) continue;//まだ再生中
            used.transform.SetPositionAndRotation(EffectPos(lane), LaneLayout.SurfaceRotation(lane));
            used.SetActive(true);//Play On Awakeで最初から再生される
            return;
        }

        GameObject effect = Spawn(prefab, lane);
        //一番上にパーティクルが無いプレハブは使い回せないので、今までどおり複製するだけにする（消すのはプレハブ側の設定に任せる）
        ParticleSystem root = effect.GetComponent<ParticleSystem>();
        if (root == null) return;
        ParticleSystem.MainModule main = root.main;
        main.stopAction = ParticleSystemStopAction.Disable;
        main.playOnAwake = true;
        pool.Add(effect);
    }

    // レーン位置(ノーツの中心レーン)を受け取り、エフェクトを出す
    public void PrecisePlusEffect(float lane)
    {
        SpawnPooled(precisePlusEffectPrefab, lane);
    }

    public void PreciseEffect(float lane)
    {
        SpawnPooled(preciseEffectPrefab, lane);
    }

    public void ApproxEffect(float lane)
    {
        SpawnPooled(approxEffectPrefab, lane);
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
