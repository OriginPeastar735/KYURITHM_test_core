//1曲分の結果。Playシーンのリザルトを出すときにGameManagerが作り、最終リザルト（Resultシーン）で並べて出す
public class TrackResult
{
    public string title;
    public int difficulty;//難易度の番号（DifficultyInfoの並び）
    public int level;
    public string jacketPath;//空なら仮のジャケットを出す
    public int score;
    public int precisePlus, precise, approx, miss;
    public int fast, late;//PRECISE・APPROXのうち早かった数・遅かった数（PRECISE+は数えない）

    public bool IsAllPrecise => miss == 0 && approx == 0;
    public bool IsFullCombo => miss == 0;
}
