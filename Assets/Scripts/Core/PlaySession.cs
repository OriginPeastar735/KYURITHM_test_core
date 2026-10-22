// シーン間で受け渡すデータを持つstaticクラス
public static class PlaySession
{
    // プレイヤー情報（ログイン機能ができるまではゲスト扱い）
    public static string PlayerName = "ゲスト";
    public static int TrackNumber = 1;//何曲目か

    // MusicSelect → Play
    public static int MusicIndex;
    public static int Difficulty;
    public static string ChartName;//空ならPlay側でデフォルト譜面を使う
}
