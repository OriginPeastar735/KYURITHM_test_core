using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;

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

    [Header("FAST/LATE表示（選曲画面の設定がONのときだけ、コンボ表示を複製して作る）")]
    [SerializeField] private Vector2 fastLateOffset = new Vector2(0f, -0.4f);//コンボ表示からの位置
    [SerializeField] private float fastLateFontSize = 0.25f;
    string ComboStr;
    string chartName;//今遊んでいる譜面。ハイスコアの記録に使う
    bool isFinished = false;//リザルトを1回だけ出すためのフラグ

    void Start()
    {
        for (int i = 0; i < 7; i++)
        {
            PlayResult[i] = 0;
        }
        //MusicSelectを経由せずに起動した場合はChartNameが空なのでデフォルト譜面を読む
        chartName = string.IsNullOrEmpty(PlaySession.ChartName) ? DefaultChartName : PlaySession.ChartName;
        NoteManager.instance.LoadJson(chartName);
        totalCombo = NoteManager.instance.totalCombo;

        if (PlaySession.ShowFastLate) FastLateDisplay.Create(ComboText, fastLateOffset, fastLateFontSize);
    }

    // Update is called once per frame
    void Update()
    {
        ComboText.text =  PlayResult[1].ToString();
        PlayResult[0] = CalcScore();
        ScoreText.text = PlayResult[0].ToString();

        //曲が最後まで流れ、すべてのノーツの判定が終わったらリザルトを出す（ノーツが0個の譜面は曲の終わりだけで出す）
        if (!isFinished && MusicManager.instance.IsSongFinished && (NoteManager.instance.AllNotesCleared || totalCombo == 0))
        {
            isFinished = true;
            int previousBest = HighScoreStore.Get(chartName);
            bool hasPlayed = HighScoreStore.Has(chartName);
            HighScoreStore.Submit(chartName, PlayResult[0]);
            if (resultScreen != null) resultScreen.Show(PlayResult, hasPlayed ? previousBest : (int?)null, hideOnResult);
        }
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
        Debug.Log($"Score:{PlayResult[0]}");
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
    }

    void OnDisable()
    {
        JudgeManager.PrecisePlus -= AddPrecisePlus;
        JudgeManager.Precise -= AddPrecise;
        JudgeManager.Approx -= AddApprox;
        JudgeManager.Miss -= AddMiss;
    }
}
