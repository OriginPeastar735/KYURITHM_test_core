using System;
using System.Collections;
using UnityEngine;
using UnityEngine.Networking;

//選曲画面で、選んでいる曲の頭からpreviewSeconds秒を繰り返し流す（最後はフェードアウトして、また最初から）
//MusicSelectManagerが毎フレームSetSongで流したい曲の音源を渡す（nullなら止める）
public class MusicPreview : MonoBehaviour
{
    public float previewSeconds = 10f;//1回に流す長さ
    public float fadeInSeconds = 0.3f;//流し始めのフェードイン
    public float fadeOutSeconds = 2f;//最後のフェードアウト
    public float switchFadeSeconds = 0.3f;//曲が変わったとき・止めるときに今の曲を消す時間
    public float loadDelay = 0.3f;//曲を選んでから読み込み始めるまでの時間（カードを素早く送っているときに毎回読み込まないように）
    public float volume = 1f;

    private AudioSource source;
    private string wantedPath;//流したい曲の音源
    private float wantedSince;//wantedPathが今の曲になった時刻
    private string loadedPath;//sourceに読み込んである曲の音源（読み込みに失敗した曲もここに入れて、読み直さないようにする）
    private bool loading;

    void Awake()
    {
        source = gameObject.AddComponent<AudioSource>();
        source.playOnAwake = false;
        source.loop = false;
    }

    public void SetSong(string audioPath)
    {
        if (string.IsNullOrEmpty(audioPath)) audioPath = null;
        if (audioPath == wantedPath) return;
        wantedPath = audioPath;
        wantedSince = Time.time;
    }

    void Update()
    {
        if (wantedPath != loadedPath)
        {
            //今の曲を少しで消してから、次の曲を読み込む
            if (source.isPlaying && source.volume > 0f)
            {
                source.volume = Mathf.MoveTowards(source.volume, 0f, volume / switchFadeSeconds * Time.deltaTime);
                return;
            }
            UnloadClip();
            if (wantedPath != null && !loading && Time.time - wantedSince >= loadDelay) StartCoroutine(Load(wantedPath));
            return;
        }
        if (source.clip == null) return;

        //previewSeconds秒（曲がそれより短ければ曲の長さ）流したら最初に戻る
        float length = Mathf.Min(previewSeconds, source.clip.length);
        if (!source.isPlaying || source.time >= length) Restart();
        float t = source.time;
        source.volume = volume * Mathf.Clamp01(Mathf.Min(t / fadeInSeconds, (length - t) / fadeOutSeconds));
    }

    void Restart()
    {
        source.volume = 0f;
        source.time = 0f;
        source.Play();
    }

    void UnloadClip()
    {
        source.Stop();
        if (source.clip != null) Destroy(source.clip);//読み込んだ音源はこのクラスだけが使うので、メモリから消す
        source.clip = null;
        loadedPath = null;
    }

    IEnumerator Load(string path)
    {
        loading = true;
        using (var request = UnityWebRequestMultimedia.GetAudioClip(new Uri(path).AbsoluteUri, MusicManager.AudioTypeOf(path)))
        {
            yield return request.SendWebRequest();
            //読み込んでいる間に別の曲が選ばれていたら捨てる
            bool stillWanted = path == wantedPath;
            if (request.result == UnityWebRequest.Result.Success)
            {
                AudioClip clip = DownloadHandlerAudioClip.GetContent(request);
                if (stillWanted)
                {
                    source.clip = clip;
                    loadedPath = path;
                    Restart();
                }
                else
                {
                    Destroy(clip);
                }
            }
            else
            {
                Debug.LogWarning($"試聴の音源を読み込めませんでした: {path}\n{request.error}");
                if (stillWanted) loadedPath = path;
            }
        }
        loading = false;
    }
}
