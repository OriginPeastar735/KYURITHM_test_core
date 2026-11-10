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
    //誤作動として無視しているレーン。一度離れたと報告されたら無視をやめる
    //InputManagerはシーンごとに作り直されるので、staticにしてシーンが変わっても無視を続ける
    private static uint ignoredMask;

    [Header("入力元の切り替え")]
    [SerializeField] private InputSource source = InputSource.Both;
    [SerializeField] private KeyCode switchKey = KeyCode.F1;//プレイ中に押すと Keyboard → Controller → Both の順に切り替わる
    [SerializeField] private KeyCode resetKey = KeyCode.F5;//押すと、今タッチ中になっているセンサーを誤作動として扱いリセットする（どのシーンでも効く）

    [SerializeField] private KeyCode skipKey = KeyCode.F12;//運営の全スキップ。今のプレイを打ち切ってオープニングへ戻る（どのシーンでも効く）

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
    //ポートは自動で探す（パケットが届くポートを選ぶ）ので、普通は合わせなくてよい。ここに書いたポートは先に試す
    [SerializeField] private string portName = "COM3";
    private const int baudRate = 115200;
    private const byte packetHeader = 0xAA;
    private const int payloadBytes = LaneCount / 8;//4バイト
    private const float RetrySeconds = 1f;//見つからない・切れたときに探し直す間隔
    private const float SilentSeconds = 2f;//つながっているのにこの秒数パケットが来なければ、切れたものとして探し直す
    private const float MissingNoticeSeconds = 3f;//この秒数探しても見つからなければ画面に知らせる

    //受信スレッド1本分。止めるときはstopを立てる（スレッドがポートを開いている途中でも、終わったら自分で閉じる）
    private class ReadSession
    {
        public volatile bool stop;
    }
    private ReadSession session;
    private Thread readThread;
    private volatile SerialPort activePort;//今つながっているポート（受信スレッドが入れる）。F5のリセットを送るのに使う

    //受信スレッドからメインスレッドへの、接続状態のお知らせ（Notice.Showはメインスレッドでしか呼べない）
    private enum ConnectionEvent { Connected, Lost, Missing }
    private readonly ConcurrentQueue<(ConnectionEvent type, string port)> connectionEvents = new ConcurrentQueue<(ConnectionEvent, string)>();
    //InputManagerはシーンごとに作り直されるので、最後につながったポートと画面に知らせた状態はstaticで持つ
    private static volatile string lastPort;//最後につながったポート。次に探すとき最初に試す
    private static int noticedState;//0: まだ知らせていない  1: つながったと知らせた  2: つながっていないと知らせた

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
        if (Input.GetKeyDown(resetKey)) ResetStuckSensors();
        if (Input.GetKeyDown(skipKey)) SceneLoader.ReturnToTitle();
        ShowConnectionNotices();

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
            ignoredMask &= e.mask;//離れたと報告されたレーンは、次に触れたら普通に反応させる
            uint pressed = e.mask & ~controllerMask & ~ignoredMask;
            for (int lane = 0; lane < LaneCount; lane++)
            {
                if ((pressed & (1u << lane)) != 0) MarkDown(lane, e.timestamp);
            }
            controllerMask = e.mask;
        }

        uint held = controllerMask & ~ignoredMask;
        for (int lane = 0; lane < LaneCount; lane++)
        {
            if ((held & (1u << lane)) != 0) laneHeld[lane] = true;
        }
    }

    //センサーの誤作動（触れていないのにタッチ中になり続ける）を直す
    //  1. タッチ中のレーンを、離れたと報告されるまで無視する（コントローラー側で直らなくても、ゲームはすぐ遊べる）
    //  2. タッチ中のレーンがあるMPR121（8レーンずつ）の番号 '0'〜'3' をコントローラーに送り、基準値を取り直してもらう
    //     （古いコントローラーのプログラムは知らない文字を読み飛ばすので、送っても問題ない）
    public void ResetStuckSensors()
    {
        if (controllerMask == 0)
        {
            Notice.Show("センサーリセット: タッチ中のレーンはありません");
            return;
        }
        ignoredMask |= controllerMask;

        var units = new List<byte>();
        for (int unit = 0; unit < LaneCount / 8; unit++)
        {
            if (((controllerMask >> (unit * 8)) & 0xFF) != 0) units.Add((byte)('0' + unit));
        }
        var lanes = new List<int>();
        for (int lane = 0; lane < LaneCount; lane++)
        {
            if ((controllerMask & (1u << lane)) != 0) lanes.Add(lane);
        }
        Notice.Show($"センサーをリセットしました（レーン {string.Join(", ", lanes)}）");
        try
        {
            SerialPort port = activePort;
            if (port != null && port.IsOpen) port.Write(units.ToArray(), 0, units.Count);
        }
        catch (Exception e)
        {
            Debug.LogWarning("コントローラーにリセットを送れませんでした: " + e.Message);
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

    //「何か押したら進む」画面用。センサーのどこか、またはキーボードの何かが押されたか
    //運営用のキー（F1の入力切替・F5のセンサーリセット・F9の画面合わせ・F12のスキップなど）では進まないよう、ファンクションキーは除く
    public static bool AnyPressed()
    {
        if (instance != null && instance.AnyDown(0, LaneCount)) return true;
        if (!Input.anyKeyDown) return false;
        for (KeyCode key = KeyCode.F1; key <= KeyCode.F15; key++)
        {
            if (Input.GetKeyDown(key)) return false;
        }
        return true;
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

    //受信スレッドを始める。コントローラーを探してつなぐのも、切れたときにつなぎ直すのも受信スレッドが行う
    //（ポートを開くのに時間がかかることがあるので、メインスレッドでは開かない）
    private void OpenController()
    {
        if (session != null) return;//受信中
        session = new ReadSession();
        ReadSession s = session;
        readThread = new Thread(() => ReadLoop(s)) { IsBackground = true };
        readThread.Start();
    }

    private void CloseController()
    {
        if (session != null) session.stop = true;
        session = null;
        if (readThread != null) readThread.Join(300);//ポートは受信スレッドが終わるときに閉じる
        readThread = null;
        while (sensorEvents.TryDequeue(out _)) { }
        controllerMask = 0;
        //ignoredMaskは消さない（シーンの切り替えでも呼ばれるので、消すと次のシーンでまた押しっぱなしになる）
    }

    //受信スレッドからの接続状態を画面に知らせる。同じ状態を続けて知らせない（シーンが変わるたびにつなぎ直すため）
    private void ShowConnectionNotices()
    {
        while (connectionEvents.TryDequeue(out var e))
        {
            switch (e.type)
            {
                case ConnectionEvent.Connected:
                    if (noticedState != 1) Notice.Show($"コントローラーに接続しました（{e.port}）");
                    noticedState = 1;
                    break;
                case ConnectionEvent.Lost:
                    Notice.Show("コントローラーとの接続が切れました。自動でつなぎ直します");
                    noticedState = 2;
                    break;
                case ConnectionEvent.Missing:
                    if (noticedState != 2) Notice.Show("コントローラーが見つかりません。自動で探し続けます");
                    noticedState = 2;
                    break;
            }
        }
    }

    //コントローラーを探してつなぎ、パケットを読み続ける。切れたら探し直す
    private void ReadLoop(ReadSession s)
    {
        long searchStart = Stopwatch.GetTimestamp();
        bool missingNoticed = false;
        while (!s.stop)
        {
            SerialPort port = FindController(s);
            if (port == null)
            {
                if (!missingNoticed && SecondsSince(searchStart) >= MissingNoticeSeconds)
                {
                    connectionEvents.Enqueue((ConnectionEvent.Missing, null));
                    missingNoticed = true;
                }
                Wait(s, RetrySeconds);
                continue;
            }

            lastPort = port.PortName;
            activePort = port;
            connectionEvents.Enqueue((ConnectionEvent.Connected, port.PortName));
            ReadPackets(port, s);
            activePort = null;
            ClosePort(port);
            if (s.stop) break;

            connectionEvents.Enqueue((ConnectionEvent.Lost, null));
            searchStart = Stopwatch.GetTimestamp();
            missingNoticed = true;//切れたことは知らせたので、見つからないお知らせは重ねない
            Wait(s, RetrySeconds);
        }
    }

    //パケットを読み、押下状態が変わったときだけ受信した時刻と一緒に記録する。切れたら（または止められたら）戻る
    private void ReadPackets(SerialPort port, ReadSession s)
    {
        uint lastMask = 0;
        void Push(uint mask)
        {
            if (s.stop || mask == lastMask) return;
            sensorEvents.Enqueue(new SensorEvent { timestamp = Stopwatch.GetTimestamp(), mask = mask });
            lastMask = mask;
        }

        long lastPacket = Stopwatch.GetTimestamp();
        while (!s.stop)
        {
            try
            {
                //ヘッダーが来るまで読み飛ばし、続く4バイトを32レーン分のビットにまとめる
                if (port.ReadByte() != packetHeader)
                {
                    if (SecondsSince(lastPacket) >= SilentSeconds) break;//パケットではないデータしか来ない
                    continue;
                }
                uint mask = 0;
                for (int i = 0; i < payloadBytes; i++)
                {
                    int b = port.ReadByte();
                    if (b < 0) throw new TimeoutException();
                    mask |= (uint)b << (i * 8);
                }
                Push(mask);
                lastPacket = Stopwatch.GetTimestamp();
            }
            catch (TimeoutException)
            {
                Push(0);//データが途切れたら押しっぱなしにしない
                if (SecondsSince(lastPacket) >= SilentSeconds) break;//コントローラーが止まった・抜かれた
            }
            catch (Exception)
            {
                break;//切断
            }
        }
        Push(0);
    }

    //コントローラーのポートを探して開く。最後につながったポート → Inspectorのポート → 残りのポートの順に試す。無ければnull
    private SerialPort FindController(ReadSession s)
    {
        var candidates = new List<string>();
        void Add(string name)
        {
            if (!string.IsNullOrEmpty(name) && !candidates.Contains(name)) candidates.Add(name);
        }
        Add(lastPort);
        Add(portName);
        try
        {
            string[] names = SerialPort.GetPortNames();
            Array.Sort(names, StringComparer.Ordinal);
            foreach (string name in names) Add(name);
        }
        catch (Exception)
        {
            //ポートの一覧が取れなくても、上の2つは試す
        }

        foreach (string name in candidates)
        {
            if (s.stop) return null;
            SerialPort port = null;
            try
            {
                port = new SerialPort(name, baudRate);
                port.DtrEnable = true;
                port.ReadTimeout = 100;
                port.Open();
                if (IsController(port)) return port;
            }
            catch (Exception)
            {
                //開けない・読めないポートは飛ばす
            }
            ClosePort(port);
        }
        return null;
    }

    //コントローラーのパケット（ヘッダー＋4バイト）が続けて届くポートか。別の機器のポートを選ばないための確認
    private static bool IsController(SerialPort port)
    {
        const int packetsNeeded = 3;
        const float checkSeconds = 0.5f;
        long start = Stopwatch.GetTimestamp();
        int packets = 0;
        while (SecondsSince(start) < checkSeconds)
        {
            if (port.ReadByte() != packetHeader)//届かなければTimeoutExceptionになり、呼び出し側で飛ばされる
            {
                packets = 0;
                continue;
            }
            for (int i = 0; i < payloadBytes; i++) port.ReadByte();
            packets++;
            if (packets >= packetsNeeded) return true;
        }
        return false;
    }

    private static void ClosePort(SerialPort port)
    {
        try
        {
            if (port != null && port.IsOpen) port.Close();
        }
        catch (Exception)
        {
            //抜かれたポートは閉じるときにも例外が出ることがある
        }
    }

    private static double SecondsSince(long timestamp)
    {
        return (Stopwatch.GetTimestamp() - timestamp) / (double)Stopwatch.Frequency;
    }

    //止められたらすぐ戻れるよう、短く区切って待つ
    private static void Wait(ReadSession s, float seconds)
    {
        long start = Stopwatch.GetTimestamp();
        while (!s.stop && SecondsSince(start) < seconds) Thread.Sleep(50);
    }
}
