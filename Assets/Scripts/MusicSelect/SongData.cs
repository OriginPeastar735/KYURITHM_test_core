using System;
using System.Collections.Generic;
using UnityEngine;

//1曲分の情報。StreamingAssets/Songs の譜面jsonのmetaからSongLibraryが作る
[Serializable]
public class SongData
{
    public int id;
    public string title;
    public string artist;
    public float bpm;
    public string jacketPath;//ジャケット画像のファイルパス。空なら仮のジャケットを表示
    public string audioPath;//音源のファイルパス。空ならプレイ画面に設定してある曲を使う
    public List<DifficultyData> difficulties = new List<DifficultyData>();//基本2種類、一部の曲のみ3種類（リストの順番が難易度の番号）
}

[Serializable]
public class DifficultyData
{
    public int level;
    public string chartName;//譜面。StreamingAssetsからの相対パス（.json）か、Resources内の名前（仮の曲用）
    public string designer;//譜面制作者
    public int highScore;//プレイ状況.jsonができたらそこから読み込む。0なら未プレイ
}

//難易度の名前と色。難易度名を変えるときはここだけ直せばよい
public static class DifficultyInfo
{
    public static readonly string[] Names = { "PARALLEL", "INTERPLAY", "ELUSIVE" };
    public static readonly Color[] Colors =
    {
        new Color(0.25f, 0.65f, 0.90f),//PARALLEL 水色
        new Color(0.92f, 0.52f, 0.12f),//INTERPLAY オレンジ
        new Color(0.92f, 0.35f, 0.62f),//ELUSIVE ピンク
    };
}

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
            //譜面はまだ1つしかないので、すべてtest1013を仮で割り当てる
            song.difficulties.Add(new DifficultyData { level = level, chartName = "test1013", designer = designer, highScore = highScore });
        }
        songs.Add(song);
    }
}
