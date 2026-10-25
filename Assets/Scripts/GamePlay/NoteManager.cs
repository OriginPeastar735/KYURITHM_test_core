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

    //譜面の"meta"部分
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

        bpm = notesData.meta != null ? notesData.meta.bpm : 0f;
        if (bpm <= 0)
        {
            Debug.LogError($"{fileName}: bpmが読み込めませんでした");
            bpm = 120f;
        }
        barMillis = (60f / bpm) * 4f;//1小節あたりの時間(秒)

        foreach (var note in notesData.notes)
        {
            if (note.type == "tap")
                CreateNote(note.tick, note.lane, note.width, note.type);
            if (note.type == "hold")
                CreateHoldNote(note.tick, note.lane, note.width, note.type);
            if (note.type == "slide")
                CreateSlide(note.sections);
        }

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
