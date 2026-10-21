using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System.IO.Ports;
using System.Threading;

//入力元。Inspectorまたはプレイ中にswitchKeyで切り替える
public enum InputSource
{
    Keyboard,//キーボードのみ（コントローラー無しでのテストプレイ用）
    Controller,//専用コントローラーのみ（本番用）
    Both//両方（どちらで押しても反応する）
}

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

    [Header("入力元の切り替え")]
    [SerializeField] private InputSource source = InputSource.Both;
    [SerializeField] private KeyCode switchKey = KeyCode.F1;//プレイ中に押すと Keyboard → Controller → Both の順に切り替わる

    //テストプレイ用キー配置（左から順に4レーンずつ担当）
    [SerializeField]
    private KeyCode[] testKeys =
    {
        KeyCode.A, KeyCode.S, KeyCode.D, KeyCode.F,
        KeyCode.J, KeyCode.K, KeyCode.L, KeyCode.Semicolon
    };
    private const int lanesPerKey = 4;


    //専用コントローラー（Arduino）のパケット: ヘッダー0xAA ＋ 4バイト = 5バイト
    //  1バイト目: lane 0〜7（左斜面）  2バイト目: lane 8〜15（底面）
    //  3バイト目: lane16〜23（底面）    4バイト目: lane24〜31（右斜面）
    //  各バイトの最下位ビット(bit0)が一番小さいレーン番号。1なら押されている
    [SerializeField] private string portName = "COM5"; //シリアルポート名
    private const int baudRate = 115200;
    private const byte packetHeader = 0xAA;
    private const int payloadBytes = LaneCount / 8;//4バイト

    private SerialPort port;
    private Thread readThread;
    private volatile bool reading;
    private volatile uint sensorMask;//32レーン分の押下状態(bit i = lane i)。受信スレッドが書き、Updateが読む

    private bool UseKeyboard => source != InputSource.Controller;
    private bool UseController => source != InputSource.Keyboard;


    void Awake()
    {
        if (instance == null)
        {
            instance = this;
        }
    }

    void Update()
    {
        if (Input.GetKeyDown(switchKey))
        {
            SetSource((InputSource)(((int)source + 1) % 3));
        }

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

    //入力元を切り替える。コントローラーを使わないモードではシリアルポートを閉じる
    public void SetSource(InputSource newSource)
    {
        source = newSource;
        if (UseController) OpenController();
        else CloseController();
        Debug.Log($"入力元: {source}");
    }

    //現在押されているレーンをheldに書き込む
    private void ReadInput(bool[] held)
    {
        Array.Clear(held, 0, LaneCount);

        if (UseController)
        {
            uint mask = sensorMask;
            for (int lane = 0; lane < LaneCount; lane++)
            {
                if ((mask & (1u << lane)) != 0) held[lane] = true;
            }
        }

        if (UseKeyboard)
        {
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
        if (UseController) OpenController();
    }

    void OnDisable()
    {
        CloseController();
    }

    private void OpenController()
    {
        if (port != null) return;//接続済み
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

    private void CloseController()
    {
        reading = false;
        if (readThread != null) readThread.Join(300);
        readThread = null;
        if (port != null && port.IsOpen) port.Close();
        port = null;
        sensorMask = 0;
    }

    private void ReadLoop()
    {
        while (reading)
        {
            try
            {
                //ヘッダーが来るまで読み飛ばし、続く4バイトを32レーン分のビットにまとめる
                if (port.ReadByte() != packetHeader) continue;
                uint mask = 0;
                for (int i = 0; i < payloadBytes; i++)
                {
                    int b = port.ReadByte();
                    if (b < 0) throw new TimeoutException();
                    mask |= (uint)b << (i * 8);
                }
                sensorMask = mask;
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
