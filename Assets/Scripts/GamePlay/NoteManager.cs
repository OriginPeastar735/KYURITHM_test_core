using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System.IO;
using Newtonsoft.Json;
using System;

public class NoteManager : MonoBehaviour
{
    public static NoteManager instance;
    public GameObject NotePrefab;//Unity上でNoteプレハブを設定
    public GameObject HoldPrefab;//Unity上で黄色のholdプレハブを作成し割り当て
    public GameObject LongNotePrefab;

    public float bpm;
    [System.NonSerialized] public string title = "";//譜面のmetaの曲名。リザルト画面で表示する
    [System.NonSerialized] public string artist = "";//譜面のmetaのアーティスト名。リザルト画面で表示する

    public int totalCombo = 0;

    [Header("ハイスピード")]
    public float speedPerHiSpeed = 2f;//ハイスピード1あたり、ノーツが1秒に進む距離（ハイスピード5で10）
    [System.NonSerialized] public float scrollSpeed;//基本BPMの1小節あたりに進む距離。LoadJsonで選曲画面のハイスピードから計算する

    [Header("ノーツの見た目（長さはノーツの奥行きに対する割合）")]
    public float cornerRadiusRatio = 0.35f;//角の丸みの半径
    public bool showCenterLine = false;//ノーツの中心に線を入れるか（今は白いノーツで見えないので切っている）
    public Color centerLineColor = Color.white;//中心の線の色（slideの中間ノーツには線を入れない）
    public float centerLineWidthRatio = 0.08f;//中心の線の太さ
    public float centerLineInsetRatio = 0.7f;//中心の線の端からノーツの端までの長さ
    private Material centerLineMaterial;//線のマテリアル。最初のノーツのマテリアルを複製して色だけ変える
    public bool showOutline = true;//ノーツの周りに縁を付けるか
    //縁の色。左端がノーツの奥側（画面の上側）、右端が手前側（画面の下側）
    public Gradient outlineGradient = new Gradient
    {
        colorKeys = new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.black, 1f) },
        alphaKeys = new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 1f) }
    };
    public float outlineWidthRatio = 0.2f;//縁の太さ（縁はノーツの内側に描くので、隣り合うノーツの間にはこの2倍の縁が入る）
    private Material outlineMaterial;//縁のマテリアル。線と同じく最初のノーツのマテリアルを複製し、グラデーションのテクスチャを貼る
    private Texture2D outlineTexture;

    private float startTime;

    private float barMillis;//基本BPMでの1小節の秒数。ノーツは時刻をこの小節数に直して流すので、BPMが変わっても流れる速さは一定
    public int destroyedNotesCount = 0;
    public float barPertick = 1920f;

    //BPM変更線を含めたBPMの変化（tick順）。0tickには基本BPMが入る
    private readonly List<BpmChange> bpmTimeline = new List<BpmChange>();

    private struct BpmChange
    {
        public float tick;
        public float bpm;
    }

    //ノーツ情報
    //tickはエディタのMOVE/QUANTIZEで小数になることがあるのでfloatで読む
    [System.Serializable]
    public class NoteData
    {
        public string type;
        public int lane;
        public int width;
        public float tick;
        public float bpm;//BPM変更線の値
        public List<SlideSection> sections; //slideフォーマット対応のためのlist

        //slideの分岐。親slideの中間ノーツ(parentMidId)につながっている。
        //branchTypeが"end"なら始点が、"start"なら終点が親の中間ノーツと同じ位置になる
        //idはエディタ上で数値にも文字列にもなるのでobjectで受け取る
        public object parentId;
        public object parentMidId;
        public string branchType;
    }

    //slideのjsonフォーマットを認識するためのクラス
    [Serializable]
    public class SlideSection
    {
        public SlidePointer from;
        public SlidePointer to;
        public string curve;
    }

    //from,to内のフォーマットも認識できるようにする。
    //これでslideの全ての要素を取り出すことが可能になる
    [Serializable]
    public class SlidePointer
    {
        public int lane;
        public int width;
        public float tick;
    }

    //譜面の"meta"部分
    [Serializable]
    public class MetaData
    {
        public string title;
        public string artist;
        public float bpm;
        public float offset;//ms。正の値だとノーツが曲に対して遅れて来る
        public int? totalNotes;//エディタで数えた総ノーツ数（読み込み時の確認に使う）
    }

    [System.Serializable]
    public class NotesData
    {
        public MetaData meta;
        public List<NoteData> notes;
    }

    public List<Note> notes = new List<Note>();
    public List<Hold> holds = new List<Hold>();
    public List<LongNote> LongNotes = new List<LongNote>();

    void Awake()
    {
        if (instance == null)
        {
            instance = this;
        }
    }

    void OnDestroy()
    {
        if (centerLineMaterial != null) Destroy(centerLineMaterial);
        if (outlineMaterial != null) Destroy(outlineMaterial);
        if (outlineTexture != null) Destroy(outlineTexture);
    }

    //ノーツの角を丸め、withCenterLineなら中心に線を入れる。showOutlineなら周りに縁を付ける（SetRangeより前に呼ぶ）
    public void ApplyNoteStyle(LaneSurfaceShape shape, bool withCenterLine)
    {
        Material noteMaterial = shape.GetComponent<MeshRenderer>().sharedMaterial;
        if (withCenterLine && showCenterLine)
        {
            if (centerLineMaterial == null) centerLineMaterial = CreateColorMaterial(noteMaterial, "NoteCenterLine", centerLineColor);
            shape.SetStyle(cornerRadiusRatio, centerLineMaterial, centerLineWidthRatio, centerLineInsetRatio);
        }
        else
        {
            shape.SetStyle(cornerRadiusRatio);
        }

        if (showOutline)
        {
            if (outlineMaterial == null) outlineMaterial = CreateOutlineMaterial(noteMaterial);
            shape.SetOutline(outlineMaterial, outlineWidthRatio);
        }
    }

    //縁のマテリアル。色は白にして、outlineGradientを縦に並べたテクスチャを貼る
    //ノーツの板のUVのvは奥側(-z)が0、手前側(+z)が1なので、テクスチャの下端が奥側の色になる
    //（ノーツは-z側から判定ライン(z=0)に向かって流れてくるので、-z側が画面の上側）
    private Material CreateOutlineMaterial(Material source)
    {
        const int size = 64;
        outlineTexture = new Texture2D(1, size) { name = "NoteOutlineGradient", wrapMode = TextureWrapMode.Clamp };
        for (int y = 0; y < size; y++)
        {
            outlineTexture.SetPixel(0, y, outlineGradient.Evaluate(y / (size - 1f)));
        }
        outlineTexture.Apply();

        Material material = CreateColorMaterial(source, "NoteOutline", Color.white);
        material.mainTexture = outlineTexture;
        return material;
    }

    //ノーツと同じシェーダーのまま色だけ変えたマテリアルを作る（ビルドでシェーダーが含まれないことを防ぐため、新しくシェーダーを探さない）
    private Material CreateColorMaterial(Material source, string name, Color color)
    {
        return new Material(source) { name = name, color = color };
    }

    // Start is called before the first frame update
    void Start()
    {
        //bpmとstartTime(譜面オフセット)はLoadJsonで譜面から設定する（Startの実行順によっては上書きしてしまうため、ここでは設定しない）
    }

    public void LoadJson(string fileName)
    {
        //StreamingAssets/Songs の譜面（選曲画面から来たとき）か、Resourcesのデフォルト譜面を読む
        NotesData notesData = JsonConvert.DeserializeObject<NotesData>(SongLibrary.ReadChart(fileName));

        //判定回数の合計。各Create関数でノーツの判定数を加算していく
        totalCombo = 0;

        title = notesData.meta?.title ?? "";
        artist = notesData.meta?.artist ?? "";

        bpm = notesData.meta != null ? notesData.meta.bpm : 0f;
        if (bpm <= 0)
        {
            Debug.LogError($"{fileName}: bpmが読み込めませんでした");
            bpm = 120f;
        }
        barMillis = (60f / bpm) * 4f;//1小節あたりの時間(秒)
        //1秒に進む距離をハイスピードで決めるので、曲のBPMが違っても同じハイスピードなら同じ速さで流れる
        scrollSpeed = PlaySession.HiSpeed * speedPerHiSpeed * barMillis;

        //譜面のオフセット(ms)だけ、ノーツの時刻を曲に対してずらす（エディタの再生と同じ扱い）
        startTime = notesData.meta != null ? notesData.meta.offset / 1000f : 0f;

        BuildBpmTimeline(notesData.notes);

        foreach (var note in notesData.notes)
        {
            if (note.type == "tap")
                CreateNote(note.tick, note.lane, note.width, note.type);
            if (note.type == "hold")
                CreateHoldNote(note.tick, note.lane, note.width, note.type);
            if (note.type == "slide")
                CreateSlide(note);
        }

        Debug.Log($"Notes:{totalCombo}");
        //エディタと数え方がずれていると、スコアの理論値が101万点にならない
        if (notesData.meta != null && notesData.meta.totalNotes.HasValue && notesData.meta.totalNotes.Value != totalCombo)
        {
            Debug.LogWarning($"{fileName}: 総ノーツ数がエディタの値({notesData.meta.totalNotes.Value})と一致しません。古いエディタで出力した譜面の可能性があります");
        }
    }

    //基本BPMとBPM変更線からBPMの変化を作る（同じtickに複数ある場合は後のものを使う）
    private void BuildBpmTimeline(List<NoteData> notes)
    {
        var bpmByTick = new SortedDictionary<float, float> { [0f] = bpm };
        foreach (var note in notes)
        {
            if (note.type == "bpm" && note.bpm > 0) bpmByTick[note.tick] = note.bpm;
        }

        bpmTimeline.Clear();
        foreach (var pair in bpmByTick)
        {
            bpmTimeline.Add(new BpmChange { tick = pair.Key, bpm = pair.Value });
        }
    }

    //tickの位置が曲の何秒目か（BPM変更線ごとに区間を分けて計算する）
    //1小節(1920tick)が4拍なので、1秒あたりのtick数は BPM × 8
    private float TickToTime(float tick)
    {
        if (tick <= 0) return 0f;
        float time = 0f;
        for (int i = 0; i < bpmTimeline.Count; i++)
        {
            float ticksPerSec = bpmTimeline[i].bpm * 8f;
            bool isLast = i == bpmTimeline.Count - 1;
            if (isLast || tick <= bpmTimeline[i + 1].tick)
            {
                return time + (tick - bpmTimeline[i].tick) / ticksPerSec;
            }
            time += (bpmTimeline[i + 1].tick - bpmTimeline[i].tick) / ticksPerSec;
        }
        return time;
    }

    private float BpmAtTick(float tick)
    {
        float result = bpmTimeline[0].bpm;
        for (int i = 0; i < bpmTimeline.Count && bpmTimeline[i].tick <= tick; i++)
        {
            result = bpmTimeline[i].bpm;
        }
        return result;
    }

    //slideの一定間隔判定の間隔(tick)。BPM120未満は16分、240未満は8分、それ以上は4分
    private float SlideJudgeInterval(float bpm)
    {
        if (bpm < 120f) return barPertick / 16f;
        if (bpm < 240f) return barPertick / 8f;
        return barPertick / 4f;
    }


    private void CreateNote(float tick, int lane, int width, string type)
    {
        float expectedTime = ExpectedTime(tick);//各ノーツの理想タイミング
        float bar = DisplayBar(expectedTime);

        GameObject obj = Instantiate(NotePrefab);
        Note note = obj.GetComponent<Note>();
        note.scrollSpeed = scrollSpeed;
        note.Init(bar, expectedTime, lane, width);
        totalCombo += 1;
        //Debug.Log($"{rail.name} worldX={rail.position.x}");

        //まとめることができるならswitch文でnotesにadd,holdにadd...とかができそう

        notes.Add(note);
    }

    private void CreateHoldNote(float tick, int lane, int width, string type)
    {
        float expectedTime = ExpectedTime(tick);//各ノーツの理想タイミング
        float bar = DisplayBar(expectedTime);

        GameObject obj = Instantiate(HoldPrefab);
        Hold hold = obj.GetComponent<Hold>();
        hold.scrollSpeed = scrollSpeed;
        hold.Init(bar, expectedTime, lane, width);
        totalCombo += 1;
        //Debug.Log($"{rail.name} worldX={rail.position.x}");

        //まとめることができるならswitch文でnotesにadd,holdにadd...とかができそう

        holds.Add(hold);
    }

    //slideはセクションごとに1つのLongNoteとして生成する
    //ノーツ数 = 始点 + 中間 + 一定間隔判定 + 終点（エディタのSPEC SLIDEと同じ数え方）
    private void CreateSlide(NoteData note)
    {
        var sections = note.sections;
        if (sections == null || sections.Count == 0) return;

        //分岐の接続点は親slideの中間ノーツとして判定するので、こちらでは判定も表示もしない
        bool isBranch = note.parentId != null && note.parentMidId != null;
        bool startIsShared = isBranch && note.branchType == "end";
        bool endIsShared = isBranch && note.branchType == "start";

        if (!startIsShared) totalCombo += 1;//始点のタップ判定
        for (int i = 0; i < sections.Count; i++)
        {
            var section = sections[i];
            bool isLast = i == sections.Count - 1;
            float startExpectedTime = ExpectedTime(section.from.tick);
            float endExpectedTime = ExpectedTime(section.to.tick);
            List<float> checkpointTimes = SlideCheckpointTimes(section.from.tick, section.to.tick, !(isLast && endIsShared));

            GameObject obj = Instantiate(LongNotePrefab);
            LongNote longNote = obj.GetComponent<LongNote>();
            longNote.scrollSpeed = scrollSpeed;

            longNote.Init(DisplayBar(startExpectedTime), DisplayBar(endExpectedTime), startExpectedTime, endExpectedTime,
                          section.from.lane, section.from.width, section.to.lane, section.to.width,
                          i == 0 && !startIsShared, isLast, !(isLast && endIsShared), checkpointTimes,
                          LongNote.ParseCurve(section.curve));

            LongNotes.Add(longNote);
            totalCombo += checkpointTimes.Count;
        }
    }

    //セクション中の判定点の時刻。始点(または中間)から一定間隔で置き、終点(または次の中間)にも置く
    //間隔は判定点ごとにその位置のBPMで決めるので、途中でBPMが変わると次の判定から間隔が変わる
    private List<float> SlideCheckpointTimes(float startTick, float endTick, bool includeEnd)
    {
        var times = new List<float>();
        for (float tick = startTick + SlideJudgeInterval(BpmAtTick(startTick)); tick < endTick - 1e-3f; tick += SlideJudgeInterval(BpmAtTick(tick)))
        {
            times.Add(ExpectedTime(tick));
        }
        if (includeEnd) times.Add(ExpectedTime(endTick));
        return times;
    }

    // ノーツの理想タイミング(秒)を計算する関数
    private float ExpectedTime(float tick)
    {
        return startTime + TickToTime(tick);
    }

    // ノーツを流す位置（基本BPMでの小節数）。時刻に比例するので、BPM変更があっても一定の速さで流れる
    private float DisplayBar(float expectedTime)
    {
        return (expectedTime - startTime) / barMillis;
    }

    void Update()
    {
        float currentTime = MusicManager.instance.CurrentPlayTime;
        float presentBar = (currentTime - startTime) / barMillis;

        foreach (var note in new List<Note>(notes))
        {
            if (note != null)
                note.UpdatePosition(presentBar);
        }

        foreach (var hold in new List<Hold>(holds))
        {
            if (hold != null)
                hold.UpdatePosition(presentBar);
        }

        foreach (var longNote in new List<LongNote>(LongNotes))
        {
            if (longNote != null)
                longNote.UpdatePosition(presentBar);
        }
    }

    //譜面を読み込んだ後に、tap/hold/slideがすべて判定されてリストが空になったか（曲の終了判定に使う）
    public bool AllNotesCleared => totalCombo > 0 && notes.Count == 0 && holds.Count == 0 && LongNotes.Count == 0;

    public void RemoveNote(Note note)
    {
        notes.Remove(note);
    }

    public void RemoveHold(Hold hold)
    {
        holds.Remove(hold);
    }

    public void RemoveLongNote(LongNote longNote)
    {
        LongNotes.Remove(longNote);
    }



}
