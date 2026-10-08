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

        totalCombo = 0;

        totalCombo += notesData.notes.Count;

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
        float lanePos = LanePos(lane);
        //ロングノーツの終点の時、始点のときのexpectedTimeを持ってくれば描画できるかも

        GameObject obj = Instantiate(NotePrefab);//railを親、objを子として生成
        Note note = obj.GetComponent<Note>();
        note.scrollSpeed = scrollSpeed;
        note.Init(bar, expectedTime, lanePos);
        //Debug.Log($"{rail.name} worldX={rail.position.x}");

        //まとめることができるならswitch文でnotesにadd,holdにadd...とかができそう

        notes.Add(note);
    }

    private void CreateHoldNote(int tick, int lane, int width, string type)
    {
        float bar = CulcBar(tick);//1920tickで1小節
        float expectedTime = ExpectedTime(bar);//各ノーツの理想タイミング
        float lanePos = LanePos(lane);
        //ロングノーツの終点の時、始点のときのexpectedTimeを持ってくれば描画できるかも

        GameObject obj = Instantiate(HoldPrefab);//railを親、objを子として生成
        Hold hold = obj.GetComponent<Hold>();
        hold.scrollSpeed = scrollSpeed;
        hold.Init(bar, expectedTime, lanePos);
        //Debug.Log($"{rail.name} worldX={rail.position.x}");

        //まとめることができるならswitch文でnotesにadd,holdにadd...とかができそう

        holds.Add(hold);
    }

    private void CreateSlide(List<SlideSection> sections)
    {
        foreach(var section in sections){
        float startBar = CulcBar(section.from.tick);
        float endBar = CulcBar(section.to.tick);
        float startExpectedTime = ExpectedTime(startBar);
        float endExpectedTime = ExpectedTime(endBar);
        float startLanePos = LanePos(section.from.lane);
        float endLanePos = LanePos(section.to.lane);
        

        GameObject obj = Instantiate(LongNotePrefab);
        LongNote longNote = obj.GetComponent<LongNote>();
        longNote.scrollSpeed = scrollSpeed;

        longNote.Init(startBar, endBar, startExpectedTime, endExpectedTime, startLanePos, endLanePos);

        LongNotes.Add(longNote);
        }

    }

    private void CreateLongNote(float startBar, float endBar, string railStr, Transform rail, float longStartTime, float longEndTime)
    {
        GameObject obj = Instantiate(LongNotePrefab, rail);//あとでプレハブ作ってね
        LongNote longNote = obj.GetComponent<LongNote>();
        longNote.scrollSpeed = scrollSpeed;

        longNote.Init(startBar, endBar, longStartTime, longEndTime, longStartTime, longEndTime);

        LongNotes.Add(longNote);
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

    public void RemoveNote(Note note, string lane)
    {
        notes.Remove(note);
        // switch (lane)
        // {
        // ここもまとめれるならswitch文でまとめたい
        // }
    }

    public void RemoveHold(Hold hold, string lane)
    {
        holds.Remove(hold);
    }

    public void RemoveLongNote(LongNote longNote, string lane)
    {
        LongNotes.Remove(longNote);
    }



}
