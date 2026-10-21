using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;

public class GameManager : MonoBehaviour
{
    const string DefaultChartName = "test1013";//Playシーン単体で起動したときに使う譜面

    public int[] PlayResult = new int[7];//(score, combo, maxCombo, precisePlus, precise, approx, miss)
    public float playScore = 0;

    //1ノーツあたり baseScore / totalCombo 点を基準に、判定ごとの割合(%)を掛けて加算する
    public int baseScore = 10000000;
    public int totalCombo = 0;
    const int PrecisePlusRate = 101;//PRECISE+ 101%
    const int PreciseRate = 100;//PRECISE 100%
    const int ApproxRate = 50;//APPROX 50%（MISSは0%）

    [SerializeField] private TextMeshProUGUI ComboText;
    [SerializeField] private TextMeshProUGUI ScoreText;
    string ComboStr;

    void Start()
    {
        for (int i = 0; i < 7; i++)
        {
            PlayResult[i] = 0;
        }
        //MusicSelectを経由せずに起動した場合はChartNameが空なのでデフォルト譜面を読む
        string chartName = string.IsNullOrEmpty(PlaySession.ChartName) ? DefaultChartName : PlaySession.ChartName;
        NoteManager.instance.LoadJson(chartName);
        totalCombo = NoteManager.instance.totalCombo;
    }

    // Update is called once per frame
    void Update()
    {
        ComboText.text =  PlayResult[1].ToString();
        PlayResult[0] = CalcScore();
        ScoreText.text = PlayResult[0].ToString();
    }

    //全ノーツPRECISE+で baseScore の101%になる
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
