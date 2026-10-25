using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class MusicManager : MonoBehaviour
{
    public static MusicManager instance;
    public float CurrentPlayTime; // 現在の再生時間
    public AudioClip shiningStar;
    private AudioSource audioSource;

    private double dspStartTime;
    public float startDelay = 1.0f;//曲が始まるまでの待ち秒
    private bool isPlaying = false;

    //選曲画面で設定したタイミング調整(秒)。+にするとノーツの到着と判定が遅れる（音が遅れて聞こえる環境向け）
    private float TimingOffset => PlaySession.TimingOffsetMs / 1000f;

    void Awake()
    {
        if (instance == null)
        {
            instance = this;
        }
    }

    void Start()
    {
        audioSource = gameObject.GetComponent<AudioSource>();
        audioSource.clip = shiningStar;
        //再生前はノーツを曲が始まるstartDelay秒前の位置に置いておく
        CurrentPlayTime = -startDelay - TimingOffset;
        //選曲画面の楽曲紹介が出ている間は、紹介が終わるまで再生を待つ
        if (!PlaySession.WaitingForIntro) StartMusic();
    }

    void StartMusic()
    {
        dspStartTime = AudioSettings.dspTime + startDelay;

        //指定したdspTimeに再生予約（dspは精度のいいタイマー）
        audioSource.PlayScheduled(dspStartTime);
        isPlaying = true;
        //CurrentPlayTime = 0;
    }

    void Update()
    {
        if (!isPlaying)
        {
            if (!PlaySession.WaitingForIntro) StartMusic();
            return;
        }

        //現在のplayTime = 現在のdspTime-再生開始dspTime
        //再生前はマイナスの値になり、再生されると0から始まる
        CurrentPlayTime = (float)(AudioSettings.dspTime - dspStartTime) - TimingOffset;
        //CurrentPlayTime = audioSource.time;
        //Debug.Log(CurrentPlayTime);
    }
}
