using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class EffectManager : MonoBehaviour
{
    public GameObject perfectEffectPrefab;
    public GameObject greatEffectPrefab;
    public GameObject goodEffectPrefab;
    public GameObject holdEffectPrefab;

    public static EffectManager instance;

    public float effectY = 0.002f;//エフェクトを出す高さ
    public float judgeLineZ = 0f;//判定ラインのz座標

    void Awake()
    {
        if (instance == null)
        {
            instance = this;
        }
    }

    //判定ライン上のx座標の位置
    private Vector3 EffectPos(float x)
    {
        return new Vector3(x, effectY, judgeLineZ);
    }

    public void PerfectEffect(float x)
    {
        Instantiate(perfectEffectPrefab, EffectPos(x), Quaternion.identity);
    }

    public void GreatEffect(float x)
    {
        Instantiate(greatEffectPrefab, EffectPos(x), Quaternion.identity);
    }

    public void GoodEffect(float x)
    {
        Instantiate(goodEffectPrefab, EffectPos(x), Quaternion.identity);
    }

    //押している間のエフェクト。消すのは呼び出し側（LongNote.Finish）が行う
    public GameObject HoldEffect(float x)
    {
        return Instantiate(holdEffectPrefab, EffectPos(x), Quaternion.identity);
    }

}
