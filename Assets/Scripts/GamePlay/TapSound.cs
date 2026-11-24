using UnityEngine;

//タップ音（ノーツ音）。判定に合わせて鳴らす
//  叩いた音  : tapを叩けた（sample/Tap.wav 風）。空打ちなら低い音
//  どの音も同じ大きさに聞こえるようにそろえてある（MatchLoudness）
//  HOLD      : holdを取れた（sample/ExTap.wav 風）
//  スライド  : 始点を叩けた・中間と終点を取れた（sample/SlideStep.wav 風）。なぞっている間は継続音（sample/SlideLoop.wav 風）
//シーンには置かず、選曲画面の設定「タップ音」が0%でないときだけGameManagerが作る
//
//音は wav を読まずにコードで作る。sample の wav を測った特徴（高さ・減り方・ノイズの量）に寄せてある
//音のデータは始めに1回だけ作ってメモリに置くので、鳴らすときの重さは wav と変わらず、読み込みの待ちも無い
//押してから鳴るまでの遅れは、Unityの音の準備（Project Settings > Audio の DSP Buffer Size。Best latency にしてある）と
//Windows・テレビ・スピーカーの遅れで決まり、ここでは縮められない
//譜面エディタ（KYURITHM-ChartEditor の templates/index.html）も同じ式で同じ音を作っている。音を変えたらエディタも直す
public class TapSound : MonoBehaviour
{
    public float volume = 0.6f;//どの音もこの大きさで鳴らす（音のデータの方で、聞こえる大きさがそろうように作ってある）
    public float slideLoopFadeSeconds = 0.03f;//継続音を鳴らし始める・止めるときに大きさを変える秒数（ぶつっと鳴らないように）

    private AudioSource source, loopSource;
    private AudioClip hitClip, emptyClip, holdClip, slideStepClip, slideLoopClip;

    public static TapSound Create()
    {
        return new GameObject("TapSound").AddComponent<TapSound>();
    }

    void Awake()
    {
        source = CreateSource();
        loopSource = CreateSource();
        loopSource.loop = true;
        loopSource.volume = 0f;

        int rate = AudioSettings.outputSampleRate;
        hitClip = ToClip("TapHit", Synthesize(HitVoice, rate), rate);
        emptyClip = ToClip("TapEmpty", Synthesize(EmptyVoice, rate), rate);
        holdClip = ToClip("TapHold", Synthesize(HoldVoice, rate), rate);
        slideStepClip = ToClip("TapSlideStep", Synthesize(SlideStepVoice, rate), rate);
        slideLoopClip = ToClip("TapSlideLoop", SynthesizeSlideLoop(rate), rate);
        loopSource.clip = slideLoopClip;
    }

    AudioSource CreateSource()
    {
        AudioSource s = gameObject.AddComponent<AudioSource>();
        s.playOnAwake = false;
        s.spatialBlend = 0f;
        return s;
    }

    void OnEnable()
    {
        JudgeManager.TapHit += PlayHit;
        JudgeManager.EmptyTap += PlayEmpty;
        JudgeManager.HoldHit += PlayHold;
        JudgeManager.SlideStep += PlaySlideStep;
    }

    void OnDisable()
    {
        JudgeManager.TapHit -= PlayHit;
        JudgeManager.EmptyTap -= PlayEmpty;
        JudgeManager.HoldHit -= PlayHold;
        JudgeManager.SlideStep -= PlaySlideStep;
    }

    //コードで作った音はオブジェクトを消しても残るので、一緒に消す
    void OnDestroy()
    {
        foreach (AudioClip clip in new[] { hitClip, emptyClip, holdClip, slideStepClip, slideLoopClip })
        {
            if (clip != null) Destroy(clip);
        }
    }

    //スライドをなぞっている間だけ継続音を鳴らす。始め・終わりは少しずつ大きさを変える
    void Update()
    {
        bool tracing = JudgeManager.instance != null && JudgeManager.instance.IsTracingSlide;
        float target = tracing ? volume * VolumeScale : 0f;
        float step = volume / Mathf.Max(0.001f, slideLoopFadeSeconds) * Time.unscaledDeltaTime;
        loopSource.volume = Mathf.MoveTowards(loopSource.volume, target, step);

        if (target > 0f && !loopSource.isPlaying) loopSource.Play();
        else if (target <= 0f && loopSource.volume <= 0f && loopSource.isPlaying) loopSource.Pause();
    }

