using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

//プレイ画面の進行。譜面の読み込みを始め、判定を受けてスコア・コンボを集計し、曲が終わったらリザルトを出す
public class GameManager : MonoBehaviour
{
    const string ComboLabelName = "COMBO";//コンボの数字といっしょに動かす見出しのオブジェクト名（Playシーン）
    const string DefaultChartName = "test_long";//Playシーン単体で起動したときに使う確認用の譜面（約100秒、全種類のノーツ入り）

    public int[] PlayResult = new int[7];//(score, combo, maxCombo, precisePlus, precise, approx, miss)
    public float playScore = 0;

    //1ノーツあたり baseScore / totalCombo 点を基準に、判定ごとの割合(%)を掛けて加算する
    //全PRECISEで100万点(ALL PRECISE)、全PRECISE+で101万点(理論値)
    public int baseScore = 1000000;
    public int totalCombo = 0;
    const int PrecisePlusRate = 101;//PRECISE+ 101%
    const int PreciseRate = 100;//PRECISE 100%
    const int ApproxRate = 50;//APPROX 50%（MISSは0%）

    [SerializeField] private TextMeshProUGUI ComboText;
    [SerializeField] private TextMeshProUGUI ScoreText;
    [SerializeField] private ResultScreen resultScreen;//全ノーツが消えたらこのパネルを割り込ませてリザルトを出す
    [SerializeField] private GameObject hideOnResult;//リザルトが出ると同時に非アクティブにするもの（プレイ中だけ使うCanvasなど）

    [SerializeField] private Renderer background;//プレイ中の背景（未設定ならメインカメラの子から探す）

    [Header("表示の調整（シーンに置いた文字を、始めるときにコードで動かす）")]
    [SerializeField] private float scoreFontScale = 1.7f;//スコアの文字の大きさ（シーンの大きさに対する倍率）
    [SerializeField] private Vector2 comboOffset = new Vector2(0f, 0.5f);//コンボ表示をシーンの位置からずらす量。yを増やすと奥（画面の上）へ動く
    [SerializeField] private float comboPositionStep = 0.8f;//コンボ表示の設定が「上」「下」のとき、「真ん中」からずらす量（yの向き）
    [SerializeField] private float songInfoTop = 75f;//右上の曲名の、画面の上端からの距離（スコアの下に来るように）
    [SerializeField] private float songInfoWidth = 640f;//右上の曲名の板の横幅
    private GameObject songInfo;//右上の曲名・難易度（リザルトが出たら隠す）

    [Header("FAST/LATE表示（選曲画面の設定がONのときだけ、コンボ表示を複製して作る）")]
    [SerializeField] private Vector2 fastLateOffset = new Vector2(0f, -0.4f);//コンボ表示からの位置
    [SerializeField] private float fastLateFontSize = 0.25f;
    string ComboStr;
    string chartId;//今遊んでいる譜面の目印。ハイスコアの記録に使う
    bool isFinished = false;//リザルトを1回だけ出すためのフラグ
    bool isTutorial;//チュートリアルとして始めたか。Tutorialは終わるとPlaySession.IsTutorialをfalseにするが、フェード中もこのシーンは動くので、始めたときの値で判断する
    int fastCount, lateCount;//PRECISE・APPROXのうち早かった数・遅かった数（最終リザルトに出す）
    int shownCombo = -1, shownScore = -1;//画面に出しているコンボ・スコア（-1はまだ出していない）
    void Start()
    {
        for (int i = 0; i < 7; i++)
        {
            PlayResult[i] = 0;
        }
        isTutorial = PlaySession.IsTutorial;
        if (isTutorial)
        {
            //ノーツは練習ごとにTutorialが流す。スコアの計算に使う総ノーツ数もTutorialが全練習の合計を入れる
            gameObject.AddComponent<Tutorial>();
        }
        else
        {
            StartChart();
        }

        //スコアを大きく、コンボを設定の位置へ（FAST/LATE表示はコンボ表示の位置を基準に作るので、その前に動かす）
        ScoreText.fontSize *= scoreFontScale;
        ScoreText.textWrappingMode = TextWrappingModes.NoWrap;
        ScoreText.overflowMode = TextOverflowModes.Overflow;
        RectTransform comboLabel = FindComboLabel();
        Vector2 offset = comboOffset + ComboPositionShift();
        ComboText.rectTransform.anchoredPosition += offset;
        if (comboLabel != null) comboLabel.anchoredPosition += offset;
        if (!isTutorial) BuildSongInfo();

        if (PlaySession.ShowFastLate) FastLateDisplay.Create(ComboText, fastLateOffset, fastLateFontSize);
        //「表示しない」でもFAST/LATEは出すので、複製し終えてから隠す
        if (PlaySession.ComboPosition == ComboPosition.Hidden)
        {
            ComboText.gameObject.SetActive(false);
            if (comboLabel != null) comboLabel.gameObject.SetActive(false);
        }
        if (PlaySession.TapSound) TapSound.Create();
        JudgeTextDisplay.Create(ComboText.font);
        ApplyBackgroundBrightness();
    }

