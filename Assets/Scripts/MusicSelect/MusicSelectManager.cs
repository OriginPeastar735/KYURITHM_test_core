using System.Collections;
using System.Collections.Generic;
using UnityEngine;

//選曲画面の操作エリア。センサーのレーン範囲ごとに役割を割り当てる
public enum SelectZone
{
    SortPrev,//左斜面の上側4レーン(0〜3)
    SortNext,//左斜面の下側4レーン(4〜7)
    Left,//底面の左5レーン(8〜12)
    Decide,//底面の中央6レーン(13〜18)
    Right,//底面の右5レーン(19〜23)
    DiffDown,//右斜面の下側4レーン(24〜27)
    DiffUp//右斜面の上側4レーン(28〜31)
}

//選曲画面の操作と状態を管理する。表示はMusicSelectViewに任せる
public class MusicSelectManager : MonoBehaviour
{
    public const int ZoneCount = 7;
    //各エリアの先頭レーンとレーン数（SelectZoneの順）
    static readonly int[] zoneStartLane = { 0, 4, 8, 13, 19, 24, 28 };
    static readonly int[] zoneLaneCount = { 4, 4, 5, 6, 5, 4, 4 };

    //コントローラー無しでのテスト用キー。プレイ画面のキー配置(A S D F J K L ;)と位置を合わせている
    static readonly KeyCode[][] zoneKeys =
    {
        new[] { KeyCode.A },
        new[] { KeyCode.S },
        new[] { KeyCode.D, KeyCode.LeftArrow },
        new[] { KeyCode.F, KeyCode.J, KeyCode.Return, KeyCode.Space },
        new[] { KeyCode.K, KeyCode.RightArrow },
        new[] { KeyCode.L },
        new[] { KeyCode.Semicolon },
    };

    [Header("制限時間(秒)")]
    public float timeLimit = 99f;

    [Header("操作の速度制限(秒)")]
    public float moveInterval = 0.15f;//楽曲移動の最短間隔。連打してもこれより速くは動かない
    public float holdRepeatDelay = 0.45f;//長押ししてから連続移動が始まるまでの時間
    public float holdRepeatInterval = 0.2f;//長押し中の連続移動の間隔
    public float switchInterval = 0.25f;//難易度・ソート切り替えの最短間隔
    public float startInputLock = 0.5f;//シーン開始直後に入力を受け付けない時間（前の画面からの押しっぱなし対策）
    public float decideDelay = 0.6f;//決定してからプレイ画面に移るまでの時間

    //ソートボタンを押すとこの順に切り替わる（端まで行くと反対側に戻る）
    enum SortMode { TitleAsc, LevelAsc, LevelDesc, ScoreAsc, ScoreDesc }
    static readonly string[] sortNames = { "曲名 昇順", "レベル 昇順", "レベル 降順", "スコア 昇順", "スコア 降順" };

    private List<SongData> songs;
    private List<int> order = new List<int>();//今の難易度がある曲だけをソートした並び（songsのインデックス）
    private SortMode sortMode = SortMode.TitleAsc;
    private int position = 0;//選択位置。左右に何周しても動けるよう範囲外の値も取り、使うときにorderの長さで割った余りにする
    private int difficulty = 0;//選んでいる難易度。最初は一番低い難易度
    private int maxDifficulty;//どれかの曲に存在する一番高い難易度

    private MusicSelectView view;
    private float remainingTime;
    private bool decided = false;
    private float inputLockUntil;

    private readonly bool[] held = new bool[ZoneCount];
    private readonly bool[] prevHeld = new bool[ZoneCount];
    private float nextMoveTime;//次に楽曲移動してよい時刻
    private float nextRepeatTime;//長押しで次に移動する時刻
    private float nextSwitchTime;//次に難易度・ソートを切り替えてよい時刻

    void Start()
    {
        //選曲画面にはInputManagerが置かれていないので、無ければ作る
        //キーボードはこのクラスで専用に読むので、InputManager側はコントローラーのみにする
        if (InputManager.instance == null)
        {
            var input = new GameObject("InputManager").AddComponent<InputManager>();
            input.SetSource(InputSource.Controller);
        }

        songs = PlaceholderSongs.Create();
        foreach (var song in songs) maxDifficulty = Mathf.Max(maxDifficulty, song.difficulties.Count - 1);
        ApplySort();

        view = gameObject.AddComponent<MusicSelectView>();
        view.Build(SongAt, DisplayDifficulty);
        view.SetPlayer(PlaySession.PlayerName, PlaySession.TrackNumber);
        view.SetSortName(sortNames[(int)sortMode]);
        view.Snap(position);

        remainingTime = timeLimit;
        inputLockUntil = Time.time + startInputLock;
    }

    void Update()
    {
        ReadZones();
        //これ以上下げられない・上げられない難易度ボタンは光らせない
        view.SetZoneEnabled(SelectZone.DiffDown, difficulty > 0);
        view.SetZoneEnabled(SelectZone.DiffUp, difficulty < maxDifficulty);
        for (int z = 0; z < ZoneCount; z++) view.SetZoneLit((SelectZone)z, held[z] && !decided);

        if (!decided)
        {
            remainingTime -= Time.deltaTime;
            view.SetTimer(Mathf.Max(0, Mathf.CeilToInt(remainingTime)));
            if (remainingTime <= 0)
            {
                Decide();//時間切れになったら選択中の曲で決定
            }
            else if (Time.time >= inputLockUntil)
            {
                HandleMove();
                if (Pressed(SelectZone.DiffDown)) TrySwitch(() => ChangeDifficulty(-1));
                if (Pressed(SelectZone.DiffUp)) TrySwitch(() => ChangeDifficulty(1));
                if (Pressed(SelectZone.SortPrev)) TrySwitch(() => ChangeSort(-1));
                if (Pressed(SelectZone.SortNext)) TrySwitch(() => ChangeSort(1));
                if (Pressed(SelectZone.Decide)) Decide();
            }
        }

        System.Array.Copy(held, prevHeld, ZoneCount);
    }