    //選曲画面で決めた音量(%)。100%のときにvolumeの大きさで鳴る
    static float VolumeScale => PlaySession.TapSoundVolumePercent / 100f;

    void PlayHit() { source.PlayOneShot(hitClip, volume * VolumeScale); }
    void PlayEmpty() { source.PlayOneShot(emptyClip, volume * VolumeScale); }
    void PlayHold() { source.PlayOneShot(holdClip, volume * VolumeScale); }
    void PlaySlideStep() { source.PlayOneShot(slideStepClip, volume * VolumeScale); }

    static AudioClip ToClip(string name, float[] data, int rate)
    {
        AudioClip clip = AudioClip.Create(name, data.Length, 1, rate, false);
        clip.SetData(data, 0);
        return clip;
    }

    #region 音づくり（Unityに頼らない計算だけ。確認用に外でも同じ式で音を作れるようにしてある）
    //響きの成分1つ分
    class Partial
    {
        public float frequency;//高さ(Hz)
        public float amplitude;//大きさ
        public float decay;//小さくなる速さ（大きさが約37%になるまでの秒。0なら減らない）
        public float start;//鳴り始める秒
        public float sweepFrom;//0でなければ、この高さから frequency へ下がっていく（「ドン」の音）
        public float sweepTime;//高さが下がる速さ（秒）

        public Partial(float frequency, float amplitude, float decay, float start = 0f, float sweepFrom = 0f, float sweepTime = 0f)
        {
            this.frequency = frequency; this.amplitude = amplitude; this.decay = decay;
            this.start = start; this.sweepFrom = sweepFrom; this.sweepTime = sweepTime;
        }
    }

    //1つの音の作り方（響きの成分＋ノイズ）
    class Voice
    {
        public float length;//長さ(秒)
        public float fade = 0.006f;//最後にこの秒数で小さくして切る（ぶつっと鳴らないように）
        public Partial[] partials;
        public float noiseLevel, noiseDecay;//ノイズの大きさ・減る速さ（秒）
        public float noiseLow, noiseHigh;//ノイズの高さの範囲(Hz)
    }

    //叩いた音（Tap.wav）：1.1〜1.9kHz の金属っぽい響き＋「シャッ」というノイズの余韻＋小さな低音。約0.18秒
    static readonly Voice HitVoice = new Voice
    {
        length = 0.18f,
        partials = new[]
        {
            new Partial(1100f, 1.00f, 0.013f),
            new Partial(1290f, 0.70f, 0.011f),
            new Partial(1380f, 0.50f, 0.016f),
            new Partial(1770f, 0.35f, 0.014f),
            new Partial(1860f, 0.50f, 0.010f),
            new Partial(2665f, 0.22f, 0.012f),
            new Partial(2945f, 0.15f, 0.016f),
            new Partial(3350f, 0.22f, 0.008f),
            new Partial(4170f, 0.11f, 0.008f),
            new Partial(95f, 0.08f, 0.03f),
            new Partial(164f, 0.016f, 0f),
        },
        noiseLevel = 1.0f, noiseDecay = 0.05f, noiseLow = 800f, noiseHigh = 9000f,
    };

    //空打ちの音：叩いた音の響きを半分くらいの高さにして、短く、ノイズ無しにしたもの（叩けたかどうかを音で聞き分けられるように）
    static readonly Voice EmptyVoice = new Voice
    {
        length = 0.07f,
        partials = new[]
        {
            new Partial(495f, 1.00f, 0.013f),
            new Partial(580f, 0.70f, 0.011f),
            new Partial(620f, 0.50f, 0.016f),
            new Partial(800f, 0.35f, 0.014f),
            new Partial(840f, 0.50f, 0.010f),
            new Partial(1200f, 0.22f, 0.012f),
            new Partial(1325f, 0.15f, 0.016f),
            new Partial(1510f, 0.22f, 0.008f),
            new Partial(1875f, 0.11f, 0.008f),
            new Partial(43f, 0.05f, 0.03f),
        },
    };

