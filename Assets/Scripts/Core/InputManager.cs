using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using UnityEngine;
using System.IO.Ports;
using System.Threading;
using Stopwatch = System.Diagnostics.Stopwatch;

//入力元。Inspectorまたはプレイ中にswitchKeyで切り替える
public enum InputSource
{
    Keyboard,//キーボードのみ（コントローラー無しでのテストプレイ用）
    Controller,//専用コントローラーのみ（本番用）
    Both//両方（どちらで押しても反応する）
}

//入力をレーン単位の状態（押された/押されている/離された）に変換する
//判定側はこのクラスだけを参照し、入力デバイスの違いはReadController・ReadKeyboardで吸収する
//
//押した時刻(laneDownTimestamp)はStopwatch.GetTimestamp()の値で持ち、MusicManager.ToPlayTimeで曲の時刻に直して判定に使う
//  コントローラー: 受信スレッドがパケットを受け取った時刻。フレームの間隔に関係なく押した時刻で判定できる
//  キーボード: 旧Input Managerはフレーム単位でしか読めないので、そのフレームで読んだ時刻（テスト用）
[DefaultExecutionOrder(-100)]//JudgeManagerより先に入力を更新する（MusicManagerはさらに先）
public class InputManager : MonoBehaviour
{
    public static InputManager instance;

    public const int LaneCount = 32;//レーン数（左斜面8・底面16・右斜面8）

    public bool[] laneDown = new bool[LaneCount];//このフレームで押された
    public bool[] laneHeld = new bool[LaneCount];//押されている
    public bool[] laneUp = new bool[LaneCount];//このフレームで離された
    public long[] laneDownTimestamp = new long[LaneCount];//laneDownがtrueのレーンを押した時刻（Stopwatch.GetTimestamp()の値）
    private bool[] prevHeld = new bool[LaneCount];//直前のフレームで押されている
    private bool[] keyHeld = new bool[LaneCount];//キーボードで押されている
    private bool[] prevKeyHeld = new bool[LaneCount];
    private uint controllerMask;//メインスレッドで反映済みのコントローラーの押下状態(bit i = lane i)

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

    //センサーの押下状態が変わったときの記録。受信スレッドが積み、Updateが受信順に取り出す
    private struct SensorEvent
    {
        public long timestamp;//受信した時刻（Stopwatch.GetTimestamp()の値）
        public uint mask;//変わった後の32レーン分の押下状態
    }
    private readonly ConcurrentQueue<SensorEvent> sensorEvents = new ConcurrentQueue<SensorEvent>();

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

        //前のフレームの押下状態を残してから、このフレームの状態を読み直す
        Array.Copy(laneHeld, prevHeld, LaneCount);
        Array.Clear(laneDown, 0, LaneCount);
        Array.Clear(laneHeld, 0, LaneCount);

        //押した瞬間(laneDown)と押した時刻はそれぞれの中で記録する
        if (UseController) ReadController();
        if (UseKeyboard) ReadKeyboard();

        //フレームの間に押して離した場合も、離した瞬間として扱う
        for (int i = 0; i < LaneCount; i++)
        {
            laneUp[i] = !laneHeld[i] && (prevHeld[i] || laneDown[i]);
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

    //受信スレッドがためた変化を受信した順に反映する。押された時刻は受信した時刻になる
    //フレームの間に押して離したレーンも、押した瞬間として取りこぼさない
    private void ReadController()
    {
        while (sensorEvents.TryDequeue(out SensorEvent e))
        {
            uint pressed = e.mask & ~controllerMask;
            for (int lane = 0; lane < LaneCount; lane++)
            {
                if ((pressed & (1u << lane)) != 0) MarkDown(lane, e.timestamp);
            }
            controllerMask = e.mask;
        }

        for (int lane = 0; lane < LaneCount; lane++)
        {
            if ((controllerMask & (1u << lane)) != 0) laneHeld[lane] = true;
        }
    }

    //キーボードはフレーム単位でしか読めないので、押された時刻はこのフレームで読んだ時刻にする
    private void ReadKeyboard()
    {
        long now = Stopwatch.GetTimestamp();
        Array.Copy(keyHeld, prevKeyHeld, LaneCount);
        Array.Clear(keyHeld, 0, LaneCount);

        for (int k = 0; k < testKeys.Length; k++)
        {
            if (!Input.GetKey(testKeys[k])) continue;
            for (int j = 0; j < lanesPerKey; j++)
            {
                int lane = k * lanesPerKey + j;
                if (lane < LaneCount) keyHeld[lane] = true;
            }
        }

        for (int lane = 0; lane < LaneCount; lane++)
        {
            if (keyHeld[lane] && !prevKeyHeld[lane]) MarkDown(lane, now);
            if (keyHeld[lane]) laneHeld[lane] = true;
        }
    }

    //同じフレームで2回押された場合は早い方の時刻を使う
    private void MarkDown(int lane, long timestamp)
    {
        if (laneDown[lane]) return;
        laneDown[lane] = true;
        laneDownTimestamp[lane] = timestamp;
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
        while (sensorEvents.TryDequeue(out _)) { }
        controllerMask = 0;
    }

    //押下状態が変わったときだけ、受信した時刻と一緒に記録する
    private void ReadLoop()
    {
        uint lastMask = 0;
        void Push(uint mask)
        {
            if (mask == lastMask) return;
            sensorEvents.Enqueue(new SensorEvent { timestamp = Stopwatch.GetTimestamp(), mask = mask });
            lastMask = mask;
        }

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
                Push(mask);
            }
            catch (TimeoutException)
            {
                Push(0);//データが途切れたら押しっぱなしにしない
            }
            catch (Exception)
            {
                break;//切断
            }
        }
        Push(0);
    }
}
