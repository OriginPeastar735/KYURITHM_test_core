using System;
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
//
//画面は3段階で切り替わる
//  SelectSong（楽曲選択）  --決定-->  Confirm（楽曲カード / 設定カード）  --設定カードで決定-->  Settings（各種設定カード）
//                         <--戻る--                                       <--戻る・決定--
//  ConfirmでSTARTを押すとプレイ画面へ。左斜面（ソート切り替え）はConfirm・Settingsでは戻るボタンになる
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
    public float moveInterval = 0.12f;//楽曲移動の最短間隔。連打してもこれより速くは動かない
    public float holdRepeatDelay = 0.3f;//長押ししてから連続移動が始まるまでの時間
    public float holdRepeatInterval = 0.12f;//長押し中の連続移動の間隔
    public float settingRepeatInterval = 0.05f;//設定の −/+ を長押ししているときの連続変更の間隔
    public float switchInterval = 0.2f;//難易度・ソート切り替え、画面の切り替えの最短間隔
    public float startInputLock = 0.5f;//シーン開始直後に入力を受け付けない時間（前の画面からの押しっぱなし対策）
    public float sortPopupDuration = 2.5f;//ソートのポップアップを最後の操作から何秒出しておくか

    [Header("楽曲紹介画面(秒)")]
    public float introFadeIn = 1f;//ジャケットと見出しが出てくるまで
    public float introReveal = 1f;//曲名などを1文字ずつ出し切り、ジャケット裏の正方形がずれるまで（文字数に関係なくこの時間）
    public float introHold = 3f;//出し切ったあと見せておく時間
    public float introFadeOut = 1f;//紹介画面が薄くなってプレイ画面が見えるまで

    enum Phase { SelectSong, Confirm, Settings }

    //ソートボタンを押すとこの順に切り替わる（端まで行くと反対側に戻る）
    enum SortMode { TitleAsc, TitleDesc, LevelAsc, LevelDesc, ScoreAsc, ScoreDesc }
    static readonly string[] sortNames = { "曲名 昇順", "曲名 降順", "レベル 昇順", "レベル 降順", "スコア 昇順", "スコア 降順" };

    //設定カード1枚分
    class SettingItem
    {
        public string name;
        public string description;
        public Func<string> value;//表示する値
        public Func<int, bool> canChange;//-1/+1の方向に変えられるか
        public Action<int> change;//-1/+1の方向に変える
        public string minusLabel = "−";//右斜面の −/+ のエリアに出す文字（ON/OFFの設定ではOFF/ONにする）
        public string plusLabel = "+";
    }

    private List<SongData> songs;
    private List<int> order = new List<int>();//今の難易度がある曲だけをソートした並び（songsのインデックス）
    private SortMode sortMode = SortMode.TitleAsc;
    private int difficulty = 0;//選んでいる難易度。最初は一番低い難易度
    private int maxDifficulty;//どれかの曲に存在する一番高い難易度
    private List<SettingItem> settings;

    private Phase phase = Phase.SelectSong;
    //今の画面のカードの位置。楽曲選択では左右に何周しても動けるよう範囲外の値も取る
    //楽曲選択のカードは order の曲 ＋ 最後にランダム選曲カード1枚。最初の曲(位置0)の左隣がランダム選曲カードになる
    private int position = 0;
    private int songPosition = 0;//楽曲選択画面での位置（Confirm・Settingsから戻るときに使う）
    private SongData chosenSong;//Confirmで選ばれている曲（ランダム選曲カードなら抽選された曲）

    private MusicSelectView view;
    private float remainingTime;
    private bool started = false;
    private float inputLockUntil;
    private bool sortPopupOpen = false;
    private float sortPopupUntil;

    private readonly bool[] held = new bool[ZoneCount];
    private readonly bool[] prevHeld = new bool[ZoneCount];
    private float nextMoveTime;//次に移動してよい時刻
    private float nextRepeatTime;//長押しで次に移動する時刻
    private float nextSettingRepeatTime;//長押しで次に設定を変える時刻
    private float nextSwitchTime;//次に切り替えてよい時刻

    void Start()
    {
        //選曲画面にはInputManagerが置かれていないので、無ければ作る
        //キーボードはこのクラスで専用に読むので、InputManager側はコントローラーのみにする
        if (InputManager.instance == null)
        {
            var input = new GameObject("InputManager").AddComponent<InputManager>();
            input.SetSource(InputSource.Controller);
        }

        //StreamingAssets/Songs の曲を読む。1曲も無ければ（曲データをまだ置いていない環境など）仮の曲を表示する
        songs = SongLibrary.LoadAll();
        if (songs.Count == 0)
        {
            Debug.Log("StreamingAssets/Songs に曲が無いので、仮の曲を表示します");
            songs = PlaceholderSongs.Create();
        }
        ApplyHighScores();
        foreach (var song in songs) maxDifficulty = Mathf.Max(maxDifficulty, song.difficulties.Count - 1);
        if (PlaySession.HasLastSelection) RestoreLastSelection();
        else ApplySort();
        settings = CreateSettings();

        view = gameObject.AddComponent<MusicSelectView>();
        view.Build(ItemAt);
        view.SetPlayer(PlaySession.PlayerName, PlaySession.TrackNumber, PlaySession.IsFinalTrack);
        RefreshInfo();
        view.Snap(position);

        remainingTime = timeLimit;
        inputLockUntil = Time.time + startInputLock;
    }

    void Update()
    {
        ReadZones();
        UpdateZoneDisplay();

        if (!started)
        {
            remainingTime -= Time.deltaTime;
            view.SetTimer(Mathf.Max(0, Mathf.CeilToInt(remainingTime)));
            if (remainingTime <= 0)
            {
                StartGame();//時間切れになったら選んでいる曲でスタート
            }
            else if (Time.time >= inputLockUntil && !view.IsTransitioning)//画面切り替えのアニメーション中は操作できない
            {
                switch (phase)
                {
                    case Phase.SelectSong: HandleSelectSong(); break;
                    case Phase.Confirm: HandleConfirm(); break;
                    case Phase.Settings: HandleSettings(); break;
                }
            }
            if (sortPopupOpen && Time.time >= sortPopupUntil) CloseSortPopup();
        }

        Array.Copy(held, prevHeld, ZoneCount);
    }

    // ===== 入力 =====
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

    //Confirm・Settingsでは左斜面全体が戻るボタン
    bool BackPressed => Pressed(SelectZone.SortPrev) || Pressed(SelectZone.SortNext);

    // ===== 画面ごとの操作 =====
    void HandleSelectSong()
    {
        if (Pressed(SelectZone.SortPrev)) TrySwitch(() => PressSort(-1));
        if (Pressed(SelectZone.SortNext)) TrySwitch(() => PressSort(1));

        //ポップアップが出ているときに他のボタンを押したら閉じる。決定はポップアップを閉じるだけにする
        if (sortPopupOpen && (Pressed(SelectZone.Left) || Pressed(SelectZone.Right) || Pressed(SelectZone.Decide)
                              || Pressed(SelectZone.DiffDown) || Pressed(SelectZone.DiffUp)))
        {
            CloseSortPopup();
            if (Pressed(SelectZone.Decide)) return;
        }

        HandleMove();
        if (Pressed(SelectZone.DiffDown)) TrySwitch(() => ChangeDifficulty(-1));
        if (Pressed(SelectZone.DiffUp)) TrySwitch(() => ChangeDifficulty(1));
        if (Pressed(SelectZone.Decide)) TrySwitch(() => EnterConfirm(0));
    }

    //カード0: 楽曲カード（STARTでプレイ開始） カード1: 設定カード（決定で各種設定へ）
    //右斜面でこの曲の難易度を切り替えられる
    void HandleConfirm()
    {
        if (BackPressed)
        {
            TrySwitch(BackToSelectSong);
            return;
        }
        HandleMove();
        if (Pressed(SelectZone.DiffDown)) TrySwitch(() => ChangeDifficulty(-1));
        if (Pressed(SelectZone.DiffUp)) TrySwitch(() => ChangeDifficulty(1));
        if (Pressed(SelectZone.Decide))
        {
            //決定の連打で確認画面を飛ばさないよう、STARTも切り替え間隔の制限をかける
            if (position == 0) TrySwitch(StartGame);
            else TrySwitch(EnterSettings);
        }
    }

    //右斜面の −/+ で値を変える（長押しで連続して変わる）。戻る・決定で楽曲カード・設定カードの画面に戻る
    void HandleSettings()
    {
        if (BackPressed || Pressed(SelectZone.Decide))
        {
            TrySwitch(() => EnterConfirm(1));
            return;
        }
        HandleMove();
        int dir = RepeatDirection(SelectZone.DiffDown, SelectZone.DiffUp, ref nextSettingRepeatTime, settingRepeatInterval);
        if (dir != 0) ChangeSetting(dir);
    }

    // ===== 長押しの連続操作 =====
    //minus/plusのエリアを、押した瞬間に1回、押し続けるとholdRepeatDelay後からinterval毎に繰り返し操作する
    //このフレームで操作する向き(-1/+1)を返す。操作しないフレームは0
    int RepeatDirection(SelectZone minus, SelectZone plus, ref float nextRepeat, float interval)
    {
        bool minusHeld = held[(int)minus];
        bool plusHeld = held[(int)plus];
        if (minusHeld == plusHeld) return 0;//どちらも押していない、または両方押している

        int dir = plusHeld ? 1 : -1;
        if (Pressed(plusHeld ? plus : minus))
        {
            nextRepeat = Time.time + holdRepeatDelay;
            return dir;
        }
        if (Time.time >= nextRepeat)
        {
            nextRepeat = Time.time + interval;
            return dir;
        }
        return 0;
    }

    // ===== カードの左右移動 =====
    //押した瞬間に1枚動き、押し続けるとholdRepeatDelay後からholdRepeatInterval毎に動く
    //連打・長押しどちらでもmoveIntervalより速くは動かない
    void HandleMove()
    {
        int dir = RepeatDirection(SelectZone.Left, SelectZone.Right, ref nextRepeatTime, holdRepeatInterval);
        if (dir != 0) TryMove(dir);
    }

    void TryMove(int dir)
    {
        if (Time.time < nextMoveTime || !CanMove(dir)) return;
        nextMoveTime = Time.time + moveInterval;
        position += dir;
        view.SetTarget(position);
    }

    //楽曲選択は円循環するのでいつでも動ける。Confirm・Settingsは端で止まる
    bool CanMove(int dir)
    {
        if (phase == Phase.SelectSong) return true;
        int next = position + dir;
        return next >= 0 && next < ItemCount;
    }

    int ItemCount => phase == Phase.Confirm ? 2 : phase == Phase.Settings ? settings.Count : order.Count + 1;

    // ===== 画面の切り替え =====
    void EnterConfirm(int focus)
    {
        //楽曲選択から来たときは進む向き、設定から戻ってきたときは戻る向きのアニメーションにする
        bool forward = phase == Phase.SelectSong;
        if (forward)
        {
            songPosition = position;
            chosenSong = SelectedSong();
        }
        CloseSortPopup();
        SetPhase(Phase.Confirm, focus, forward);
    }

    void BackToSelectSong()
    {
        SetPhase(Phase.SelectSong, songPosition, false);
    }

    void EnterSettings()
    {
        SetPhase(Phase.Settings, 0, true);
    }

    //アニメーションでカードが消えたところで画面を切り替える
    void SetPhase(Phase next, int nextPosition, bool forward)
    {
        view.Transition(forward, () =>
        {
            phase = next;
            position = nextPosition;
            view.Snap(position);
            RefreshInfo();
        });
    }

    //前の曲を選んだときのソート・難易度にし、遊んだ曲（ランダム選曲なら抽選された曲）を選んだ状態にする
    void RestoreLastSelection()
    {
        sortMode = (SortMode)Mathf.Clamp(PlaySession.SortMode, 0, sortNames.Length - 1);
        difficulty = Mathf.Clamp(PlaySession.Difficulty, 0, maxDifficulty);
        ApplySort();
        int index = order.FindIndex(i => songs[i].id == PlaySession.MusicIndex);
        if (index >= 0) position = index;
    }

    //プレイ画面で記録したハイスコアを曲リストに反映する（記録の無い譜面は読み込んだときの値のまま）
    void ApplyHighScores()
    {
        foreach (var song in songs)
        {
            foreach (var diff in song.difficulties)
            {
                if (HighScoreStore.Has(diff.chartName)) diff.highScore = HighScoreStore.Get(diff.chartName);
            }
        }
    }

    //楽曲紹介画面を出してからプレイ画面へ
    void StartGame()
    {
        if (started) return;
        started = true;
        SongData song = phase == Phase.SelectSong ? SelectedSong() : chosenSong;
        PlaySession.MusicIndex = song.id;
        PlaySession.Difficulty = difficulty;
        PlaySession.ChartName = song.difficulties[difficulty].chartName;
        PlaySession.SortMode = (int)sortMode;
        PlaySession.HasLastSelection = true;
        PlaySession.AudioPath = song.audioPath;
        CloseSortPopup();
        view.PlayDecide();
        //紹介画面を出している間にプレイ画面を読み込み、そのままプレイ画面へ移る
        view.PlayIntro(song, difficulty, introFadeIn, introReveal, introHold, introFadeOut);
    }

    // ===== 操作エリアの表示 =====
    //今の画面で押せるエリアだけを光らせ、エリアの文字を画面に合わせて変える
    void UpdateZoneDisplay()
    {
        bool selecting = phase == Phase.SelectSong;

        view.SetSlopeTitle(true, selecting ? "ソート切り替え" : "戻る");
        view.SetSlopeMerged(true, !selecting);//戻るボタンは斜面全体で1つのボタンとして光らせる
        view.SetZoneLabel(SelectZone.SortPrev, selecting ? "←" : "");
        view.SetZoneLabel(SelectZone.SortNext, selecting ? "→" : "");
        view.SetZoneLabel(SelectZone.Left, "← 左");
        view.SetZoneLabel(SelectZone.Right, "右 →");
        view.SetZoneLabel(SelectZone.Decide, selecting ? "決定" : phase == Phase.Settings ? "OK" : position == 0 ? "START" : "設定");
        view.SetSlopeTitle(false, phase == Phase.Settings ? "設定変更" : "難易度切り替え");
        SettingItem setting = phase == Phase.Settings ? settings[position] : null;
        view.SetZoneLabel(SelectZone.DiffDown, setting != null ? setting.minusLabel : "−");
        view.SetZoneLabel(SelectZone.DiffUp, setting != null ? setting.plusLabel : "+");

        view.SetZoneEnabled(SelectZone.Left, CanMove(-1));
        view.SetZoneEnabled(SelectZone.Right, CanMove(1));
        view.SetZoneEnabled(SelectZone.DiffDown, CanChangeRight(-1));
        view.SetZoneEnabled(SelectZone.DiffUp, CanChangeRight(1));

        //戻るボタンのときは左斜面のどちらを押しても全体を光らせる
        bool backHeld = !selecting && (held[(int)SelectZone.SortPrev] || held[(int)SelectZone.SortNext]);
        for (int z = 0; z < ZoneCount; z++)
        {
            bool lit = held[z] || (backHeld && (z == (int)SelectZone.SortPrev || z == (int)SelectZone.SortNext));
            view.SetZoneLit((SelectZone)z, lit && !started);
        }
    }

    //右斜面(−/+)が押せるか。楽曲選択・Confirmでは難易度、設定では値の変更
    bool CanChangeRight(int dir)
    {
        if (phase == Phase.Settings) return settings[position].canChange(dir);
        return dir < 0 ? difficulty > 0 : difficulty < MaxSelectableDifficulty;
    }

    //選べる一番高い難易度。Confirmでは選んだ曲にある難易度まで
    int MaxSelectableDifficulty => phase == Phase.Confirm ? chosenSong.difficulties.Count - 1 : maxDifficulty;

    //左上の表示。ソートはポップアップで確認できるので、楽曲選択では何も出さない
    void RefreshInfo()
    {
        switch (phase)
        {
            case Phase.SelectSong: view.SetInfo(""); break;
            case Phase.Confirm: view.SetInfo("楽曲確認"); break;
            case Phase.Settings: view.SetInfo("プレイ設定"); break;
        }
    }

    // ===== ソート =====
    void TrySwitch(Action action)
    {
        if (Time.time < nextSwitchTime) return;
        nextSwitchTime = Time.time + switchInterval;
        action();
    }

    //1回目はポップアップを出して今のソートを見せるだけ。出ている間に押すとソートを変える
    void PressSort(int delta)
    {
        if (sortPopupOpen) ChangeSort(delta);
        sortPopupOpen = true;
        sortPopupUntil = Time.time + sortPopupDuration;
        view.ShowSortPopup(sortNames, (int)sortMode);
    }

    void CloseSortPopup()
    {
        sortPopupOpen = false;
        view.HideSortPopup();
    }

    void ChangeSort(int delta)
    {
        int count = sortNames.Length;
        sortMode = (SortMode)((((int)sortMode + delta) % count + count) % count);
        position = Resort(position);
        view.Snap(position);
    }

    //難易度を変えると、その難易度がある曲だけの並びに作り直す
    //Confirmで変えたときも楽曲選択の並びを作り直しておき、戻ったときに同じ曲が選ばれているようにする
    void ChangeDifficulty(int delta)
    {
        int next = Mathf.Clamp(difficulty + delta, 0, MaxSelectableDifficulty);
        if (next == difficulty) return;
        difficulty = next;
        if (phase == Phase.SelectSong)
        {
            position = Resort(position);
            view.Snap(position);
        }
        else
        {
            songPosition = Resort(songPosition);
            view.RefreshCards();
        }
    }

    //並び替えたあとの、keepPositionで選んでいた曲の位置を返す
    //ランダム選曲カードはランダム選曲カードのまま。選んでいた曲が新しい並びに無い場合（その難易度が無い曲）は、元の並びでその次にある曲を選ぶ
    int Resort(int keepPosition)
    {
        var oldOrder = new List<int>(order);
        int oldIndex = Mod(keepPosition, oldOrder.Count + 1);
        ApplySort();

        if (oldIndex == oldOrder.Count) return order.Count;
        for (int i = 0; i < oldOrder.Count; i++)
        {
            int index = order.IndexOf(oldOrder[(oldIndex + i) % oldOrder.Count]);
            if (index >= 0) return index;
        }
        return 0;
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
                case SortMode.TitleDesc: c = string.CompareOrdinal(songs[b].title, songs[a].title); break;
                case SortMode.LevelAsc: c = Chart(a).level.CompareTo(Chart(b).level); break;
                case SortMode.LevelDesc: c = Chart(b).level.CompareTo(Chart(a).level); break;
                case SortMode.ScoreAsc: c = Chart(a).highScore.CompareTo(Chart(b).highScore); break;
                case SortMode.ScoreDesc: c = Chart(b).highScore.CompareTo(Chart(a).highScore); break;
            }
            //同じ値どうし（と曲名昇順）は曲名の昇順
            return c != 0 ? c : string.CompareOrdinal(songs[a].title, songs[b].title);
        });
    }

    //songs[songIndex]の今の難易度の譜面
    DifficultyData Chart(int songIndex)
    {
        return songs[songIndex].difficulties[difficulty];
    }

    // ===== 設定 =====
    //値はPlaySessionに保存する（プレイ画面への反映はまだ）
    List<SettingItem> CreateSettings()
    {
        return new List<SettingItem>
        {
            new SettingItem
            {
                name = "ハイスピード",
                description = "ノーツが流れてくる速さ",
                value = () => PlaySession.HiSpeed.ToString("0.0"),
                //1.0〜12.0を0.1刻み。0.1を足し続けると誤差が溜まるので、毎回小数第1位に丸める
                canChange = d => d < 0 ? PlaySession.HiSpeed > 1.05f : PlaySession.HiSpeed < 11.95f,
                change = d => PlaySession.HiSpeed = Mathf.Clamp(Mathf.Round((PlaySession.HiSpeed + d * 0.1f) * 10f) / 10f, 1f, 12f),
            },
            new SettingItem
            {
                name = "壁",
                description = "レーンの奥側を見えなくする（0%で全部見える）",
                value = () => $"{PlaySession.WallPercent}%",
                canChange = d => d < 0 ? PlaySession.WallPercent > 0 : PlaySession.WallPercent < 100,
                change = d => PlaySession.WallPercent = Mathf.Clamp(PlaySession.WallPercent + d, 0, 100),
            },
            new SettingItem
            {
                name = "タイミング調整",
                description = "判定のタイミングをずらす",
                value = () => $"{PlaySession.TimingOffsetMs:+0;-0;0}ms",
                canChange = d => d < 0 ? PlaySession.TimingOffsetMs > -100 : PlaySession.TimingOffsetMs < 100,
                change = d => PlaySession.TimingOffsetMs = Mathf.Clamp(PlaySession.TimingOffsetMs + d * 5, -100, 100),
            },
            new SettingItem
            {
                name = "FAST/LATE表示",
                description = "判定が早いか遅いかを表示する",
                value = () => PlaySession.ShowFastLate ? "ON" : "OFF",
                canChange = d => d < 0 ? PlaySession.ShowFastLate : !PlaySession.ShowFastLate,
                change = d => PlaySession.ShowFastLate = d > 0,
                minusLabel = "OFF",
                plusLabel = "ON",
            },
        };
    }

    void ChangeSetting(int dir)
    {
        SettingItem item = settings[position];
        if (!item.canChange(dir)) return;
        item.change(dir);
        view.RefreshCards();
    }

    // ===== Viewに渡すカードの内容 =====
    CardInfo ItemAt(int p)
    {
        switch (phase)
        {
            case Phase.Confirm:
                if (p == 0) return CardInfo.FromSong(chosenSong, difficulty);
                if (p == 1) return SettingsCard();
                return null;
            case Phase.Settings:
                return p >= 0 && p < settings.Count ? SettingItemCard(settings[p]) : null;
            default:
                int index = SelectIndex(p);
                return index == order.Count ? RandomCard() : CardInfo.FromSong(songs[order[index]], difficulty);
        }
    }

    //ランダム選曲カードは灰色の簡素なデザイン
    CardInfo RandomCard()
    {
        return new CardInfo
        {
            background = new Color(0.68f, 0.68f, 0.68f),
            squareColor = new Color(0.45f, 0.45f, 0.45f),
            squareBorder = new Color(0.05f, 0.15f, 0.2f),
            squareText = "?",
            squareTextSize = 200,
            title = "ランダム選曲",
            titleSize = 48,
            titleInBottomCenter = true,
        };
    }

    static readonly Color settingColor = new Color(0.32f, 0.38f, 0.5f);

    CardInfo SettingsCard()
    {
        return new CardInfo
        {
            background = settingColor,
            topSmall = "OPTION",
            topLarge = "設定",
            squareColor = new Color(0.2f, 0.24f, 0.33f),
            squareText = "SETTINGS",
            squareTextSize = 56,
            title = "プレイ設定",
            titleSize = 32,
            subtitle = $"ハイスピード {PlaySession.HiSpeed:0.0} / タイミング {PlaySession.TimingOffsetMs:+0;-0;0}ms",
            subtitleSize = 26,
        };
    }

    CardInfo SettingItemCard(SettingItem item)
    {
        return new CardInfo
        {
            background = settingColor,
            topSmall = "SETTING",
            topLarge = item.name,
            squareColor = new Color(0.2f, 0.24f, 0.33f),
            squareText = item.value(),
            squareTextSize = 96,
            //中央の値に比べて小さくなりすぎないよう、説明文は大きめにする
            title = item.description,
            titleSize = 32,
            subtitle = $"右斜面の {item.minusLabel} / {item.plusLabel} で変更",
            subtitleSize = 28,
        };
    }

    //楽曲選択の位置pが何番目のカードか（左右に何周しても同じ並びが繰り返される）。order.Countならランダム選曲カード
    int SelectIndex(int p)
    {
        return Mod(p, order.Count + 1);
    }

    //楽曲選択で選んでいる曲。ランダム選曲カードならここで抽選する
    SongData SelectedSong()
    {
        int index = SelectIndex(position);
        if (index == order.Count) index = UnityEngine.Random.Range(0, order.Count);
        return songs[order[index]];
    }

    static int Mod(int a, int n)
    {
        return (a % n + n) % n;
    }
}
