using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System.IO;
using Newtonsoft.Json;
using System;

//譜面jsonを読み込んでノーツを生成し、毎フレーム曲の再生時間に合わせてノーツを動かす
public class NoteManager : MonoBehaviour
{
    public static NoteManager instance;
    public GameObject NotePrefab;//tapノーツのプレハブ
    public GameObject HoldPrefab;//holdノーツのプレハブ
    public GameObject LongNotePrefab;//slideの1セクション分のプレハブ

    public float bpm;
    [System.NonSerialized] public string title = "";//譜面のmetaの曲名。リザルト画面で表示する
    [System.NonSerialized] public string artist = "";//譜面のmetaのアーティスト名。リザルト画面で表示する

    public int totalCombo = 0;

    [Header("ハイスピード")]
    public float speedPerHiSpeed = 6f;//ハイスピード1あたり、ノーツが1秒に進む距離（ハイスピード5で30）
    [System.NonSerialized] public float scrollSpeed;//基本BPMの1小節あたりに進む距離。LoadJsonで選曲画面のハイスピードから計算する

    [Header("ノーツの見た目（長さはノーツの奥行きに対する割合）")]
    public float cornerRadiusRatio = 0.35f;//角の丸みの半径
    public bool showCenterLine = false;//ノーツの中心に線を入れるか（白いノーツでは見えないのでOFF）
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

    private float startTime;//譜面のオフセット（秒）。tick 0 のノーツが判定ラインに来る時刻

    private float barMillis;//基本BPMでの1小節の秒数。ノーツは時刻をこの小節数に直して流すので、BPMが変わっても流れる速さは一定
    public int destroyedNotesCount = 0;
    public float barPertick = 1920f;//1小節のtick数
    public const int LeadInBars = 2;//曲の再生を始める前に、小節線だけを流す小節数（基本BPM）。プレイはここから始まる

    //BPM変更線を含めたBPMの変化（tick順）。0tickには基本BPMが入る
    private readonly List<BpmChange> bpmTimeline = new List<BpmChange>();

    private struct BpmChange
    {
        public float tick;
        public float bpm;
    }

    [Header("小節線（選曲画面の設定がONのときだけ流す）")]
    public float barLineThickness = 0.05f;//小節線の太さ（奥行き）
    public float barLineHeight = 0.012f;//面から浮かせる高さ。判定ライン(約0.0155)・ノーツ(LaneLayout.NoteHeight)より下
    public float barLineAlpha = 0.6f;
    private readonly List<BarLine> barLines = new List<BarLine>();//時刻順
    private Mesh barLineMesh;//全部の小節線で共有する（z位置だけ変える）
    private Material barLineMaterial;

    private struct BarLine
    {
        public GameObject obj;
        public float bar;//流す位置（DisplayBarの小節数）
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
        if (barLineMesh != null) Destroy(barLineMesh);
        if (barLineMaterial != null) Destroy(barLineMaterial);
        if (centerLineMaterial != null) Destroy(centerLineMaterial);
        if (outlineMaterial != null) Destroy(outlineMaterial);
        if (outlineTexture != null) Destroy(outlineTexture);
    }

