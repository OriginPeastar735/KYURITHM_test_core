using System.Collections.Generic;

//譜面ごとのハイスコアを持つstaticクラス
//今はメモリ上にだけ持つので、ゲームを終了する（Unityの再生を止める）とリセットされる
//keyは譜面名（PlaySession.ChartName / DifficultyData.chartName）。曲と難易度の組み合わせごとに1つ
public static class HighScoreStore
{
    static readonly Dictionary<string, int> scores = new Dictionary<string, int>();

    //記録が無ければ0（未プレイ）
    public static int Get(string chartName)
    {
        if (string.IsNullOrEmpty(chartName)) return 0;
        return scores.TryGetValue(chartName, out int score) ? score : 0;
    }

    public static bool Has(string chartName)
    {
        return !string.IsNullOrEmpty(chartName) && scores.ContainsKey(chartName);
    }

    //今回のスコアを登録する。ハイスコアを更新したらtrue
    public static bool Submit(string chartName, int score)
    {
        if (string.IsNullOrEmpty(chartName)) return false;
        bool isNewRecord = !scores.TryGetValue(chartName, out int best) || score > best;
        if (isNewRecord) scores[chartName] = score;
        return isNewRecord;
    }
}
