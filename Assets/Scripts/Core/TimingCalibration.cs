using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using static UIUtil;
using Stopwatch = System.Diagnostics.Stopwatch;

//筐体のタイミング合わせ。音と映像の遅れを測って、楽曲タイミング・判定タイミングの筐体の調整を決める（画面合わせの後に出す）
//
//  HDMIでテレビにつなぐと、テレビの映像処理やスピーカーの分だけ音と映像が遅れる。遅れはつないだ機材で決まるので、筐体の設定として保存する
//  1. 映像: 音を出さずに線を流し、判定ラインに重なったらタップしてもらう → 映像の遅れ V
//  2. 音: 映像を出さずにクリック音を鳴らし、音に合わせてタップしてもらう → 音の遅れ A
//  3. 確認: 両方を出して、ずれていないか確かめる。キーボードで1ms単位に直せる
//  合わせ済みの筐体でやり直すとき（オープニングでF9）は、今の値で3から始める。測り直すときはRで1から
//  ゲームの時計を音より A - V 遅らせると、ノーツが判定ラインに見えるのと音が聞こえるのが同時になる（楽曲タイミング = A - V）
//  そのとき見た目どおりに押した時刻は、ゲームの時計で V 遅れる（判定タイミング = V）
//  ばらつきが大きいときは測り直し、1回ごとの外れ値に引っぱられないよう中央値を使う
//
//  時計はMusicManagerと同じ作り方（StopwatchをdspTimeに寄せる）にして、測ったときとプレイ中で時計のくせが変わらないようにする
public class TimingCalibration : MonoBehaviour
{
    enum Step { Visual, Audio, Check }

    const float BeatSeconds = 0.6f;//クリック・線の間隔（秒）。実際はサンプル数に丸めた値を使う
    const float LeadIn = 1.5f;//始めてから最初の拍までの秒
    const int WarmupBeats = 4;//最初の何拍はリズムをつかむ間として数えない
    const int TapsNeeded = 20;//1回の測定に使うタップの数
    const float TapDebounce = 0.15f;//これより短い間隔のタップは同じタップとして扱う（手のひらで複数のセンサーに触れたとき）
    const float MaxSpreadMs = 35f;//タップのずれの標準偏差がこれより大きければ測り直す
    const float ResultSeconds = 2.5f;//測り終えた結果を見せてから次へ進むまで
    const int OffsetRange = 300;//調整できる範囲(ms)
    const int CheckAverageCount = 8;//確認で平均を出すタップの数

    //線を流すレーン（キャンバスの中央基準）
    const float LaneTop = 230f;
    const float LineY = -250f;
    const float LaneWidth = 360f;
    const float FallSpeed = 500f;//線の速さ（1秒あたり）
    const float FadeSeconds = 0.15f;//判定ラインを過ぎた線が消えるまで
    const int MarkerCount = 6;

    static readonly Color BackgroundColor = new Color(0.05f, 0.06f, 0.12f);
    static readonly Color AccentColor = new Color(0.3f, 0.8f, 1f);
    static readonly Color MarkerColor = new Color(1f, 0.4f, 0.75f);
    static readonly Color DimTextColor = new Color(1f, 1f, 1f, 0.75f);

    //MusicManagerと同じ、dspTimeに寄せたStopwatchの時計。0が最初の拍
    class Clock
    {
        const double FollowRate = 0.05;
        const double SnapSeconds = 0.05;
        public double DspStart { get; private set; }
        private long startTimestamp;
        private long frameTimestamp;
        private double correction;
        public float Time { get; private set; }

        public void Start(float leadIn)
        {
            DspStart = AudioSettings.dspTime + leadIn;
            startTimestamp = Stopwatch.GetTimestamp() + (long)(leadIn * Stopwatch.Frequency);
            correction = 0;
            Update();
        }

        public void Update()
        {
            frameTimestamp = Stopwatch.GetTimestamp();
            double elapsed = (frameTimestamp - startTimestamp) / (double)Stopwatch.Frequency;
            double error = (AudioSettings.dspTime - DspStart) - (elapsed + correction);
            correction += System.Math.Abs(error) > SnapSeconds ? error : error * FollowRate;
            Time = (float)(elapsed + correction);
        }

        //Stopwatch.GetTimestamp()で取った時刻が、この時計で何秒か
        public float ToTime(long timestamp)
        {
            return Time + (float)((timestamp - frameTimestamp) / (double)Stopwatch.Frequency);
        }
    }

