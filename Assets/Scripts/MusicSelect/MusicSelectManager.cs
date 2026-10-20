using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class MusicSelectManager : MonoBehaviour
{
    int musicIndex = 0;
    int difficultyIndex = 0;

    // Start is called before the first frame update
    void Start()
    {

    }

    // Update is called once per frame
    void Update()
    {
        if (Input.GetKeyDown("d"))//難易度低下
        {
            difficultyIndex--;
        }
        if (Input.GetKeyDown("f"))//難易度上昇
        {
            difficultyIndex++;
        }
        if (Input.GetKeyDown("j"))//1つ下の曲へ
        {
            musicIndex--;
        }
        if (Input.GetKeyDown("k"))//1つ上の曲へ
        {
            musicIndex++;
        }
        if (Input.GetKeyDown("a"))//決定
        {
            DecideMusic();
        }
    }

    void DecideMusic()
    {
        PlaySession.MusicIndex = musicIndex;
        PlaySession.Difficulty = difficultyIndex;
        //TODO: 曲リストができたらmusicIndexとdifficultyIndexから譜面名を決めてPlaySession.ChartNameに入れる
        SceneLoader.ChangeScene(GameScene.Play);
    }
}