    //HOLD（ExTap.wav）：95Hz から 38Hz へ下がる重い「ドン」＋叩いた音に似た頭＋1kHz あたりのノイズの長い余韻
    //元は3秒あるが、後ろはほとんど聞こえないので0.8秒で切る（切れ目が分からないよう、最後の0.15秒で小さくする）
    static readonly Voice HoldVoice = new Voice
    {
        length = 0.8f, fade = 0.15f,
        partials = new[]
        {
            new Partial(38f, 0.35f, 0.13f, 0f, 95f, 0.06f),
            new Partial(110f, 0.10f, 0.08f),
            new Partial(1078f, 0.35f, 0.012f),
            new Partial(1315f, 0.22f, 0.010f),
            new Partial(1863f, 0.08f, 0.010f),
            new Partial(2665f, 0.10f, 0.010f),
            new Partial(3317f, 0.22f, 0.006f),
            new Partial(4129f, 0.10f, 0.006f),
        },
        noiseLevel = 0.8f, noiseDecay = 0.6f, noiseLow = 700f, noiseHigh = 1800f,
    };

    //スライドの始点・中間・終点（SlideStep.wav）：2.4〜4.8kHz の「チッ」→ 1.1〜1.4kHz の響き → 4kHz の「キン」。約0.12秒
    static readonly Voice SlideStepVoice = new Voice
    {
        length = 0.12f,
        partials = new[]
        {
            new Partial(2420f, 0.60f, 0.008f),
            new Partial(3115f, 0.80f, 0.008f),
            new Partial(2560f, 0.45f, 0.008f),
            new Partial(3500f, 0.30f, 0.008f),
            new Partial(4820f, 0.45f, 0.008f),
            new Partial(1100f, 0.40f, 0.020f, 0.012f),
            new Partial(1190f, 0.45f, 0.020f, 0.012f),
            new Partial(1260f, 0.45f, 0.020f, 0.012f),
            new Partial(1330f, 0.40f, 0.022f, 0.012f),
            new Partial(1420f, 0.35f, 0.022f, 0.012f),
            new Partial(4050f, 0.45f, 0.030f, 0.040f),
            new Partial(122f, 0.40f, 0.050f),
        },
        noiseLevel = 2.0f, noiseDecay = 0.06f, noiseLow = 1000f, noiseHigh = 8000f,
    };

    public static float[] SynthesizeHit(int rate) => Synthesize(HitVoice, rate);
    public static float[] SynthesizeEmpty(int rate) => Synthesize(EmptyVoice, rate);
    public static float[] SynthesizeHold(int rate) => Synthesize(HoldVoice, rate);
    public static float[] SynthesizeSlideStep(int rate) => Synthesize(SlideStepVoice, rate);

    //聞こえる大きさの目安（鳴り始めから0.1秒の平均(RMS)）。どの音もこの値にそろえる
    //叩いた音のいちばん大きいところがちょうど1になる値。ほかの音はこれにそろえても1を超えない
    const double Loudness = 0.145;
    const double LoudnessSeconds = 0.1;

    //聞こえる大きさ（Loudness）にそろえて返す
    static float[] Synthesize(Voice voice, int rate)
    {
        int samples = (int)(rate * voice.length);
        int fadeSamples = System.Math.Max(1, (int)(rate * voice.fade));
        var data = new float[samples];

        foreach (Partial p in voice.partials)
        {
            int first = (int)(p.start * rate);
            for (int i = first; i < samples; i++)
            {
                double t = (double)(i - first) / rate;
                double phase = p.frequency * t;
                if (p.sweepFrom > 0f) phase += (p.sweepFrom - p.frequency) * p.sweepTime * (1.0 - System.Math.Exp(-t / p.sweepTime));
                double envelope = p.decay > 0f ? System.Math.Exp(-t / p.decay) : 1.0;
                if (t < 0.001) envelope *= t / 0.001;//途中から鳴り始める成分は1msで大きくする
                data[i] += (float)(p.amplitude * envelope * System.Math.Sin(2.0 * System.Math.PI * phase));
            }
        }

        if (voice.noiseLevel > 0f)
        {
            var noise = new float[samples];
            var random = new SeededRandom(1234);//毎回同じ音になるよう、ノイズの種を決めておく
            for (int i = 0; i < samples; i++) noise[i] = (float)(random.Next() * 2.0 - 1.0);
            BandPass(noise, rate, voice.noiseLow, voice.noiseHigh, 1);
            for (int i = 0; i < samples; i++)
            {
                data[i] += voice.noiseLevel * noise[i] * (float)System.Math.Exp(-(double)i / rate / voice.noiseDecay);
            }
        }

        for (int i = samples - fadeSamples; i < samples; i++) data[i] *= (float)(samples - i) / fadeSamples;
        MatchLoudness(data, (int)(rate * LoudnessSeconds));
        return data;
    }