    //ノーツの角を丸め、withCenterLineなら中心に線を入れる。showOutlineなら周りに縁を付ける（SetRangeより前に呼ぶ）
    //ノーツの太さ（奥行き）も、選曲画面の設定に合わせてここで変える
    public void ApplyNoteStyle(LaneSurfaceShape shape, bool withCenterLine)
    {
        //プレハブの奥行きを太さ50%とする。LaneSurfaceShapeはSetStyleの時点のスケールを奥行きとして使うので、その前に変える
        Vector3 scale = shape.transform.localScale;
        scale.z *= PlaySession.NoteThicknessPercent / 50f;
        shape.transform.localScale = scale;

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

    void Start()
    {
        //bpmとstartTime(譜面オフセット)はLoadJsonで譜面から設定する（ここで設定すると、Startの実行順によってはLoadJsonの値を上書きしてしまう）
    }

    public void LoadJson(string fileName)
    {
        //StreamingAssets/Songs の譜面（選曲画面から来たとき）か、Resourcesのデフォルト譜面を読む
        //読めない譜面はノーツ0個として扱う（曲が最後まで流れればリザルトに進めるので、プレイ画面で止まらない）
        ChartData notesData = null;
        try
        {
            notesData = JsonConvert.DeserializeObject<ChartData>(SongLibrary.ReadChart(fileName));
        }
        catch (Exception e)
        {
            Debug.LogError($"{fileName}: 譜面を読み込めませんでした\n{e.Message}");
        }
        LoadChart(notesData ?? new ChartData(), fileName);
    }

    //読み込んだ譜面からノーツを生成する。チュートリアルではTutorialが作った譜面を練習ごとに渡す
    //前の譜面のノーツがすべて判定され終わってから呼ぶ（BPMやオフセットはこの譜面のものに置き換わる）
    public void LoadChart(ChartData notesData, string fileName)
    {
        if (notesData.notes == null) notesData.notes = new List<ChartNoteData>();
        notesData.notes.RemoveAll(note => note == null);

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
                totalCombo += CreateSlide(note, false);
        }

        //判定と表示の処理は、リストが時刻順に並んでいることを前提に途中で打ち切る
        notes.Sort((a, b) => a.expectedTime.CompareTo(b.expectedTime));
        holds.Sort((a, b) => a.expectedTime.CompareTo(b.expectedTime));
        LongNotes.Sort((a, b) => a.startExpectedTime.CompareTo(b.startExpectedTime));

        //小節線はプレイ中だけ（チュートリアルでは出さない）
        if (PlaySession.ShowBarLines && !PlaySession.IsTutorial) CreateBarLines(notesData.notes);

        Debug.Log($"Notes:{totalCombo}");
        //エディタと数え方がずれていると、スコアの理論値が101万点にならない
        if (notesData.meta != null && notesData.meta.totalNotes.HasValue && notesData.meta.totalNotes.Value != totalCombo)
        {
            Debug.LogWarning($"{fileName}: 総ノーツ数がエディタの値({notesData.meta.totalNotes.Value})と一致しません。古いエディタで出力した譜面の可能性があります");
        }

        //生成したノーツは判定ライン(z=0)に置かれているので、すぐ流れてくる位置へ動かす
        //（このフレームのUpdateが済んだ後に読み込むと、1フレームだけ判定ラインの所に見えてしまうため）
        UpdatePositions();
    }

    //ノーツが壁（見え始める位置）から判定ラインまで流れてくる秒数
    //基本BPMでの1小節の秒数
    public float BarSeconds => barMillis;

    //曲の再生を始める前に待つ秒数（LeadInBars小節）
    public float LeadInSeconds => LeadInBars * barMillis;

    public float TravelSeconds => LaneLayout.VisibleLength / (PlaySession.HiSpeed * speedPerHiSpeed);

    //基本BPMとBPM変更線からBPMの変化を作る（同じtickに複数ある場合は後のものを使う）
    private void BuildBpmTimeline(List<ChartNoteData> notes)
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
        float expectedTime = ExpectedTime(tick);
        float bar = DisplayBar(expectedTime);

