using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

//入力をレーン単位の状態（押された/押されている/離された）に変換する
//判定側はこのクラスだけを参照し、入力デバイスの違いはReadInputで吸収する
[DefaultExecutionOrder(-100)]//JudgeManagerより先に入力を更新する
public class InputManager : MonoBehaviour
{
    public static InputManager instance;

    public const int LaneCount = 32;

    public bool[] laneDown = new bool[LaneCount];//このフレームで押された
    public bool[] laneHeld = new bool[LaneCount];//押されている
    public bool[] laneUp = new bool[LaneCount];//このフレームで離された
    private bool[] prevHeld = new bool[LaneCount];

    //テストプレイ用キー配置（左から順に4レーンずつ担当）
    [SerializeField]
    private KeyCode[] testKeys =
    {
        KeyCode.A, KeyCode.S, KeyCode.D, KeyCode.F,
        KeyCode.J, KeyCode.K, KeyCode.L, KeyCode.Semicolon
    };
    private const int lanesPerKey = 4;

    void Awake()
    {
        if (instance == null)
        {
            instance = this;
        }
    }

    void Update()
    {
        Array.Copy(laneHeld, prevHeld, LaneCount);
        ReadInput(laneHeld);

        //前フレームとの差分から押した瞬間・離した瞬間を求める
        for (int i = 0; i < LaneCount; i++)
        {
            laneDown[i] = laneHeld[i] && !prevHeld[i];
            laneUp[i] = !laneHeld[i] && prevHeld[i];
        }
    }

    //現在押されているレーンをheldに書き込む
    //専用コントローラー対応時はここを差し替える
    private void ReadInput(bool[] held)
    {
        Array.Clear(held, 0, LaneCount);
        for (int k = 0; k < testKeys.Length; k++)
        {
            if (!Input.GetKey(testKeys[k])) continue;
            for (int j = 0; j < lanesPerKey; j++)
            {
                int lane = k * lanesPerKey + j;
                if (lane < LaneCount) held[lane] = true;
            }
        }
    }

    //ノーツの範囲[lane, lane+width)のどこかがこのフレームで押されたか
    public bool AnyDown(int lane, int width)
    {
        return AnyInRange(laneDown, lane, width);
    }

    //ノーツの範囲[lane, lane+width)のどこかが押されているか
    public bool AnyHeld(int lane, int width)
    {
        return AnyInRange(laneHeld, lane, width);
    }

    private bool AnyInRange(bool[] states, int lane, int width)
    {
        int start = Mathf.Max(0, lane);
        int end = Mathf.Min(LaneCount, lane + width);
        for (int i = start; i < end; i++)
        {
            if (states[i]) return true;
        }
        return false;
    }
}
