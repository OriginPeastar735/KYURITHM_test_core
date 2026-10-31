using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using Newtonsoft.Json;
using UnityEngine;

//画面を大きなテレビに映すとき、ゲームのウィンドウを小さくして筐体のセンサーに合わせる
//
//  ウィンドウを枠なしにし、テレビの画面に対する大きさ（縦横比は画面と同じ）と位置を設定の値にする
//  ゲームの画面はウィンドウの大きさに合わせて描かれるので、UIもプレイ画面もセンサーとの位置関係を保ったまま小さくなる
//  ウィンドウの外にはデスクトップが見えるので、展示用のPCは壁紙を黒にし、タスクバーを自動で隠し、通知を切っておく
//
//  設定は display_fit.json（Application.persistentDataPath の中）に保存する
//  起動時にファイルがあれば読み込んで使い、無ければ DisplayFitSetup で合わせてもらってから保存する
//  同じファイルに筐体のタイミングの調整（音と映像の遅れ）も保存する。画面合わせを確定すると TimingCalibration に進む
//  合わせ直すときは、オープニングでF9を押す（今の設定から始めるので、少し直すだけでよい）
//
//  ウィンドウを動かすのはWindowsのビルドだけ（エディタで再生したときは、エディタのウィンドウを動かさないよう何もしない）
public class DisplayFit : MonoBehaviour
{
    [Serializable]
    public class Settings
    {
        public float scale = 1f;//テレビの画面に対するウィンドウの大きさ（1で全画面）
        public float left;//ウィンドウの左端の位置（画面の幅に対する割合）
        public float bottom;//ウィンドウの下端の位置（画面の高さに対する割合）

        //筐体のタイミングの調整(ms)。選曲画面の設定に足して使う（PlaySession.SongOffsetMs・JudgeOffsetMsを参照）
        public int songOffsetMs;
        public int judgeOffsetMs;
        public bool timingCalibrated;//タイミング合わせを済ませたか

        public Settings Clone()
        {
            return (Settings)MemberwiseClone();
        }
    }

    public const float MinScale = 0.3f;
    const float ApplyInterval = 0.1f;//設定画面でキーを押し続けている間、ウィンドウを動かす間隔（秒）。毎フレーム動かすと重いため

    public static string FilePath => Path.Combine(Application.persistentDataPath, "display_fit.json");

    public static Settings Current { get; private set; } = new Settings();//今使っている設定。設定画面では動かすたびに変わる
    public static bool IsSetupOpen { get; private set; }//初回の画面合わせ・タイミング合わせ中。オープニングはこの間の入力を受け付けない

#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
    private IntPtr window;
    private Vector2Int requestedSize;//最後にUnityに頼んだゲーム画面の大きさ
    private RectInt appliedRect;//最後にウィンドウに設定した位置と大きさ（デスクトップ上のピクセル）
    private float lastApplyTime = float.NegativeInfinity;

    //最初のシーンを読み込む前に、シーンが変わっても残るオブジェクトを作る
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void Boot()
    {
        var host = new GameObject("DisplayFit");
        DontDestroyOnLoad(host);
        host.AddComponent<DisplayFit>();

        Settings saved = Load();
        Current = saved ?? new Settings();
        if (saved == null) OpenSetup();
    }
#endif

    //画面合わせ → タイミング合わせの順に設定画面を出す（初回起動時、またはオープニングでF9を押したとき）
    public static void OpenSetup()
    {
        IsSetupOpen = true;
        var host = new GameObject("DisplayFitSetup");
        DontDestroyOnLoad(host);
        host.AddComponent<DisplayFitSetup>();
    }

    //タイミング合わせの画面を出す（画面合わせを確定した後）
    public static void OpenTimingCalibration()
    {
        IsSetupOpen = true;
        var host = new GameObject("TimingCalibration");
        DontDestroyOnLoad(host);
        host.AddComponent<TimingCalibration>();
    }

    //画面合わせ・タイミング合わせがすべて終わったときに呼ぶ
    public static void EndSetup()
    {
        IsSetupOpen = false;
    }

    //設定画面で動かした値をすぐウィンドウに反映する
    public static void Preview(Settings settings)
    {
        Current = Clamp(settings.Clone());
    }

    //設定画面で確定したときに呼ぶ。保存できなくても今回の起動中はその設定で動かす
    public static void Confirm(Settings settings)
    {
        Preview(settings);
#if UNITY_EDITOR
        //エディタとビルドは保存先が同じなので、エディタで試したときにビルドの画面合わせを上書きしないよう保存しない
        Debug.Log("エディタでは筐体の設定を保存しません（今回の再生中だけ使います）");
#else
        try
        {
            File.WriteAllText(FilePath, JsonConvert.SerializeObject(Current, Formatting.Indented));
            Debug.Log($"筐体の設定を保存しました: {FilePath}");
        }
        catch (Exception e)
        {
            Debug.LogWarning($"筐体の設定を保存できませんでした: {FilePath}\n{e.Message}");
        }
#endif
    }

    //小さくしても画面の外にはみ出さないようにする
    public static Settings Clamp(Settings settings)
    {
        settings.scale = Mathf.Clamp(settings.scale, MinScale, 1f);
        settings.left = Mathf.Clamp(settings.left, 0f, 1f - settings.scale);
        settings.bottom = Mathf.Clamp(settings.bottom, 0f, 1f - settings.scale);
        return settings;
    }