        GameObject obj = Instantiate(NotePrefab);
        Note note = obj.GetComponent<Note>();
        note.scrollSpeed = scrollSpeed;
        note.Init(bar, expectedTime, lane, width);
        totalCombo += 1;
        notes.Add(note);
    }

    private void CreateHoldNote(float tick, int lane, int width, string type)
    {
        float expectedTime = ExpectedTime(tick);
        float bar = DisplayBar(expectedTime);

        GameObject obj = Instantiate(HoldPrefab);
        Hold hold = obj.GetComponent<Hold>();
        hold.scrollSpeed = scrollSpeed;
        hold.Init(bar, expectedTime, lane, width);
        totalCombo += 1;
        holds.Add(hold);
    }

    //譜面のノーツ数（判定の回数）を、ノーツを生成せずに数える。チュートリアルで全練習の合計からスコアを計算するのに使う
    //BPMの変化はこの譜面のものに置き換わるので、ノーツを流している間は呼ばない
    public int CountNotes(ChartData notesData)
    {
        bpm = notesData.meta != null && notesData.meta.bpm > 0 ? notesData.meta.bpm : 120f;
        BuildBpmTimeline(notesData.notes);
        int count = 0;
        foreach (var note in notesData.notes)
        {
            if (note.type == "tap" || note.type == "hold") count += 1;
            if (note.type == "slide") count += CreateSlide(note, true);
        }
        return count;
    }

    //slideはセクションごとに1つのLongNoteとして生成する。戻り値は判定の回数
    //ノーツ数 = 始点 + 中間 + 一定間隔判定 + 終点（エディタのSPEC SLIDEと同じ数え方）
    //countOnly: 数えるだけで生成しない
    private int CreateSlide(ChartNoteData note, bool countOnly)
    {
        var sections = note.sections;
        if (sections == null || sections.Count == 0) return 0;
        if (sections.Exists(section => section == null || section.from == null || section.to == null))
        {
            if (!countOnly) Debug.LogWarning($"始点・終点の無いセクションがあるslideを読み飛ばしました（tick {note.tick}）");
            return 0;
        }

        //分岐の接続点は親slideの中間ノーツとして判定するので、こちらでは判定も表示もしない
        bool isBranch = note.parentId != null && note.parentMidId != null;
        bool startIsShared = isBranch && note.branchType == "end";
        bool endIsShared = isBranch && note.branchType == "start";

        int count = startIsShared ? 0 : 1;//始点のタップ判定
        for (int i = 0; i < sections.Count; i++)
        {
            var section = sections[i];
            bool isLast = i == sections.Count - 1;
            float startExpectedTime = ExpectedTime(section.from.tick);
            float endExpectedTime = ExpectedTime(section.to.tick);
            List<float> checkpointTimes = SlideCheckpointTimes(section.from.tick, section.to.tick, !(isLast && endIsShared));
            count += checkpointTimes.Count;
            if (countOnly) continue;

            GameObject obj = Instantiate(LongNotePrefab);
            LongNote longNote = obj.GetComponent<LongNote>();
            longNote.scrollSpeed = scrollSpeed;

            longNote.Init(DisplayBar(startExpectedTime), DisplayBar(endExpectedTime), startExpectedTime, endExpectedTime,
                          section.from.lane, section.from.width, section.to.lane, section.to.width,
                          i == 0 && !startIsShared, isLast, !(isLast && endIsShared), checkpointTimes,
                          LongNote.ParseCurve(section.curve));

            LongNotes.Add(longNote);
        }
        return count;
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

    //tickの位置のノーツが判定ラインに来る時刻（曲の再生時間、秒）。譜面のオフセットを含む
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
        UpdatePositions();
    }

    //リストは時刻順なので、壁より奥にあって既に非表示になっているノーツが来たら、それ以降は動かさない
    //（生成直後は表示状態なので、一度すべて動かして壁より奥のものを非表示にする）
    private void UpdatePositions()
    {
        if (MusicManager.instance == null) return;
        float currentTime = MusicManager.instance.CurrentPlayTime;
        float presentBar = (currentTime - startTime) / barMillis;
        float farBar = presentBar + LaneLayout.VisibleLength / scrollSpeed;//壁の位置の小節位置

        UpdateBarLines(presentBar, farBar);

        foreach (var note in notes)
        {
            if (note == null) continue;
            if (!note.gameObject.activeSelf && note.noteBar > farBar) break;
            note.UpdatePosition(presentBar);
        }

        foreach (var hold in holds)
        {
            if (hold == null) continue;
            if (!hold.gameObject.activeSelf && hold.noteBar > farBar) break;
            hold.UpdatePosition(presentBar);
        }

        foreach (var longNote in LongNotes)
        {
            if (longNote == null) continue;
            if (!longNote.gameObject.activeSelf && longNote.startBar > farBar) break;
            longNote.UpdatePosition(presentBar);
        }
    }

    // ===== 小節線 =====
    //曲の再生前のLeadInBars小節ぶんと、tick 0から最後のノーツのある小節まで、1小節(barPertick)ごとに小節線を置く
    //譜面の小節線はtickで区切るので、BPM変更があっても譜面の小節に合う。再生前の小節線は再生開始(0秒)から基本BPMの1小節ずつさかのぼる
    private void CreateBarLines(List<ChartNoteData> chartNotes)
    {
        foreach (BarLine line in barLines) Destroy(line.obj);
        barLines.Clear();

        float lastTick = 0f;
        foreach (var note in chartNotes)
        {
            if (note.type == "bpm") continue;
            lastTick = Mathf.Max(lastTick, note.tick);
            if (note.sections == null) continue;
            foreach (var section in note.sections)
            {
                if (section?.to != null) lastTick = Mathf.Max(lastTick, section.to.tick);
            }
        }

        if (barLineMaterial == null && LaneFieldBuilder.instance != null) barLineMaterial = LaneFieldBuilder.instance.CreateBarLineMaterial(barLineAlpha);
        if (barLineMaterial == null) return;
        if (barLineMesh == null) barLineMesh = CreateBarLineMesh();

        for (int i = LeadInBars; i >= 1; i--) AddBarLine(DisplayBar(-i * barMillis));
        for (float tick = 0f; tick <= lastTick + 1e-3f; tick += barPertick) AddBarLine(DisplayBar(ExpectedTime(tick)));
    }

    //bar: 流す位置（DisplayBarの小節数）
    private void AddBarLine(float bar)
    {
        var obj = new GameObject("BarLine", typeof(MeshFilter), typeof(MeshRenderer));
        obj.GetComponent<MeshFilter>().sharedMesh = barLineMesh;
        var rend = obj.GetComponent<MeshRenderer>();
        rend.sharedMaterial = barLineMaterial;
        rend.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        rend.receiveShadows = false;
        obj.SetActive(false);//壁より手前に来たら出す
        barLines.Add(new BarLine { obj = obj, bar = bar });
    }

    //判定ラインを過ぎた小節線は消し、壁より手前のものを動かす（時刻順なので、壁より奥のものが来たら打ち切る）
    private void UpdateBarLines(float presentBar, float farBar)
    {
        while (barLines.Count > 0 && barLines[0].bar < presentBar)
        {
            Destroy(barLines[0].obj);
            barLines.RemoveAt(0);
        }
        foreach (BarLine line in barLines)
        {
            if (line.bar > farBar) break;
            if (!line.obj.activeSelf) line.obj.SetActive(true);
            line.obj.transform.localPosition = new Vector3(0f, 0f, (presentBar - line.bar) * scrollSpeed);
        }
    }

    //左の傾斜・水平・右の傾斜の3区間を横切る細い板（z=0が線の真ん中）。面に沿わせて浮かせる
    private Mesh CreateBarLineMesh()
    {
        float[] folds = { 0f, LaneLayout.LeftFold, LaneLayout.RightFold, InputManager.LaneCount };
        const float inset = 0.001f;//折れ目ちょうどだと隣の区間の傾きになるので、区間の内側の点で傾きを決める
        float half = barLineThickness / 2f;
        var vertices = new List<Vector3>();
        var normals = new List<Vector3>();
        var triangles = new List<int>();
        for (int s = 0; s + 1 < folds.Length; s++)
        {
            float from = folds[s] + inset;
            float to = folds[s + 1] - inset;
            int first = vertices.Count;
            vertices.Add(LaneLayout.SurfacePoint(from, barLineHeight, -half));
            vertices.Add(LaneLayout.SurfacePoint(from, barLineHeight, half));
            vertices.Add(LaneLayout.SurfacePoint(to, barLineHeight, -half));
            vertices.Add(LaneLayout.SurfacePoint(to, barLineHeight, half));
            Vector3 up = LaneLayout.SurfaceRotation((from + to) / 2f) * Vector3.up;
            for (int i = 0; i < 4; i++) normals.Add(up);
            triangles.AddRange(new[] { first, first + 1, first + 2, first + 2, first + 1, first + 3 });
        }
        var mesh = new Mesh { name = "BarLine" };
        mesh.SetVertices(vertices);
        mesh.SetNormals(normals);
        mesh.SetTriangles(triangles, 0);
        mesh.RecalculateBounds();
        return mesh;
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
