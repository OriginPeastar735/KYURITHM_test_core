// シーン間で受け渡すデータを持つstaticクラス
public static class PlaySession
{
    // プレイヤー情報（ログイン機能ができるまではゲスト扱い）
    public static string PlayerName = "ゲスト";
    public static int TrackNumber = 1;//何曲目か
    public const int MaxTracks = 3;//1回のプレイで遊べる曲数。この曲数を遊び終えるとオープニングに戻る
    public static bool IsFinalTrack => TrackNumber >= MaxTracks;

    //1曲遊び終えたときに呼ぶ。まだ続けて遊べるならtrue、規定の曲数を遊び終えたら曲数を戻してfalse
    public static bool NextTrack()
    {
        if (IsFinalTrack)
        {
            TrackNumber = 1;
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
    public static bool WaitingForIntro;//楽曲紹介の表示中。trueの間はプレイ画面の曲を再生しない

    // プレイ設定（選曲画面の設定カードで変更し、プレイ画面のNoteManager・MusicManager・GameManagerで使う）
    public static float HiSpeed = 5f;//ノーツの速さ
    public static int TimingOffsetMs = 0;//判定タイミングの調整(ms)。+にするとノーツの到着と判定が遅れる
    public static bool ShowFastLate = true;//FAST/LATEを表示するか
}