    private readonly Clock clock = new Clock();
    private AudioSource click;
    private float beat;//実際の拍の間隔（クリック音のサンプル数に丸めた値）

    private Step step;
    private bool measuring;//タップを集めている間
    private float nextStepTime;//測り終えた後、この時刻（unscaledTime）に次へ進む
    private bool retry;//ばらつきが大きかったので、同じ手順をやり直す
    private readonly List<float> diffs = new List<float>();//拍からのずれ（秒）
    private float lastTapTime = float.NegativeInfinity;
    private float visualMs, audioMs;//測った映像・音の遅れ
    private int songOffsetMs, judgeOffsetMs;

    private RectTransform canvas;
    private Text titleText, guideText, keysText, progressText, valueText;
    private RectTransform lane;
    private Image line;
    private readonly Image[] markers = new Image[MarkerCount];

    void Start()
    {
        songOffsetMs = DisplayFit.Current.songOffsetMs;
        judgeOffsetMs = DisplayFit.Current.judgeOffsetMs;
        CreateClick();
        BuildCanvas();
        StartStep(DisplayFit.Current.timingCalibrated ? Step.Check : Step.Visual);
    }

    //拍の頭で鳴るクリック音を1拍ぶんの長さで作り、ループさせる（ループはサンプル単位で正確なので拍がずれていかない）
    void CreateClick()
    {
        int rate = AudioSettings.outputSampleRate;
        int samples = Mathf.RoundToInt(rate * BeatSeconds);
        beat = samples / (float)rate;

        var data = new float[samples];
        int length = Mathf.Min(samples, Mathf.RoundToInt(rate * 0.03f));
        for (int i = 0; i < length; i++)
        {
            //立ち上がりがはっきりするよう、最初のサンプルから鳴らしてすぐ減衰させる
            data[i] = Mathf.Sin(2f * Mathf.PI * 1500f * i / rate) * Mathf.Exp(-i / (rate * 0.006f)) * 0.8f;
        }
        AudioClip clip = AudioClip.Create("Click", samples, 1, rate, false);
        clip.SetData(data, 0);

        click = gameObject.AddComponent<AudioSource>();
        click.clip = clip;
        click.loop = true;
        click.playOnAwake = false;
        click.spatialBlend = 0f;
    }

    void BuildCanvas()
    {
        canvas = CreateCanvas("TimingCalibration", 300, 0f);//シーンの切り替えの覆い(200)よりも手前に描く
        DontDestroyOnLoad(canvas.gameObject);
        Font font = CreateFont();

        Stretch(CreateImage("Background", canvas, BackgroundColor).rectTransform);

        titleText = CreateText("Title", canvas, font, Color.white, "", 60, TextAnchor.MiddleCenter, FontStyle.Bold);
        Place(titleText.rectTransform, new Vector2(0f, 440f), new Vector2(1800f, 80f));
        guideText = CreateText("Guide", canvas, font, Color.white, "", 38, TextAnchor.MiddleCenter);
        Place(guideText.rectTransform, new Vector2(0f, 360f), new Vector2(1800f, 60f));
        keysText = CreateText("Keys", canvas, font, DimTextColor, "", 30, TextAnchor.MiddleCenter);
        Place(keysText.rectTransform, new Vector2(0f, 300f), new Vector2(1800f, 50f));

        lane = CreateImage("Lane", canvas, new Color(1f, 1f, 1f, 0.06f)).rectTransform;
        Place(lane, new Vector2(0f, (LaneTop + LineY) / 2f), new Vector2(LaneWidth, LaneTop - LineY));
        for (int i = 0; i < MarkerCount; i++)
        {
            markers[i] = CreateImage("Marker", canvas, MarkerColor);
            markers[i].rectTransform.sizeDelta = new Vector2(LaneWidth, 20f);
        }
        line = CreateImage("Line", canvas, AccentColor);
        Place(line.rectTransform, new Vector2(0f, LineY), new Vector2(LaneWidth + 80f, 10f));

        progressText = CreateText("Progress", canvas, font, AccentColor, "", 48, TextAnchor.MiddleCenter, FontStyle.Bold);
        Place(progressText.rectTransform, new Vector2(0f, -330f), new Vector2(1800f, 70f));
        valueText = CreateText("Value", canvas, font, DimTextColor, "", 34, TextAnchor.MiddleCenter);
        Place(valueText.rectTransform, new Vector2(0f, -400f), new Vector2(1800f, 50f));
    }

