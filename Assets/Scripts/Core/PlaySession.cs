// シーン間で受け渡すデータを持つstaticクラス
public static class PlaySession
{
    // プレイヤー情報（ログイン機能は無いので、名前はゲスト固定）
    public static string PlayerName = "ゲスト";
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

    // MusicSelect → Play
    public static int MusicIndex;
    public static int Difficulty;
    public static string ChartName;//空ならPlay側でデフォルト譜面を使う（読み方はSongLibrary.ReadChart）
    public static string AudioPath;//音源のファイルパス。空ならPlayシーンに設定してある曲を使う
    public static string SongTitle;//選曲画面で選んだ曲の曲名（リザルトに出す）。空なら譜面jsonのmeta.titleを使う
    public static string SongArtist;//選曲画面で選んだ曲のアーティスト名。空なら譜面jsonのmeta.artistを使う
    public static bool WaitingForIntro;//楽曲紹介の表示中。trueの間はプレイ画面の曲を再生しない
    public static int SortMode;//選曲画面のソート（MusicSelectManager.SortModeの番号）
    public static bool HasLastSelection;//前の曲を選んだ状態（MusicIndex・Difficulty・SortMode）が残っているか。次の曲の選曲画面はその状態から始める

    // プレイ設定（選曲画面の設定カードで変更し、プレイ画面のNoteManager・MusicManager・GameManagerで使う）
    public static float HiSpeed = 5f;//ノーツの速さ
    public static int TimingOffsetMs = 0;//判定タイミングの調整(ms)。+にするとノーツの到着と判定が遅れる
    public static int WallPercent = 0;//壁の位置。レーンの奥側から何%を見えなくするか（0%で全部見える、100%で何も見えない）
    public static int LaneOpacityPercent = 80;//レーンの床の濃さ（0%で透明、100%で背景が見えない）
    public static int BackgroundBrightnessPercent = 80;//プレイ中の背景の明るさ（50%〜100%）
    public static int LineOpacityPercent = 30;//レーンの区切り線の濃さ（0%で見えない）
    public static int LineDivision = 4;//区切り線を何レーンごとに引くか（0なら引かない。底面と斜面の境目・斜面の端は常に引く）
    public static int KeyBeamPercent = 100;//押したときに光るキービームの長さ（判定ラインから壁までの長さに対する%。100%で壁まで届く）
    public static int NoteThicknessPercent = 50;//ノーツの太さ（奥行き）。50%がプレハブのままの太さ
    public static bool ShowFastLate = false;//FAST/LATEを表示するか（通常はOFF）
}