    void StartChart()
    {
        //MusicSelectを経由せずに起動した場合はChartNameが空なのでデフォルト譜面を読む
        bool fromSelect = !string.IsNullOrEmpty(PlaySession.ChartName);
        string chartName = fromSelect ? PlaySession.ChartName : DefaultChartName;
        chartId = fromSelect && !string.IsNullOrEmpty(PlaySession.ChartId) ? PlaySession.ChartId : chartName;
        NoteManager.instance.LoadJson(chartName);
        totalCombo = NoteManager.instance.totalCombo;

        //曲を再生する前に、必ずLeadInBars小節ぶん待つ（小節線がONなら、その間は小節線だけが流れてくる）
        //待ち始めに1本目の小節線が壁から見え始めるよう、ノーツが流れてくる秒数も足す
        NoteManager notes = NoteManager.instance;
        MusicManager.instance.SetStartDelay(notes.TravelSeconds + notes.LeadInSeconds);
    }

    //コンボの数字の上にある「COMBO」の文字（シーンでは数字と同じ親の別オブジェクト）。数字と一緒に動かす・隠す
    RectTransform FindComboLabel()
    {
        Transform parent = ComboText.rectTransform.parent;
        Transform label = parent != null ? parent.Find(ComboLabelName) : null;
        if (label == null) Debug.LogWarning($"コンボの見出し「{ComboLabelName}」が見つからないので、数字だけ動かす");
        return label as RectTransform;
    }

    //選曲画面の「コンボ表示」の設定で、真ん中からずらす量
    Vector2 ComboPositionShift()
    {
        switch (PlaySession.ComboPosition)
        {
            case ComboPosition.Top: return new Vector2(0f, comboPositionStep);
            case ComboPosition.Bottom: return new Vector2(0f, -comboPositionStep);
            default: return Vector2.zero;
        }
    }