    void StartStep(Step next)
    {
        step = next;
        measuring = step != Step.Check;
        retry = false;
        diffs.Clear();
        lastTapTime = float.NegativeInfinity;
        clock.Start(LeadIn);

        click.Stop();
        if (step != Step.Visual) click.PlayScheduled(clock.DspStart);
        bool showLane = step != Step.Audio;
        lane.gameObject.SetActive(showLane);
        line.gameObject.SetActive(showLane);
        foreach (Image marker in markers) marker.gameObject.SetActive(showLane);

        switch (step)
        {
            case Step.Visual:
                titleText.text = "タイミング合わせ 1/3（映像）";
                guideText.text = "音は鳴りません。線が水色のラインに重なった瞬間に、センサーをタップしてください";
                keysText.text = "R : やり直す　　Esc : スキップ（今の設定のまま）";
                break;
            case Step.Audio:
                titleText.text = "タイミング合わせ 2/3（音）";
                guideText.text = "画面は見ずに、クリック音に合わせてセンサーをタップしてください";
                keysText.text = "R : やり直す　　Esc : スキップ（今の設定のまま）";
                break;
            case Step.Check:
                titleText.text = "タイミング合わせ 3/3（確認）";
                guideText.text = "線とクリック音が同時か確かめて、タップのずれが0付近になるか見てください";
                keysText.text = "← → : 楽曲タイミング　　↓ ↑ : 判定タイミング　　Shift : 5ms刻み　　R : 1から測り直す　　Enter : 確定　　Esc : 変えずに閉じる";
                break;
        }
        progressText.text = step == Step.Check ? "線に合わせてタップすると、ずれを表示します" : "";
        valueText.text = step == Step.Check ? ValueLine() : "";
    }

    void Update()
    {
        clock.Update();

        if (Input.GetKeyDown(KeyCode.Escape))
        {
            Close(false);
            return;
        }
        if (Input.GetKeyDown(KeyCode.R))
        {
            StartStep(step == Step.Check ? Step.Visual : step);
            return;
        }

        if (step == Step.Check) UpdateCheckKeys();
        UpdateMarkers();

        if (TryGetTap(out long timestamp)) OnTap(clock.ToTime(timestamp));

        if (measuring)
        {
            progressText.text = clock.Time < (WarmupBeats - 0.5f) * beat ? "リズムをつかんでください…" : $"タップ {diffs.Count} / {TapsNeeded}";
        }
        else if (step != Step.Check && Time.unscaledTime >= nextStepTime)
        {
            if (retry) StartStep(step);
            else if (step == Step.Visual) StartStep(Step.Audio);
            else
            {
                //映像の遅れぶん判定を遅らせ、音と映像の遅れの差ぶんノーツを曲より遅らせる
                songOffsetMs = Mathf.Clamp(Mathf.RoundToInt(audioMs - visualMs), -OffsetRange, OffsetRange);
                judgeOffsetMs = Mathf.Clamp(Mathf.RoundToInt(visualMs), -OffsetRange, OffsetRange);
                StartStep(Step.Check);
            }
        }
    }

    //このフレームのタップ。手のひらで複数のセンサーに触れたときは一番早く押したセンサーの時刻にする
    //キーボードのSpaceでも叩ける（フレーム単位でしか読めないので、センサーより精度は落ちる）
    bool TryGetTap(out long timestamp)
    {
        timestamp = long.MaxValue;
        InputManager input = InputManager.instance;
        if (input != null)
        {
            for (int i = 0; i < InputManager.LaneCount; i++)
            {
                if (input.laneDown[i]) timestamp = System.Math.Min(timestamp, input.laneDownTimestamp[i]);
            }
        }
        if (timestamp == long.MaxValue && Input.GetKeyDown(KeyCode.Space)) timestamp = Stopwatch.GetTimestamp();
        return timestamp != long.MaxValue;
    }

    void OnTap(float time)
    {
        if (time - lastTapTime < TapDebounce) return;
        lastTapTime = time;

        //確認では、プレイ中と同じように楽曲タイミング・判定タイミングを引いた時刻で比べる
        if (step == Step.Check) time -= (songOffsetMs + judgeOffsetMs) / 1000f;
        int nearest = Mathf.RoundToInt(time / beat);
        float diff = time - nearest * beat;
        if (Mathf.Abs(diff) > beat * 0.4f) return;//拍と拍の間を押したものは数えない

        if (step == Step.Check)
        {
            diffs.Add(diff);
            if (diffs.Count > CheckAverageCount) diffs.RemoveAt(0);
            float last = diff * 1000f;
            float average = Average(diffs) * 1000f;
            progressText.text = $"ずれ {last:+0;-0;0}ms（{(last < 0 ? "FAST" : "LATE")}）　　直近{diffs.Count}回の平均 {average:+0;-0;0}ms";
            return;
        }

        if (!measuring || nearest < WarmupBeats) return;
        diffs.Add(diff);
        if (diffs.Count >= TapsNeeded) FinishMeasure();
    }

