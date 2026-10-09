using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;

public class GameManager : MonoBehaviour
{
    

    public enum GameScene
    {
        Opening,
        MusicSelect,
        Play,
        Result
    };

    public int[] PlayResult = new int[7];//(score, combo, maxCombo, parfect, great, good, miss)
    public float playScore = 0;

    public int theoScore = 1010000;
    public int totalCombo = 0;
    public float greatCoef = 0.8f;//great coefficient
    public float goodCoef = 0.5f;

    [SerializeField] private TextMeshProUGUI ComboText;
    [SerializeField] private TextMeshProUGUI ScoreText;
    string ComboStr;
    //int CurrentScene = 1; // 0:opening, 1:musicselect, 2:play, 3:playresult
    public GameScene CurrentScene { get; private set; }//外部から参照はできるが，書き込みはできない
    int musicIndex = 0;
    int difficultyIndex = 0;

    void Start()
    {
        for (int i = 0; i < 7; i++)
        {
            PlayResult[i] = 0;
        }
        NoteManager.instance.LoadJson("test2");
        CurrentScene = GameScene.MusicSelect;
        totalCombo = NoteManager.instance.totalCombo;
    }

    // Update is called once per frame
    void Update()
    {
        ComboText.text =  PlayResult[1].ToString();
        if (Input.GetKeyDown("d") && CurrentScene == GameScene.MusicSelect)//難易度低下
        {
            difficultyIndex--;
        }
        if (Input.GetKeyDown("f") && CurrentScene == GameScene.MusicSelect)//難易度上昇
        {
            difficultyIndex++;
        }
        if (Input.GetKeyDown("j") && CurrentScene == GameScene.MusicSelect)//1つ下の曲へ
        {
            musicIndex--;
        }
        if (Input.GetKeyDown("k") && CurrentScene == GameScene.MusicSelect)//1つ上の曲へ
        {
            musicIndex++;
        }
        if (Input.GetKeyDown("a") && CurrentScene == GameScene.MusicSelect)//難易度低下
        {
            ChangeScene(GameScene.Play);
        }
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

    public void ChangeScene(GameScene next)
    {
        CurrentScene = next;

        switch (next)
        {
            case GameScene.Opening:
                SceneManager.LoadScene("OpeningScene");
                break;

            case GameScene.MusicSelect:
                SceneManager.LoadScene("MusicSelectScene");
                break;

            case GameScene.Play:
                SceneManager.LoadScene("PlayScene");
                break;

            case GameScene.Result:
                SceneManager.LoadScene("ResultScene");
                break;
        }
    }
}