    //遊んでいる曲名と難易度を、画面の右上（スコアの下）に出す
    //シーンには置かず、ほかの画面と同じくUIUtilで作る（曲名は日本語なのでWindowsのフォントを使う）
    void BuildSongInfo()
    {
        bool fromSelect = !string.IsNullOrEmpty(PlaySession.ChartName);
        string title = fromSelect && !string.IsNullOrEmpty(PlaySession.SongTitle) ? PlaySession.SongTitle : NoteManager.instance.title;
        if (string.IsNullOrEmpty(title)) return;

        RectTransform canvas = UIUtil.CreateCanvas("SongInfoCanvas", -1, 0f);//リザルトなどシーンのUIより奥に描く
        songInfo = canvas.gameObject;
        Font font = UIUtil.CreateFont();
        Vector2 topRight = Vector2.one;

        //選曲画面を通らずに起動したときは難易度が分からないので、曲名だけ出す
        int difficulty = Mathf.Clamp(PlaySession.Difficulty, 0, DifficultyInfo.Names.Length - 1);
        Color accent = fromSelect ? DifficultyInfo.Colors[difficulty] : new Color(0.6f, 0.6f, 0.6f);
        const float titleHeight = 60f, badgeHeight = 44f, padding = 14f;
        float height = padding + titleHeight + (fromSelect ? badgeHeight + 8f : 0f) + padding;

        //背景の上でも読めるよう、暗い板の上に載せて周りと区切る（左端の色は難易度の色）
        var panel = UIUtil.CreateImage("Panel", canvas, new Color(0.03f, 0.04f, 0.09f, 0.85f)).rectTransform;
        UIUtil.Place(panel, topRight, topRight, topRight, new Vector2(-24f, -songInfoTop), new Vector2(songInfoWidth, height));
        var outline = panel.gameObject.AddComponent<Outline>();//板の縁取り
        outline.effectColor = new Color(accent.r, accent.g, accent.b, 0.8f);
        outline.effectDistance = new Vector2(2f, -2f);
        var strip = UIUtil.CreateImage("Strip", panel, accent).rectTransform;
        UIUtil.Place(strip, Vector2.zero, new Vector2(0f, 1f), new Vector2(0f, 0.5f), Vector2.zero, new Vector2(12f, 0f));

        //長い曲名は板に収まるまで文字を小さくする
        Text titleText = UIUtil.CreateText("Title", panel, font, Color.white, title, 46, TextAnchor.MiddleRight, FontStyle.Bold);
        titleText.horizontalOverflow = HorizontalWrapMode.Wrap;
        titleText.verticalOverflow = VerticalWrapMode.Truncate;
        titleText.resizeTextForBestFit = true;
        titleText.resizeTextMinSize = 24;
        titleText.resizeTextMaxSize = 46;
        UIUtil.Place(titleText.rectTransform, new Vector2(0f, 1f), Vector2.one, topRight, new Vector2(-20f, -padding), new Vector2(-52f, titleHeight));
        AddShadow(titleText);

        if (!fromSelect) return;
        //難易度とレベルは、難易度の色の札にして右下に出す
        var badge = UIUtil.CreateImage("DifficultyBadge", panel, accent).rectTransform;
        UIUtil.Place(badge, new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(-20f, padding), new Vector2(300f, badgeHeight));
        Text difficultyText = UIUtil.CreateText("Difficulty", badge, font, Color.white,
            $"{DifficultyInfo.Names[difficulty]}  Lv.{PlaySession.SongLevel}", 32, TextAnchor.MiddleCenter, FontStyle.Bold);
        UIUtil.Stretch(difficultyText.rectTransform);
        AddShadow(difficultyText);
    }

    //明るい背景の上でも読めるよう、文字に影を付ける
    static void AddShadow(Text text)
    {
        var shadow = text.gameObject.AddComponent<Shadow>();
        shadow.effectColor = new Color(0f, 0f, 0f, 0.7f);
        shadow.effectDistance = new Vector2(2f, -2f);
    }

    //選曲画面で設定した明るさ(%)を背景の色に掛けて暗くする（マテリアルはこのシーンだけの複製を変える）
    void ApplyBackgroundBrightness()
    {
        Renderer bg = background;
        if (bg == null && Camera.main != null) bg = Camera.main.GetComponentInChildren<Renderer>();
        if (bg == null || !bg.sharedMaterial.HasProperty("_BaseColor")) return;

        float brightness = PlaySession.BackgroundBrightnessPercent / 100f;
        Material material = bg.material;
        Color color = material.GetColor("_BaseColor");
        material.SetColor("_BaseColor", new Color(color.r * brightness, color.g * brightness, color.b * brightness, color.a));
    }

