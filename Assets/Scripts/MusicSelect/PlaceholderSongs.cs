using System.Collections.Generic;

//StreamingAssets/Songs に曲が1つも無いときに表示する仮の曲リスト
public static class PlaceholderSongs
{
    public static List<SongData> Create()
    {
        var songs = new List<SongData>();
        //レベルを2つ渡すとPARALLEL・INTERPLAYのみ、3つ渡すとELUSIVEもある曲になる
        Add(songs, "Aurora Signal", "Lumina Works", 150, 2, 8);
        Add(songs, "Binary Garden", "nanoleaf", 174, 3, 9, 13);
        Add(songs, "Crimson Orbit", "K-Shift", 168, 5, 10);
        Add(songs, "Daybreak Runner", "Sora Tsukimi", 132, 1, 6);
        Add(songs, "Echo Lattice", "Prism Unit", 145, 4, 9);
        Add(songs, "Frozen Parade", "Hazel & Co.", 160, 3, 8, 12);
        Add(songs, "Glass Horizon", "Lumina Works", 180, 6, 11);
        Add(songs, "星屑ステップ", "ミナト", 128, 2, 7);
        Add(songs, "夜明けのパルス", "七瀬リオ", 190, 4, 10, 14);
        Add(songs, "Iron Cascade", "Delta Drive", 200, 7, 12, 15);
        Add(songs, "Jade Pulse", "Prism Unit", 140, 3, 8);
        Add(songs, "Kaleidoscope", "Sora Tsukimi", 155, 5, 11);
        Add(songs, "Lunar Waltz", "Hazel & Co.", 120, 1, 7);
        Add(songs, "Mirage Engine", "K-Shift", 185, 6, 12, 14);
        Add(songs, "桜色シグナル", "ミナト", 136, 2, 9);
        Add(songs, "Neon Cathedral", "Delta Drive", 175, 5, 10, 13);
        Add(songs, "Overdrive Heart", "nanoleaf", 165, 4, 11);
        Add(songs, "流星群ランデブー ～とても長い曲名の表示確認用～", "七瀬リオ", 158, 3, 9, 12);
        Add(songs, "Quartz Memory", "Lumina Works", 148, 6, 10);
        Add(songs, "Rainfall Theory", "Prism Unit", 210, 2, 8, 15);
        return songs;
    }

    static readonly string[] designers = { "仮譜面師A", "仮譜面師B", "KYU-Charts", "Placeholder Team" };

    static void Add(List<SongData> songs, string title, string artist, float bpm, params int[] levels)
    {
        var song = new SongData { id = songs.Count, title = title, artist = artist, bpm = bpm };
        //ハイスコアも仮の値。曲ごとに決まった乱数で、3割ほどは未プレイ(0)にする
        var random = new System.Random(song.id * 7919 + 17);
        foreach (int level in levels)
        {
            int highScore = random.NextDouble() < 0.3 ? 0 : random.Next(700000, 1010001);
            string designer = designers[random.Next(designers.Length)];
            //仮の曲はすべて確認用の譜面test_long（約100秒、全種類のノーツ入り）にする
            song.difficulties.Add(new DifficultyData { level = level, chartName = "test_long", designer = designer, highScore = highScore });
        }
        songs.Add(song);
    }
}
