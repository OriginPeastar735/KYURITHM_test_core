//スコアからランクとクリアかどうかを決める
//スコアは全PRECISEで100万点、全PRECISE+で101万点（GameManagerのCalcScore）
//
//  SSS+ 1,005,000  PRECISE+が半分以上（APPROXなし）
//  SSS  1,000,000  ALL PRECISEと同じ点
//  SS+    995,000  1000ノーツでAPPROX 10個まで（PRECISE+なしの場合。以下同じ）
//  SS     990,000  APPROX 20個まで
//  S+     980,000  APPROX 40個まで
//  S      970,000  APPROX 60個まで
//  AAA    940,000  APPROX 120個まで
//  AA     900,000  APPROX 200個まで
//  A      800,000  クリアライン
//  BBB    750,000  ここから下は未クリア
//  BB     700,000
//  B      600,000
//  C      500,000  全APPROXでもここ
//  D      それ未満
public static class ScoreRank
{
    public const int ClearScore = 800000;//クリアに必要なスコア（Aランク）

    //高いランクから順に並べる。スコアがBorders以上なら同じ番号のNames
    static readonly int[] Borders = { 1005000, 1000000, 995000, 990000, 980000, 970000, 940000, 900000, 800000, 750000, 700000, 600000, 500000 };
    static readonly string[] Names = { "SSS+", "SSS", "SS+", "SS", "S+", "S", "AAA", "AA", "A", "BBB", "BB", "B", "C" };
    const string LowestName = "D";

    public static string Get(int score)
    {
        for (int i = 0; i < Borders.Length; i++)
        {
            if (score >= Borders[i]) return Names[i];
        }
        return LowestName;
    }

    public static bool IsClear(int score)
    {
        return score >= ClearScore;
    }
}