    void Update()
    {
        //文字は値が変わったときだけ書き換える（毎フレーム文字列を作り直さない）
        PlayResult[0] = CalcScore();
        if (PlayResult[1] != shownCombo)
        {
            shownCombo = PlayResult[1];
            ComboText.text = shownCombo.ToString();
        }
        if (PlayResult[0] != shownScore)
        {
            shownScore = PlayResult[0];
            ScoreText.text = shownScore.ToString();
        }

        if (songInfo != null && songInfo.activeSelf && ResultScreen.IsShowing) songInfo.SetActive(false);

        //チュートリアルはリザルトを出さない（終わりはTutorialが決める）
        if (isTutorial) return;
        //すべてのノーツの判定が終わったら（ノーツのリストが全部空になったら）、曲の途中でもリザルトを出す
        //ノーツが0個の譜面はリストが最初から空なので、曲の終わりで出す
        bool finished = totalCombo > 0 ? NoteManager.instance.AllNotesCleared : MusicManager.instance.IsSongFinished;
        if (!isFinished && finished)
        {
            isFinished = true;
            int previousBest = HighScoreStore.Get(chartId);
            bool hasPlayed = HighScoreStore.Has(chartId);
            TrackResult result = CreateTrackResult();
            HighScoreStore.Submit(result);
            PlaySession.RecordTrack(result);
            if (resultScreen != null) resultScreen.Show(PlayResult, hasPlayed ? previousBest : (int?)null, hideOnResult);
        }
    }

    //1曲分の結果。ハイスコアの記録と、最終リザルト・Webへ送る結果に使う
    TrackResult CreateTrackResult()
    {
        return new TrackResult
        {
            chartId = chartId,
            maxCombo = PlayResult[2],
            totalNotes = totalCombo,
            playerName = PlaySession.PlayerName,
            playedAt = System.DateTime.Now.ToString("yyyy-MM-ddTHH:mm:sszzz"),
            title = string.IsNullOrEmpty(PlaySession.SongTitle) ? NoteManager.instance.title : PlaySession.SongTitle,
            difficulty = PlaySession.Difficulty,
            level = PlaySession.SongLevel,
            jacketPath = PlaySession.JacketPath,
            score = PlayResult[0],
            precisePlus = PlayResult[3],
            precise = PlayResult[4],
            approx = PlayResult[5],
            miss = PlayResult[6],
            fast = fastCount,
            late = lateCount,
        };
    }

    //全ノーツPRECISE+で baseScore の101%(101万点)になる
    //小数の誤差で1点ずれないよう、longの整数で計算してから最後に割る（intだと掛け算であふれる）
    int CalcScore()
    {
        if (totalCombo <= 0) return 0;
        long rateSum = (long)PlayResult[3] * PrecisePlusRate + (long)PlayResult[4] * PreciseRate + (long)PlayResult[5] * ApproxRate;
        return (int)(baseScore * rateSum / (100L * totalCombo));
    }

    void AddPrecisePlus()
    {
        PlayResult[3]++;
        AddCombo();
    }
    void AddPrecise()
    {
        PlayResult[4]++;
        AddCombo();
    }
    void AddApprox()
    {
        PlayResult[5]++;
        AddCombo();
    }
    void AddMiss()
    {
        PlayResult[6]++;
        ResetCombo();
    }

    void AddCombo()
    {
        PlayResult[1]++;
        if (PlayResult[2] < PlayResult[1])
        {
            PlayResult[2] = PlayResult[1];
        }
    }

    //diffSecが負なら早い(FAST)
    void CountFastLate(float diffSec)
    {
        if (diffSec < 0f) fastCount++;
        else lateCount++;
    }

    void ResetCombo()
    {
        PlayResult[1] = 0;
    }

    void OnEnable()
    {
        JudgeManager.PrecisePlus += AddPrecisePlus;
        JudgeManager.Precise += AddPrecise;
        JudgeManager.Approx += AddApprox;
        JudgeManager.Miss += AddMiss;
        JudgeManager.FastLate += CountFastLate;
    }

    void OnDisable()
    {
        JudgeManager.PrecisePlus -= AddPrecisePlus;
        JudgeManager.Precise -= AddPrecise;
        JudgeManager.Approx -= AddApprox;
        JudgeManager.Miss -= AddMiss;
        JudgeManager.FastLate -= CountFastLate;
    }
}
