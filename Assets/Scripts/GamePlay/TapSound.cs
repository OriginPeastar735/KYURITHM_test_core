using UnityEngine;

//タップ音。センサーを押したときに、ノーツを叩けたら高い音、空打ちなら低く小さい音を鳴らす
//シーンには置かず、選曲画面の設定「タップ音」がONのときだけGameManagerが作る
//
//音はコードで作る（短い音なので素材が要らず、読み込みの待ちも無い）
//押してから鳴るまでの遅れは、Unityの音の準備（Project Settings > Audio の DSP Buffer Size。Best latency にしてある）と
//Windows・テレビ・スピーカーの遅れで決まり、ここでは縮められない
public class TapSound : MonoBehaviour
{
    public float hitVolume = 0.6f;
    public float emptyVolume = 0.3f;

    private AudioSource source;
    private AudioClip hitClip, emptyClip;

    public static TapSound Create()
    {
        return new GameObject("TapSound").AddComponent<TapSound>();
    }

    void Awake()
    {
        source = gameObject.AddComponent<AudioSource>();
        source.playOnAwake = false;
        source.spatialBlend = 0f;
        hitClip = CreateClick("TapHit", 1800f, 0.008f);
        emptyClip = CreateClick("TapEmpty", 500f, 0.012f);
    }

    void OnEnable()
    {
        JudgeManager.TapHit += PlayHit;
        JudgeManager.EmptyTap += PlayEmpty;
    }

    void OnDisable()
    {
        JudgeManager.TapHit -= PlayHit;
        JudgeManager.EmptyTap -= PlayEmpty;
    }

    //コードで作った音はオブジェクトを消しても残るので、一緒に消す
    void OnDestroy()
    {
        if (hitClip != null) Destroy(hitClip);
        if (emptyClip != null) Destroy(emptyClip);
    }

    void PlayHit()
    {
        source.PlayOneShot(hitClip, hitVolume);
    }

    void PlayEmpty()
    {
        source.PlayOneShot(emptyClip, emptyVolume);
    }

    //最初のサンプルから鳴ってすぐ消える短い音（frequency: 高さHz、decay: 小さくなるまでの秒）
    static AudioClip CreateClick(string name, float frequency, float decay)
    {
        int rate = AudioSettings.outputSampleRate;
        int samples = Mathf.RoundToInt(rate * decay * 6f);//聞こえなくなるまでの長さ
        var data = new float[samples];
        for (int i = 0; i < samples; i++)
        {
            data[i] = Mathf.Sin(2f * Mathf.PI * frequency * i / rate) * Mathf.Exp(-i / (rate * decay));
        }
        AudioClip clip = AudioClip.Create(name, samples, 1, rate, false);
        clip.SetData(data, 0);
        return clip;
    }
}
