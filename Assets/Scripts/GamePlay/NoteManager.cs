//KYURITHM用にjsonファイル読み込みロジックを変更しています


using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System.IO;
using Newtonsoft.Json;

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

    //ノーツ情報
    [System.Serializable]
    public class NoteData
    {
        public float bar;
        public string type;
        public int lane;
        public int width;
        public int tick;
    }

    [System.Serializable]
    public class NotesData
    {
        public float bpm;
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
        bpm = 158;
        barMillis = (60f / bpm) * 4f;//1小節あたりの時間(ms)
    }

    public void LoadJson(string fileName)
    {
        TextAsset jsonFile = Resources.Load<TextAsset>(fileName);
        NotesData notesData = JsonConvert.DeserializeObject<NotesData>(jsonFile.text);

        totalCombo = 0;

        totalCombo += notesData.notes.Count;

        bpm = notesData.bpm;
        barMillis = (60f / bpm) * 4f;

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
            if (note.type == "slide") { }
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


    private void CreateNote(float tick, int lane, int width, string type)
    {
        float bar = tick / 1920f;//1920tickで1小節
        float expectedTime = startTime + bar * barMillis;//各ノーツの理想タイミング
        float lanePos = 3 - (lane / 4f);
        //ロングノーツの終点の時、始点のときのexpectedTimeを持ってくれば描画できるかも

        GameObject obj = Instantiate(NotePrefab);//railを親、objを子として生成
        Note note = obj.GetComponent<Note>();
        note.scrollSpeed = scrollSpeed;
        note.Init(bar, expectedTime, lanePos);
        //Debug.Log($"{rail.name} worldX={rail.position.x}");

        //まとめることができるならswitch文でnotesにadd,holdにadd...とかができそう

        notes.Add(note);
    }

    private void CreateHoldNote(float tick, int lane, int width, string type)
    {
        float bar = tick / 1920f;//1920tickで1小節
        float expectedTime = startTime + bar * barMillis;//各ノーツの理想タイミング
        float lanePos = 3 - (lane / 4f);
        //ロングノーツの終点の時、始点のときのexpectedTimeを持ってくれば描画できるかも

        GameObject obj = Instantiate(HoldPrefab);//railを親、objを子として生成
        Hold hold = obj.GetComponent<Hold>();
        hold.scrollSpeed = scrollSpeed;
        hold.Init(bar, expectedTime, lanePos);
        //Debug.Log($"{rail.name} worldX={rail.position.x}");

        //まとめることができるならswitch文でnotesにadd,holdにadd...とかができそう

        holds.Add(hold);
    }

    private void CreateSlideNote()
    {

    }


    private void CreateLongNote(float startBar, float endBar, string railStr, Transform rail, float longStartTime, float longEndTime)
    {
        GameObject obj = Instantiate(LongNotePrefab, rail);//あとでプレハブ作ってね
        LongNote longNote = obj.GetComponent<LongNote>();
        longNote.scrollSpeed = scrollSpeed;

        longNote.Init(startBar, endBar, longStartTime, longEndTime, railStr);

        LongNotes.Add(longNote);
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
