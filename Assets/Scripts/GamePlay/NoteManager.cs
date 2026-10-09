//KYURITHM用にjsonファイル読み込みロジックを変更しています


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
    public GameObject IsoNotePrefab;
    public GameObject LongNotePrefab;

    public Transform DRailBase;
    public Transform FRailBase;
    public Transform JRailBase;
    public Transform KRailBase;

    public float bpm;

    public int totalCombo = 0;

    public float scrollSpeed = 200f;

    private float startTime;

    private float barMillis;
    public int destroyedNotesCount = 0;
    public float barPertick = 1920f;
    public int slideCheckpointTick = 240;//slide中の判定点の間隔(tick)。1920で1小節なので240は8分音符ごと

    //ノーツ情報
    [System.Serializable]
    public class NoteData
    {
        public float bar;
        public string type;
        public int lane;
        public int width;
        public int tick;
        public List<SlideSection> sections; //slideフォーマット対応のためのlist
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
        public int tick;
    }

    //KYURITHMフォーマットの"meta"部分
    [Serializable]
    public class MetaData
    {
        public string title;
        public float bpm;
        public float offset;
    }

    [System.Serializable]
    public class NotesData
    {
        public MetaData meta;
        public float bpm;//旧フォーマット（トップレベルにbpm）用
        public List<NoteData> notes;
        // public List<NoteData> SNotes;
        // public List<NoteData> DNotes;
        // public List<NoteData> FNotes;
        // public List<NoteData> JNotes;
        // public List<NoteData> KNotes;
        // public List<NoteData> LNotes;
        // public List<NoteData> DLongNotes;
        // public List<NoteData> FLongNotes;
        // public List<NoteData> JLongNotes;
        // public List<NoteData> KLongNotes;
    }

    public List<Note> notes = new List<Note>();
    public List<Hold> holds = new List<Hold>();
    public List<LongNote> LongNotes = new List<LongNote>();
    // public List<Note> SNotes = new List<Note>();
    // public List<Note> DNotes = new List<Note>();
    // public List<Note> FNotes = new List<Note>();
    // public List<Note> JNotes = new List<Note>();
    // public List<Note> KNotes = new List<Note>();
    // public List<Note> LNotes = new List<Note>();
    // public List<LongNote> DLongNotes = new List<LongNote>();
    // public List<LongNote> FLongNotes = new List<LongNote>();
    // public List<LongNote> JLongNotes = new List<LongNote>();
    // public List<LongNote> KLongNotes = new List<LongNote>();

    private float[] previousExpectedTime = new float[4];//ロングノーツ描画のための一時変数
    private float[] previousNoteBar = new float[4];//ロングノーツ描画のための一時変数

    const int D = 0;
    const int F = 1;
    const int J = 2;
    const int K = 3;

    void Awake()
    {
        if (instance == null)
        {
            instance = this;
        }
    }

    // Start is called before the first frame update
    void Start()
    {
        startTime = 0f;//後で変更
        //bpmはLoadJsonで譜面から設定する（Startの実行順によっては上書きしてしまうため、ここでは設定しない）
    }

    public void LoadJson(string fileName)
    {
        TextAsset jsonFile = Resources.Load<TextAsset>(fileName);
        NotesData notesData = JsonConvert.DeserializeObject<NotesData>(jsonFile.text);

        //判定回数の合計。各Create関数でノーツの判定数を加算していく
        totalCombo = 0;

        //KYURITHMフォーマットはmeta内、旧フォーマットはトップレベルのbpmを使う
        bpm = notesData.meta != null ? notesData.meta.bpm : notesData.bpm;
        if (bpm <= 0)
        {
            Debug.LogError($"{fileName}: bpmが読み込めませんでした");
            bpm = 120f;
        }
        barMillis = (60f / bpm) * 4f;//1小節あたりの時間(秒)

        //holdノートに使えるかも
        // foreach (var note in notesData.SNotes)
        // {
        //     CreateIsoNote(note.bar, "S", DRailBase, note.type);
        // }

        foreach (var note in notesData.notes)
        {
            if (note.type == "tap")
                CreateNote(note.tick, note.lane, note.width, note.type);
            if (note.type == "hold")
                CreateHoldNote(note.tick, note.lane, note.width, note.type);
            if (note.type == "slide")
                CreateSlide(note.sections);
            // CreateSlideNote(note.sections);
        }

        // foreach (var note in notesData.DLongNotes)
        // {
        //     float expectedTime = startTime + note.bar * barMillis;
        //     if (note.type == "s")
        //     {
        //         previousExpectedTime[D] = expectedTime;
        //         previousNoteBar[D] = note.bar;
        //     }
        //     else if (note.type == "e")
        //     {
        //         CreateLongNote(previousNoteBar[D], note.bar, "D", DRailBase, previousExpectedTime[D], expectedTime);
        //     }
        // }

        Debug.Log($"Notes:{totalCombo}");
    }


    private void CreateNote(int tick, int lane, int width, string type)
    {
        float bar = CulcBar(tick);//1920tickで1小節
        float expectedTime = ExpectedTime(bar);//各ノーツの理想タイミング

        GameObject obj = Instantiate(NotePrefab);
        Note note = obj.GetComponent<Note>();
        note.scrollSpeed = scrollSpeed;
        note.Init(bar, expectedTime, lane, width);
        totalCombo += 1;
        //Debug.Log($"{rail.name} worldX={rail.position.x}");

        //まとめることができるならswitch文でnotesにadd,holdにadd...とかができそう

        notes.Add(note);
    }

    private void CreateHoldNote(int tick, int lane, int width, string type)
    {
        float bar = CulcBar(tick);//1920tickで1小節
        float expectedTime = ExpectedTime(bar);//各ノーツの理想タイミング

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
    private void CreateSlide(List<SlideSection> sections)
    {
        totalCombo += 1;//始点のタップ判定
        for (int i = 0; i < sections.Count; i++)
        {
            var section = sections[i];
            float startBar = CulcBar(section.from.tick);
            float endBar = CulcBar(section.to.tick);
            float startExpectedTime = ExpectedTime(startBar);
            float endExpectedTime = ExpectedTime(endBar);
            List<float> checkpointTimes = SlideCheckpointTimes(section.from.tick, section.to.tick);

            GameObject obj = Instantiate(LongNotePrefab);
            LongNote longNote = obj.GetComponent<LongNote>();
            longNote.scrollSpeed = scrollSpeed;

            longNote.Init(startBar, endBar, startExpectedTime, endExpectedTime,
                          section.from.lane, section.from.width, section.to.lane, section.to.width,
                          i == 0, i == sections.Count - 1, checkpointTimes,
                          LongNote.ParseCurve(section.curve));

            LongNotes.Add(longNote);
            totalCombo += checkpointTimes.Count;
        }
    }

    //slide中の判定点の時刻。始点の後からslideCheckpointTickごとに置き、終点にも必ず置く
    private List<float> SlideCheckpointTimes(int startTick, int endTick)
    {
        var times = new List<float>();
        if (slideCheckpointTick > 0)
        {
            for (int tick = startTick + slideCheckpointTick; tick < endTick; tick += slideCheckpointTick)
            {
                times.Add(ExpectedTime(CulcBar(tick)));
            }
        }
        times.Add(ExpectedTime(CulcBar(endTick)));
        return times;
    }

    // ノーツの理想タイミングを計算する関数
    private float ExpectedTime(float bar)
    {
        return startTime + bar * barMillis;
    }

    // ノーツの小節位置を計算する関数
    private float CulcBar(int tick)
    {
        return tick / barPertick;
    }

    // ノーツのレーン位置を計算する関数（計算本体はLaneLayoutに集約）
    private float LanePos(int lane)
    {
        return LaneLayout.LanePos(lane);
    }




    // private void CreateIsoNote(float bar, string railStr, Transform rail, string type)
    // {
    //     GameObject obj = Instantiate(IsoNotePrefab, rail);
    //     Note note = obj.GetComponent<Note>();
    //     note.scrollSpeed = scrollSpeed;

    //     float expectedTime = startTime + bar * barMillis;//各ノーツの理想タイミング

    //     note.Init(bar, expectedTime);
    //     //Debug.Log($"{rail.name} worldX={rail.position.x}");


    //     switch (railStr)
    //     {
    //         case "S":
    //             SNotes.Add(note);
    //             break;
    //         case "L":
    //             LNotes.Add(note);
    //             break;
    //         default:
    //             break;
    //     }
    //     Notes.Add(note);
    // }
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