    //センサー(InputManager)とテスト用キーから各エリアが押されているかを読む
    void ReadZones()
    {
        for (int z = 0; z < ZoneCount; z++)
        {
            bool h = InputManager.instance != null && InputManager.instance.AnyHeld(zoneStartLane[z], zoneLaneCount[z]);
            foreach (var key in zoneKeys[z])
            {
                if (Input.GetKey(key)) h = true;
            }
            held[z] = h;
        }
    }

    bool Pressed(SelectZone zone)
    {
        return held[(int)zone] && !prevHeld[(int)zone];
    }

    // ===== 楽曲の左右移動 =====
    //押した瞬間に1曲動き、押し続けるとholdRepeatDelay後からholdRepeatInterval毎に動く
    //連打・長押しどちらでもmoveIntervalより速くは動かない
    void HandleMove()
    {
        bool left = held[(int)SelectZone.Left];
        bool right = held[(int)SelectZone.Right];
        if (left == right) return;//どちらも押していない、または両方押している

        int dir = right ? 1 : -1;
        if (Pressed(right ? SelectZone.Right : SelectZone.Left))
        {
            nextRepeatTime = Time.time + holdRepeatDelay;
            TryMove(dir);
        }
        else if (Time.time >= nextRepeatTime)
        {
            nextRepeatTime = Time.time + holdRepeatInterval;
            TryMove(dir);
        }
    }

    void TryMove(int dir)
    {
        if (Time.time < nextMoveTime) return;
        nextMoveTime = Time.time + moveInterval;
        position += dir;
        view.SetTarget(position);
    }

    // ===== 難易度・ソート =====
    void TrySwitch(System.Action action)
    {
        if (Time.time < nextSwitchTime) return;
        nextSwitchTime = Time.time + switchInterval;
        action();
    }

    //難易度を変えると、その難易度がある曲だけの並びに作り直す
    void ChangeDifficulty(int delta)
    {
        int next = Mathf.Clamp(difficulty + delta, 0, maxDifficulty);
        if (next == difficulty) return;
        difficulty = next;
        ResortKeepingSelection();
    }

    void ChangeSort(int delta)
    {
        int count = sortNames.Length;
        sortMode = (SortMode)((((int)sortMode + delta) % count + count) % count);
        view.SetSortName(sortNames[(int)sortMode]);
        ResortKeepingSelection();
    }

    //並び替えても選んでいた曲を選んだままにする
    //選んでいた曲が新しい並びに無い場合（その難易度が無い曲）は、元の並びでその次にある曲を選ぶ
    void ResortKeepingSelection()
    {
        var oldOrder = new List<int>(order);
        int oldIndex = Mod(position, oldOrder.Count);
        ApplySort();

        position = 0;
        for (int i = 0; i < oldOrder.Count; i++)
        {
            int index = order.IndexOf(oldOrder[(oldIndex + i) % oldOrder.Count]);
            if (index >= 0)
            {
                position = index;
                break;
            }
        }
        view.Snap(position);
    }

    void ApplySort()
    {
        order.Clear();
        for (int i = 0; i < songs.Count; i++)
        {
            if (songs[i].difficulties.Count > difficulty) order.Add(i);
        }

        order.Sort((a, b) =>
        {
            int c = 0;
            switch (sortMode)
            {
                case SortMode.LevelAsc: c = Chart(a).level.CompareTo(Chart(b).level); break;
                case SortMode.LevelDesc: c = Chart(b).level.CompareTo(Chart(a).level); break;
                case SortMode.ScoreAsc: c = Chart(a).highScore.CompareTo(Chart(b).highScore); break;
                case SortMode.ScoreDesc: c = Chart(b).highScore.CompareTo(Chart(a).highScore); break;
            }
            //同じ値どうしは曲名の昇順
            return c != 0 ? c : string.CompareOrdinal(songs[a].title, songs[b].title);
        });
    }

    //songs[songIndex]の今の難易度の譜面
    DifficultyData Chart(int songIndex)
    {
        return songs[songIndex].difficulties[difficulty];
    }

    // ===== 決定 =====
    void Decide()
    {
        if (decided) return;
        decided = true;
        SongData song = SelectedSong;
        int diff = DisplayDifficulty(song);
        PlaySession.MusicIndex = song.id;
        PlaySession.Difficulty = diff;
        PlaySession.ChartName = song.difficulties[diff].chartName;
        view.PlayDecide();
        Invoke(nameof(GoToPlay), decideDelay);
    }

    void GoToPlay()
    {
        SceneLoader.ChangeScene(GameScene.Play);
    }

    // ===== Viewに渡す情報 =====
    SongData SelectedSong => SongAt(position);

    //選択位置pの曲（左右に何周しても同じ並びが繰り返される）
    SongData SongAt(int p)
    {
        return songs[order[Mod(p, order.Count)]];
    }

    //その曲で表示する難易度。並びには今の難易度がある曲しか入らないので、常に選んでいる難易度になる
    int DisplayDifficulty(SongData song)
    {
        return difficulty;
    }

    static int Mod(int a, int n)
    {
        return (a % n + n) % n;
    }
}
