//1曲分の結果。Playシーンのリザルトを出すときにGameManagerが作り、最終リザルト（Resultシーン）で並べて出す
//ハイスコアの記録（HighScoreStore.Submit）と、Webへ送る結果もこの形で渡す
public class TrackResult
{
    public string chartId;//譜面の目印（DifficultyData.chartId）
    public string title;
    public int difficulty;//難易度の番号（DifficultyInfoの並び）
    public int level;
    public string jacketPath;//空なら仮のジャケットを出す
    public int score;
    public int precisePlus, precise, approx, miss;
    public int fast, late;//PRECISE・APPROXのうち早かった数・遅かった数（PRECISE+は数えない）
    public int maxCombo;
    public int totalNotes;//譜面の総ノーツ数
    public string playerName;
    public string playedAt;//遊び終えた日時（ISO 8601、その場所の時刻）

    [Newtonsoft.Json.JsonIgnore] public bool IsAllPrecise => miss == 0 && approx == 0;
    [Newtonsoft.Json.JsonIgnore] public bool IsFullCombo => miss == 0;
}