    void FinishMeasure()
    {
        measuring = false;
        click.Stop();
        nextStepTime = Time.unscaledTime + ResultSeconds;

        float median = Median(diffs) * 1000f;
        float spread = StandardDeviation(diffs) * 1000f;
        retry = spread > MaxSpreadMs;
        string name = step == Step.Visual ? "映像" : "音";
        if (step == Step.Visual) visualMs = median;
        else audioMs = median;
        progressText.text = retry
            ? $"ばらつきが大きいので（±{spread:0}ms）もう一度測ります"
            : $"{name}の遅れ {median:+0;-0;0}ms（ばらつき ±{spread:0}ms）";
    }

    void UpdateCheckKeys()
    {
        int amount = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift) ? 5 : 1;
        int song = 0, judge = 0;
        if (Input.GetKeyDown(KeyCode.LeftArrow)) song -= amount;
        if (Input.GetKeyDown(KeyCode.RightArrow)) song += amount;
        if (Input.GetKeyDown(KeyCode.DownArrow)) judge -= amount;
        if (Input.GetKeyDown(KeyCode.UpArrow)) judge += amount;
        if (song != 0 || judge != 0)
        {
            songOffsetMs = Mathf.Clamp(songOffsetMs + song, -OffsetRange, OffsetRange);
            judgeOffsetMs = Mathf.Clamp(judgeOffsetMs + judge, -OffsetRange, OffsetRange);
            diffs.Clear();//調整前のずれは平均に入れない
            valueText.text = ValueLine();
        }

        if (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter)) Close(true);
    }

    string ValueLine()
    {
        return $"楽曲タイミング {songOffsetMs:+0;-0;0}ms　　判定タイミング {judgeOffsetMs:+0;-0;0}ms";
    }

    //拍ごとの線を流す。確認ではプレイ中と同じく、楽曲タイミングのぶん音より遅れて判定ラインに来る
    void UpdateMarkers()
    {
        if (step == Step.Audio) return;
        float time = clock.Time - (step == Step.Check ? songOffsetMs / 1000f : 0f);
        int first = Mathf.CeilToInt((time - FadeSeconds) / beat);
        for (int i = 0; i < MarkerCount; i++)
        {
            int k = first + i;
            float until = k * beat - time;//判定ラインに来るまでの秒（過ぎたらマイナス）
            float y = LineY + until * FallSpeed;
            bool visible = k >= 0 && y <= LaneTop;
            markers[i].enabled = visible;
            if (!visible) continue;
            markers[i].rectTransform.anchoredPosition = new Vector2(0f, Mathf.Max(y, LineY));
            Color c = MarkerColor;
            c.a = until >= 0f ? 1f : 1f - Mathf.Clamp01(-until / FadeSeconds);
            markers[i].color = c;
        }

        //線が重なった瞬間はラインを白く光らせる
        float sinceBeat = time - Mathf.Floor(time / beat) * beat;
        line.color = time >= 0f && sinceBeat < 0.08f ? Color.white : AccentColor;
    }

    //saveがfalseなら（スキップ）、今の設定のまま閉じる
    void Close(bool save)
    {
        DisplayFit.Settings settings = DisplayFit.Current.Clone();
        if (save)
        {
            settings.songOffsetMs = songOffsetMs;
            settings.judgeOffsetMs = judgeOffsetMs;
        }
        settings.timingCalibrated = true;
        DisplayFit.Confirm(settings);
        DisplayFit.EndSetup();
        Destroy(canvas.gameObject);
        Destroy(gameObject);
    }

    static float Average(List<float> values)
    {
        float sum = 0f;
        foreach (float v in values) sum += v;
        return values.Count > 0 ? sum / values.Count : 0f;
    }

    static float Median(List<float> values)
    {
        var sorted = new List<float>(values);
        sorted.Sort();
        int n = sorted.Count;
        return n % 2 == 1 ? sorted[n / 2] : (sorted[n / 2 - 1] + sorted[n / 2]) / 2f;
    }

    static float StandardDeviation(List<float> values)
    {
        float average = Average(values);
        float sum = 0f;
        foreach (float v in values) sum += (v - average) * (v - average);
        return Mathf.Sqrt(sum / values.Count);
    }
}
