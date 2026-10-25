using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.Networking;

public class MusicManager : MonoBehaviour
{
    public static MusicManager instance;
    public float CurrentPlayTime; // 現在の再生時間
    public AudioClip shiningStar;//選曲画面を通らずに起動したときや、音源が読めなかったときに流す曲
    private AudioSource audioSource;

    private double dspStartTime;
    public float startDelay = 1.0f;//曲が始まるまでの待ち秒
    private bool isPlaying = false;
    private bool audioReady = true;//StreamingAssetsの音源を読み込み中はfalse

    //曲を再生してよいか（音源の読み込みが終わっていて、楽曲紹介も終わっている）
    private bool CanStart => audioReady && !PlaySession.WaitingForIntro;

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
        //選曲画面で選んだ曲の音源をStreamingAssetsから読み込む（楽曲紹介を出している間に読み終わる）
        if (!string.IsNullOrEmpty(PlaySession.AudioPath)) StartCoroutine(LoadAudio(PlaySession.AudioPath));
        //選曲画面の楽曲紹介が出ている間は、紹介が終わるまで再生を待つ
        if (CanStart) StartMusic();
    }

    IEnumerator LoadAudio(string path)
    {
        audioReady = false;
        using (var request = UnityWebRequestMultimedia.GetAudioClip(new Uri(path).AbsoluteUri, AudioTypeOf(path)))
        {
            yield return request.SendWebRequest();
            if (request.result == UnityWebRequest.Result.Success)
            {
                audioSource.clip = DownloadHandlerAudioClip.GetContent(request);
            }
            else
            {
                Debug.LogWarning($"音源を読み込めませんでした。代わりの曲を流します: {path}\n{request.error}");
            }
        }
        audioReady = true;
    }

    static AudioType AudioTypeOf(string path)
    {
        switch (Path.GetExtension(path).ToLowerInvariant())
        {
            case ".mp3": return AudioType.MPEG;
            case ".ogg": return AudioType.OGGVORBIS;
            case ".wav": return AudioType.WAV;
            default: return AudioType.UNKNOWN;
        }
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
            if (CanStart) StartMusic();
            return;
        }

        //現在のplayTime = 現在のdspTime-再生開始dspTime
        //再生前はマイナスの値になり、再生されると0から始まる
        CurrentPlayTime = (float)(AudioSettings.dspTime - dspStartTime) - TimingOffset;
        //CurrentPlayTime = audioSource.time;
        //Debug.Log(CurrentPlayTime);
    }
}
