// シーン間で受け渡すデータを持つstaticクラス
public static class PlaySession
{
    // プレイヤー情報（ログイン機能はまだ無いので、名前はゲスト固定・IDは空）
    public const string GuestName = "ゲスト";
    public static string PlayerName = GuestName;
    public static string PlayerId = "";//Webサイトのアカウント。空ならゲスト（結果をWebへ送らない）

    //今のプレイヤーが遊び終えた曲の結果すべて（コンティニューした分も含む）。プレイを終えるときにまとめてWebへ送る
    public static readonly System.Collections.Generic.List<TrackResult> SessionResults = new System.Collections.Generic.List<TrackResult>();

    //今のプレイヤーのプレイを終える（終了を選んだ・時間切れ・運営の全スキップ）。次のプレイヤー向けにすべて戻す
    //「次の人に持ち越さない」ものはここに集める。持ち越すものを増やさないよう、戻す処理をほかの場所に書かない
    public static void EndSession()
    {
        HighScoreStore.QueueForUpload(PlayerId, PlayerName, SessionResults);//ゲストなら何もしない
        SessionResults.Clear();
        System.Array.Clear(TrackResults, 0, TrackResults.Length);
        TrackNumber = 1;
        HasLastSelection = false;
        IsTutorial = false;
        SettingsBeforeTutorial = null;
        WaitingForIntro = false;
        ResetSettingsForNextPlayer();
        PlayerName = GuestName;
        PlayerId = "";
    }
    public static int TrackNumber = 1;//何曲目か
    public const int MaxTracks = 3;//1回のプレイで遊べる曲数。この曲数を遊び終えるとResultシーン・エンディングを経てオープニングに戻る
    public static bool IsFinalTrack => TrackNumber >= MaxTracks;

    //1曲遊び終えたときに呼ぶ。まだ続けて遊べるならtrue、規定の曲数を遊び終えたら曲数を戻してfalse
    public static bool NextTrack()
    {
        if (IsFinalTrack)
        {
            TrackNumber = 1;
            HasLastSelection = false;//次のプレイヤーは選曲画面の初期状態から始める
            return false;
        }
        TrackNumber++;
        return true;
    }

    // Opening → Play
    public static bool IsTutorial;//チュートリアル中。Playシーンで譜面と曲の代わりにTutorialが練習用のノーツを流す。終わったら選曲画面へ
    public static PlayPreset SettingsBeforeTutorial;//チュートリアルを始める前の設定。チュートリアル中は初心者向けの設定にし、終わったらこれに戻す

    //1回のプレイで遊んだ曲ごとの結果（[TrackNumber-1]）。最終リザルトに出す
    public static readonly TrackResult[] TrackResults = new TrackResult[MaxTracks];

    //Playシーンでリザルトを出すときに呼ぶ。1曲目の結果を入れるときは、前のプレイヤーの結果を消してから入れる
    public static void RecordTrack(TrackResult result)
    {
        if (TrackNumber < 1 || TrackNumber > MaxTracks) return;
        if (TrackNumber == 1) System.Array.Clear(TrackResults, 0, TrackResults.Length);
        TrackResults[TrackNumber - 1] = result;
        SessionResults.Add(result);
    }

    // MusicSelect → Play
    public static int MusicIndex;
    public static int Difficulty;
    public static string ChartName;//空ならPlay側でデフォルト譜面を使う（読み方はSongLibrary.ReadChart）
    public static string ChartId;//譜面の目印（DifficultyData.chartId）。スコアの記録に使う
    public static string AudioPath;//音源のファイルパス。空ならPlayシーンに設定してある曲を使う
    public static string SongTitle;//選曲画面で選んだ曲の曲名（リザルトに出す）。空なら譜面jsonのmeta.titleを使う
    public static string JacketPath;//選曲画面で選んだ曲のジャケット画像（最終リザルトに出す）
    public static int SongLevel;//選曲画面で選んだ譜面のレベル（最終リザルトに出す）
    public static string SongArtist;//選曲画面で選んだ曲のアーティスト名。空なら譜面jsonのmeta.artistを使う
    public static bool WaitingForIntro;//楽曲紹介の表示中。trueの間はプレイ画面の曲を再生しない
    public static int SortMode;//選曲画面のソート（MusicSelectManager.SortModeの番号）
    public static bool HasLastSelection;//前の曲を選んだ状態（MusicIndex・Difficulty・SortMode）が残っているか。次の曲の選曲画面はその状態から始める

    // プレイ設定（選曲画面の設定カードで変更し、プレイ画面のNoteManager・MusicManager・GameManagerで使う）
    //プリセットに含まれる設定の初期値は、おすすめプリセットの初心者向け（静的コンストラクタで入れる）
    static PlaySession()
    {
        PlayPreset.Beginner.Apply();
    }

    //プレイ設定を次のプレイヤー向けに全部戻す（EndSessionから呼ぶ）
    //プリセットに含まれない設定（判定タイミング・小節線）もここで戻す。前の人の調整が次の人に残らないように
    public static void ResetSettingsForNextPlayer()
    {
        PlayPreset.Beginner.Apply();
        JudgeOffsetMs = 0;
        ShowBarLines = true;
    }

    public static float HiSpeed;//ノーツの速さ
    //タイミングの調整(ms)。筐体ごとの調整（DisplayFit.Current）に足して使う
    public static int SongOffsetMs;//楽曲タイミング。+にすると曲に対してノーツの到着と判定が遅れる（音が遅れて聞こえる環境向け）
    public static int JudgeOffsetMs = 0;//判定タイミング。+にするとノーツの見た目はそのままで判定だけ遅れる（遅れて押しがちな人向け）
    public static int WallPercent;//壁の位置。レーンの奥側から何%を見えなくするか（0%で全部見える、100%で何も見えない）
    public static int LaneOpacityPercent;//レーンの床の濃さ（0%で透明、100%で背景が見えない）
    public static int BackgroundBrightnessPercent;//プレイ中の背景の明るさ（50%〜100%）
    public static int LineOpacityPercent;//レーンの区切り線の濃さ（0%で見えない）
    public static int LineDivision;//区切り線を何レーンごとに引くか（0なら引かない。底面と斜面の境目・斜面の端は常に引く）
    public static int KeyBeamPercent;//押したときに光るキービームの長さ（判定ラインから壁までの長さに対する%。100%で壁まで届く）
    public static int NoteThicknessPercent;//ノーツの太さ（奥行き）。50%がプレハブのままの太さ
    public static bool ShowFastLate;//FAST/LATEを表示するか
    public static bool ShowBarLines = true;//1小節ごとに小節線を流すか（通常はON）
    public static bool TapSound;//タップ音（ノーツを叩いた音・空打ちの音）を鳴らすか
}
