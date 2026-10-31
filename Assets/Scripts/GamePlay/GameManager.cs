using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;

//プレイ画面の進行。譜面の読み込みを始め、判定を受けてスコア・コンボを集計し、曲が終わったらリザルトを出す
public class GameManager : MonoBehaviour
{
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

    [Tooltip("運営用。プレイ中に押すと、演出もリザルトも出さずにすぐ選曲画面へ移る（最終リザルトにはSkippedと出す）")]
    [SerializeField] private KeyCode skipKey = KeyCode.F12;

    [Header("FAST/LATE表示（選曲画面の設定がONのときだけ、コンボ表示を複製して作る）")]
    [SerializeField] private Vector2 fastLateOffset = new Vector2(0f, -0.4f);//コンボ表示からの位置
    [SerializeField] private float fastLateFontSize = 0.25f;
    string ComboStr;
    string chartName;//今遊んでいる譜面。ハイスコアの記録に使う
    bool isFinished = false;//リザルトを1回だけ出すためのフラグ
    int fastCount, lateCount;//PRECISE・APPROXのうち早かった数・遅かった数（最終リザルトに出す）
    void Start()
    {
        for (int i = 0; i < 7; i++)
        {
            PlayResult[i] = 0;
        }
        if (PlaySession.IsTutorial)
        {
            //ノーツは練習ごとにTutorialが流す。スコアの計算に使う総ノーツ数もTutorialが全練習の合計を入れる
            gameObject.AddComponent<Tutorial>();
        }
        else
        {
            StartChart();
        }

        if (PlaySession.ShowFastLate) FastLateDisplay.Create(ComboText, fastLateOffset, fastLateFontSize);
        JudgeTextDisplay.Create(ComboText.font);
        ApplyBackgroundBrightness();
    }

    void StartChart()
    {
        //MusicSelectを経由せずに起動した場合はChartNameが空なのでデフォルト譜面を読む
        chartName = string.IsNullOrEmpty(PlaySession.ChartName) ? DefaultChartName : PlaySession.ChartName;
        NoteManager.instance.LoadJson(chartName);
        totalCombo = NoteManager.instance.totalCombo;

        //曲を再生する前に、必ずLeadInBars小節ぶん待つ（小節線がONなら、その間は小節線だけが流れてくる）
        //待ち始めに1本目の小節線が壁から見え始めるよう、ノーツが流れてくる秒数も足す
        NoteManager notes = NoteManager.instance;
        MusicManager.instance.SetStartDelay(notes.TravelSeconds + notes.LeadInSeconds);
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
        ComboText.text =  PlayResult[1].ToString();
        PlayResult[0] = CalcScore();
        ScoreText.text = PlayResult[0].ToString();

        //すべてのノーツの判定が終わったら（ノーツのリストが全部空になったら）、曲の途中でもリザルトを出す
        //ノーツが0個の譜面はリストが最初から空なので、曲の終わりで出す
        if (!isFinished && Input.GetKeyDown(skipKey))
        {
            SkipPlay();
            return;
        }

        //チュートリアルはリザルトを出さない（終わりはTutorialが決める）
        if (PlaySession.IsTutorial) return;
        bool finished = totalCombo > 0 ? NoteManager.instance.AllNotesCleared : MusicManager.instance.IsSongFinished;
        if (!isFinished && finished)
        {
            isFinished = true;
            int previousBest = HighScoreStore.Get(chartName);
            bool hasPlayed = HighScoreStore.Has(chartName);
            HighScoreStore.Submit(chartName, PlayResult[0]);
            RecordTrack();
            if (resultScreen != null) resultScreen.Show(PlayResult, hasPlayed ? previousBest : (int?)null, hideOnResult);
        }
    }

    //運営のプレイスキップ。センサーの不具合などで続けられないときに使う
    //ハイスコアは記録せず、最終リザルトには"Skipped"と出す。規定の曲数を遊び終えたことになる場合は最終リザルトへ移る
    void SkipPlay()
    {
        isFinished = true;
        //楽曲紹介の途中なら、紹介画面（シーンが変わっても残る）も消す
        IntroScreen intro = FindAnyObjectByType<IntroScreen>();
        if (intro != null) Destroy(intro.gameObject);

        if (PlaySession.IsTutorial)
        {
            Notice.Show("チュートリアルをスキップしました");
            GetComponent<Tutorial>().Finish(true);
            return;
        }

        PlaySession.RecordTrack(new TrackResult
        {
            title = string.IsNullOrEmpty(PlaySession.SongTitle) ? NoteManager.instance.title : PlaySession.SongTitle,
            difficulty = PlaySession.Difficulty,
            level = PlaySession.SongLevel,
            jacketPath = PlaySession.JacketPath,
            skipped = true,
        });
        Notice.Show($"プレイをスキップしました（{PlaySession.TrackNumber}曲目）");
        SceneLoader.ChangeScene(PlaySession.NextTrack() ? GameScene.MusicSelect : GameScene.Result);
    }

    //最終リザルトに出す1曲分の結果を残す
    void RecordTrack()
    {
        PlaySession.RecordTrack(new TrackResult
        {
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
        });
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