    //ファイルが無い・読めないときはnull（画面合わせをやり直す）
    static Settings Load()
    {
        if (!File.Exists(FilePath)) return null;
        try
        {
            Settings settings = JsonConvert.DeserializeObject<Settings>(File.ReadAllText(FilePath));
            if (settings != null) return Clamp(settings);
        }
        catch (Exception e)
        {
            Debug.LogWarning($"画面の大きさの設定を読めませんでした: {FilePath}\n{e.Message}");
        }
        return null;
    }

#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
    //設定とウィンドウがずれていたら合わせる。全画面とウィンドウの切り替えは次のフレーム以降に効くので、数フレームかけて合わせる
    void Update()
    {
        if (Time.unscaledTime - lastApplyTime < ApplyInterval) return;

        if (window == IntPtr.Zero) window = FindGameWindow();
        if (window == IntPtr.Zero) return;
        var monitorInfo = new MonitorInfo { size = Marshal.SizeOf<MonitorInfo>() };
        if (!GetMonitorInfo(MonitorFromWindow(window, MonitorDefaultToNearest), ref monitorInfo)) return;
        Rect32 monitor = monitorInfo.monitor;
        int monitorWidth = monitor.right - monitor.left;
        int monitorHeight = monitor.bottom - monitor.top;

        Settings settings = Current;
        if (settings.scale >= 1f)
        {
            if (Screen.fullScreenMode != FullScreenMode.FullScreenWindow)
            {
                Screen.SetResolution(monitorWidth, monitorHeight, FullScreenMode.FullScreenWindow);
                lastApplyTime = Time.unscaledTime;
            }
            requestedSize = default;
            appliedRect = default;
            return;
        }

        int width = Mathf.RoundToInt(monitorWidth * settings.scale);
        int height = Mathf.RoundToInt(monitorHeight * settings.scale);
        var rect = new RectInt(
            monitor.left + Mathf.RoundToInt(monitorWidth * settings.left),
            monitor.bottom - Mathf.RoundToInt(monitorHeight * settings.bottom) - height,//Windowsの座標は上が0
            width, height);

        //まずUnityにウィンドウモードとゲーム画面の大きさを設定してもらい、それが効いてから枠を消して位置を決める
        //（SetResolutionはフレームの終わりに効き、ウィンドウを枠付きで画面の中央に置き直すため）
        if (Screen.fullScreenMode != FullScreenMode.Windowed || requestedSize != rect.size)
        {
            Screen.SetResolution(width, height, FullScreenMode.Windowed);
            requestedSize = rect.size;
            lastApplyTime = Time.unscaledTime;
            appliedRect = default;
            return;
        }
        if (rect.Equals(appliedRect)) return;

        SetWindowLongPtr(window, GwlStyle, (IntPtr)(WsPopup | WsVisible));
        SetWindowPos(window, IntPtr.Zero, rect.x, rect.y, rect.width, rect.height, SwpNoZOrder | SwpFrameChanged | SwpShowWindow);
        appliedRect = rect;
        lastApplyTime = Time.unscaledTime;
    }

    // ===== Windowsのウィンドウ操作 =====
    const int GwlStyle = -16;
    const uint WsPopup = 0x80000000;
    const uint WsVisible = 0x10000000;
    const uint SwpNoZOrder = 0x0004;
    const uint SwpFrameChanged = 0x0020;
    const uint SwpShowWindow = 0x0040;
    const uint MonitorDefaultToNearest = 2;

    [StructLayout(LayoutKind.Sequential)]
    struct Rect32
    {
        public int left, top, right, bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct MonitorInfo
    {
        public int size;
        public Rect32 monitor;//モニター全体（タスクバーを含む）
        public Rect32 work;
        public uint flags;
    }

    delegate bool EnumWindowsProc(IntPtr window, IntPtr param);

    [DllImport("user32.dll")] static extern bool EnumWindows(EnumWindowsProc callback, IntPtr param);
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetClassName(IntPtr window, StringBuilder className, int maxCount);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")] static extern IntPtr SetWindowLongPtr(IntPtr window, int index, IntPtr value);
    [DllImport("user32.dll")] static extern bool SetWindowPos(IntPtr window, IntPtr insertAfter, int x, int y, int width, int height, uint flags);
    [DllImport("user32.dll")] static extern IntPtr MonitorFromWindow(IntPtr window, uint flags);
    [DllImport("user32.dll")] static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);

    static IntPtr foundWindow;
    static uint currentProcessId;

    //このプロセスのUnityのウィンドウを探す
    static IntPtr FindGameWindow()
    {
        foundWindow = IntPtr.Zero;
        currentProcessId = (uint)System.Diagnostics.Process.GetCurrentProcess().Id;
        EnumWindows(CheckWindow, IntPtr.Zero);
        return foundWindow;
    }

    [AOT.MonoPInvokeCallback(typeof(EnumWindowsProc))]
    static bool CheckWindow(IntPtr window, IntPtr param)
    {
        GetWindowThreadProcessId(window, out uint processId);
        if (processId != currentProcessId) return true;
        var className = new StringBuilder(64);
        GetClassName(window, className, className.Capacity);
        if (className.ToString() != "UnityWndClass") return true;
        foundWindow = window;
        return false;//見つかったので探すのをやめる
    }
#endif
}
