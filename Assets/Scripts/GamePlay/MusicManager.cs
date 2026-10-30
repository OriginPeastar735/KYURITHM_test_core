using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.Networking;
using Stopwatch = System.Diagnostics.Stopwatch;

//曲の再生と、曲の再生時間(CurrentPlayTime)の管理
//再生時間はdspTime（音の時計）が基準。ただしdspTimeはオーディオのバッファ単位（数ms）でしか進まないので、
//Stopwatchの経過時間を少しずつdspTimeに寄せて、なめらかで音とずれない時計にしている
[DefaultExecutionOrder(-110)]//InputManagerより先に、このフレームの再生時間を決めておく（入力の時刻を曲の時刻に直す基準になる）
public class MusicManager : MonoBehaviour
{
    public static MusicManager instance;
    public float CurrentPlayTime;//曲の再生時間（秒）。再生前はマイナス
    public AudioClip shiningStar;//選曲画面を通らずに起動したときや、音源が読めなかったときに流す曲
    private AudioSource audioSource;

    private double dspStartTime;
    private long startTimestamp;//dspStartTimeに当たるStopwatchの値
    private long frameTimestamp;//このフレームでCurrentPlayTimeを決めたときのStopwatchの値
    private double clockCorrection;//Stopwatchの経過時間をdspTimeに合わせるための補正(秒)
    private const double ClockFollowRate = 0.05;//1フレームでdspTimeとの差の何割を詰めるか（小さいほどなめらか）
    private const double ClockSnapSeconds = 0.05;//これ以上ずれたら一気に合わせる（処理落ちやウィンドウ操作で止まった場合など）
    public float startDelay = 1.0f;//曲が始まるまでの待ち秒
    private bool isPlaying = false;
    private bool audioReady = true;//StreamingAssetsの音源を読み込み中はfalse

    //曲を再生してよいか（音源の読み込みが終わっていて、楽曲紹介も終わっている）
    private bool CanStart => audioReady && !PlaySession.WaitingForIntro;

    //選曲画面で設定したタイミング調整(秒)。+にするとノーツの到着と判定が遅れる（音が遅れて聞こえる環境向け）
    private float TimingOffset => PlaySession.TimingOffsetMs / 1000f;

    //曲を最後まで再生し終えたか（リザルトを出すタイミングに使う）
    //再生予約したdspTimeから曲の長さが過ぎたらtrue。音源が無い場合は曲の長さが分からないのでtrue（ノーツの終わりで判断する）
    public bool IsSongFinished
    {
        get
        {
            if (!isPlaying) return false;
            if (audioSource.clip == null) return true;
            return AudioSettings.dspTime >= dspStartTime + audioSource.clip.length;
        }
    }

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
        audioSource.clip = PlaySession.IsTutorial ? null : shiningStar;//チュートリアルでは曲を流さず、時計だけ進める
        //再生前はノーツを曲が始まるstartDelay秒前の位置に置いておく
        CurrentPlayTime = -startDelay - TimingOffset;
        //選曲画面で選んだ曲の音源をStreamingAssetsから読み込む（楽曲紹介を出している間に読み終わる）
        if (!string.IsNullOrEmpty(PlaySession.AudioPath)) StartCoroutine(LoadAudio(PlaySession.AudioPath));
        //再生はUpdateで始める（GameManagerが譜面を読んでSetStartDelayで待ち時間を決めてから始めるため）
        //選曲画面の楽曲紹介が出ている間は、紹介が終わるまで再生を待つ
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

    public static AudioType AudioTypeOf(string path)
    {
        switch (Path.GetExtension(path).ToLowerInvariant())
        {
            case ".mp3": return AudioType.MPEG;
            case ".ogg": return AudioType.OGGVORBIS;
            case ".wav": return AudioType.WAV;
            default: return AudioType.UNKNOWN;
        }
    }

    //Stopwatch.GetTimestamp()で取った時刻が、曲の再生時間で何秒か（入力を押した時刻の判定に使う）
    public float ToPlayTime(long timestamp)
    {
        return CurrentPlayTime + (float)((timestamp - frameTimestamp) / (double)Stopwatch.Frequency);
    }

    //曲が始まるまでの待ち時間を、今より長ければ延ばす（再生を始める前だけ）
    public void SetStartDelay(float seconds)
    {
        if (isPlaying || seconds <= startDelay) return;
        startDelay = seconds;
        CurrentPlayTime = -startDelay - TimingOffset;
    }

    void StartMusic()
    {
        dspStartTime = AudioSettings.dspTime + startDelay;
        startTimestamp = Stopwatch.GetTimestamp() + (long)(startDelay * Stopwatch.Frequency);
        clockCorrection = 0;

        //指定したdspTimeに再生予約（dspは精度のいいタイマー）
        if (audioSource.clip != null) audioSource.PlayScheduled(dspStartTime);
        isPlaying = true;
    }

    void Update()
    {
        frameTimestamp = Stopwatch.GetTimestamp();
        if (!isPlaying)
        {
            if (CanStart) StartMusic();
            return;
        }

        //現在のplayTime = 再生開始からの経過時間（Stopwatchで測り、dspTimeとの差を少しずつ補正する）
        //再生前はマイナスの値になり、再生されると0から始まる
        double elapsed = (frameTimestamp - startTimestamp) / (double)Stopwatch.Frequency;
        double error = (AudioSettings.dspTime - dspStartTime) - (elapsed + clockCorrection);
        clockCorrection += Math.Abs(error) > ClockSnapSeconds ? error : error * ClockFollowRate;
        CurrentPlayTime = (float)(elapsed + clockCorrection) - TimingOffset;
    }
}
