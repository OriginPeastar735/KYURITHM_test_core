using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;

public class GameManager : MonoBehaviour
{
    const string DefaultChartName = "test1013";//Playシーン単体で起動したときに使う譜面

    public int[] PlayResult = new int[7];//(score, combo, maxCombo, parfect, great, good, miss)
    public float playScore = 0;

    public int theoScore = 1010000;
    public int totalCombo = 0;
    public float greatCoef = 0.8f;//great coefficient
    public float goodCoef = 0.5f;

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
        PlayResult[0] = (int)(((theoScore * PlayResult[3]) / totalCombo) + ((theoScore * PlayResult[4] * greatCoef) / totalCombo) + ((theoScore * PlayResult[5] * goodCoef) / totalCombo));
        ScoreText.text = PlayResult[0].ToString();
    }

    void AddPerfect()
    {
        PlayResult[3]++;
        AddCombo();
    }
    void AddGreat()
    {
        PlayResult[4]++;
        AddCombo();
    }
    void AddGood()
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
        JudgeManager.Perfect += AddPerfect;
        JudgeManager.Great += AddGreat;
        JudgeManager.Good += AddGood;
        JudgeManager.Miss += AddMiss;
    }

    void OnDisable()
    {
        JudgeManager.Perfect -= AddPerfect;
        JudgeManager.Great -= AddGreat;
        JudgeManager.Good -= AddGood;
        JudgeManager.Miss -= AddMiss;
    }
}