    //先頭からcount個の平均(RMS)がLoudnessになるように、全体の大きさを変える
    static void MatchLoudness(float[] data, int count)
    {
        count = System.Math.Min(count, data.Length);
        double sum = 0;
        for (int i = 0; i < count; i++) sum += data[i] * data[i];
        float scale = (float)(Loudness / System.Math.Sqrt(sum / count + 1e-12));
        for (int i = 0; i < data.Length; i++) data[i] *= scale;
    }

    //スライドをなぞっている間の継続音（SlideLoop.wav）：1.0〜1.7kHz に集まった「サー」というノイズ
    //1秒分を作り、つなぎ目が分からないようにくり返して鳴らす。大きさは全体の平均がLoudnessになるようにそろえる
    public static float[] SynthesizeSlideLoop(int rate)
    {
        int samples = rate;
        var data = new float[samples];
        var random = new SeededRandom(5678);
        for (int i = 0; i < samples; i++) data[i] = (float)(random.Next() * 2.0 - 1.0);
        //2周回して2周目を使うと、フィルターの始まりと終わりがつながり、くり返してもつなぎ目で音が途切れない
        BandPass(data, rate, 1050f, 1700f, 2);
        MatchLoudness(data, samples);
        return data;
    }

    //low〜high Hz の音だけを残す（低い方と高い方をそれぞれ2回ずつ削る）。passes回くり返し通して最後の結果を残す
    static void BandPass(float[] data, int rate, float low, float high, int passes)
    {
        var filters = new[] { new Biquad(rate, low, false), new Biquad(rate, low, false), new Biquad(rate, high, true), new Biquad(rate, high, true) };
        var input = (float[])data.Clone();
        for (int pass = 0; pass < passes; pass++)
        {
            for (int i = 0; i < data.Length; i++)
            {
                float v = input[i];
                foreach (Biquad f in filters) v = f.Process(v);
                data[i] = v;
            }
        }
    }

    //種を決めた乱数（0以上1未満）。譜面エディタの seededRandom と同じ計算なので、ゲームとエディタで同じノイズになる
    class SeededRandom
    {
        uint state;
        public SeededRandom(uint seed) { state = seed; }

        public double Next()
        {
            unchecked
            {
                state += 0x6D2B79F5;
                uint t = state;
                t = (t ^ (t >> 15)) * (t | 1);
                t ^= t + (t ^ (t >> 7)) * (t | 61);
                return (t ^ (t >> 14)) / 4294967296.0;
            }
        }
    }

    //高い音か低い音を削るフィルター1段
    class Biquad
    {
        readonly double b0, b1, b2, a1, a2;
        double x1, x2, y1, y2;

        //lowPass: trueならcutoffより高い音を削る、falseなら低い音を削る
        public Biquad(int rate, double cutoff, bool lowPass)
        {
            double w = 2.0 * System.Math.PI * cutoff / rate;
            double alpha = System.Math.Sin(w) / (2.0 * 0.7071);
            double cos = System.Math.Cos(w);
            double a0 = 1.0 + alpha;
            if (lowPass) { b0 = (1.0 - cos) / 2.0; b1 = 1.0 - cos; b2 = (1.0 - cos) / 2.0; }
            else { b0 = (1.0 + cos) / 2.0; b1 = -(1.0 + cos); b2 = (1.0 + cos) / 2.0; }
            b0 /= a0; b1 /= a0; b2 /= a0;
            a1 = -2.0 * cos / a0; a2 = (1.0 - alpha) / a0;
        }

        public float Process(float x)
        {
            double y = b0 * x + b1 * x1 + b2 * x2 - a1 * y1 - a2 * y2;
            x2 = x1; x1 = x; y2 = y1; y1 = y;
            return (float)y;
        }
    }
    #endregion
}
