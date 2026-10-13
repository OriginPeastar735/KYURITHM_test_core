using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System.IO.Ports;
using System.Threading;

//入力をレーン単位の状態（押された/押されている/離された）に変換する
//判定側はこのクラスだけを参照し、入力デバイスの違いはReadInputで吸収する
[DefaultExecutionOrder(-100)]//JudgeManagerより先に入力を更新する
public class InputManager : MonoBehaviour
{
    public static InputManager instance;

    public const int LaneCount = 32;//音ゲーの仕様によりレーンは32本。

    public bool[] laneDown = new bool[LaneCount];//このフレームで押された
    public bool[] laneHeld = new bool[LaneCount];//押されている
    public bool[] laneUp = new bool[LaneCount];//このフレームで離された
    private bool[] prevHeld = new bool[LaneCount];//直前のフレームで押されている

    //テストプレイ用キー配置（左から順に4レーンずつ担当）
    [SerializeField]
    private KeyCode[] testKeys =
    {
        KeyCode.A, KeyCode.S, KeyCode.D, KeyCode.F,
        KeyCode.J, KeyCode.K, KeyCode.L, KeyCode.Semicolon
    };
    private const int lanesPerKey = 4;


    [SerializeField] private string portName = "COM5"; //シリアルポート名
    private const int baudRate = 115200;
    private const byte packetHeader = 0xAA;
    private const int sensorCount = 8;

    private SerialPort port;
    private Thread readThread;
    private volatile bool reading;
    private volatile byte sensorMask;//受信スレッドが書き、Updateが読む


    void Awake()
    {
        if (instance == null)
        {
            instance = this;
        }
    }

    void Update()
    {
        //Array.Copy(元, 先, 個数): 配列の中身をコピーする。laneHeldの内容をprevHeldにコピー
        Array.Copy(laneHeld, prevHeld, LaneCount);

        //現在のレーン押下情報の取得
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
        byte mask = sensorMask;
        for (int k = 0; k < sensorCount; k++)
        {
            bool pressed = (mask & (1 << k)) != 0
                || (k < testKeys.Length && Input.GetKey(testKeys[k]));
            if (!pressed) continue;
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

    void OnEnable()
    {
        try
        {
            port = new SerialPort(portName, baudRate);
            port.DtrEnable = true;
            port.ReadTimeout = 100;
            port.Open();
        }
        catch (Exception e)
        {
            Debug.LogWarning("コントローラーに接続できません: " + e.Message);
            port = null;
            return;//キーボードのみで動作
        }
        reading = true;
        readThread = new Thread(ReadLoop) { IsBackground = true };
        readThread.Start();
    }

    void OnDisable()
    {
        reading = false;
        if (readThread != null) readThread.Join(300);
        if (port != null && port.IsOpen) port.Close();
    }

    private void ReadLoop()
    {
        while (reading)
        {
            try
            {
                if (port.ReadByte() != packetHeader) continue;
                sensorMask = (byte)port.ReadByte();
            }
            catch (TimeoutException)
            {
                sensorMask = 0;//データが途切れたら押しっぱなしにしない
            }
            catch (Exception)
            {
                break;//切断
            }
        }
        sensorMask = 0;
    }
}
