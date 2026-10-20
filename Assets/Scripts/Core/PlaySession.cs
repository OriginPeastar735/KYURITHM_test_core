// シーン間で受け渡すデータを持つstaticクラス
public static class PlaySession
{
    // MusicSelect → Play
    public static int MusicIndex;
    public static int Difficulty;
    public static string ChartName;//空ならPlay側でデフォルト譜面を使う
}
