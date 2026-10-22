using System;
using System.Collections.Generic;
using UnityEngine;

//1曲分の情報。将来は楽曲メタデータ.jsonから読み込む想定（フィールド名はjsonのキーに合わせて変えてよい）
[Serializable]
public class SongData
{
    public int id;
    public string title;
    public string artist;
    public float bpm;
    public string jacketPath;//Resources内のジャケット画像のパス。空なら仮のジャケットを表示
    public List<DifficultyData> difficulties = new List<DifficultyData>();//基本2種類、一部の曲のみ3種類
}

[Serializable]
public class DifficultyData
{
    public string name;
    public int level;
    public string chartName;//Resources内の譜面jsonの名前
}

//楽曲メタデータができるまでの仮の曲リスト
public static class PlaceholderSongs
{
    public static readonly string[] DifficultyNames = { "NORMAL", "HARD", "EXTRA" };

    public static List<SongData> Create()
    {
        var songs = new List<SongData>();
        Add(songs, "Shining Star", "仮アーティストA", 150, 3, 8, 11);
        Add(songs, "Iso Rate", "仮アーティストB", 174, 4, 9);
        Add(songs, "Curve Line", "仮アーティストC", 128, 2, 7);
        Add(songs, "Tap Hold Slide", "仮アーティストD", 140, 5, 10, 13);
        Add(songs, "サンプル楽曲その5", "仮アーティストE", 160, 3, 9);
        Add(songs, "サンプル楽曲その6", "仮アーティストF", 182, 6, 12);
        Add(songs, "Test 1013", "仮アーティストG", 120, 1, 6);
        Add(songs, "とても長い曲名のサンプル楽曲 ～表示確認用～", "仮アーティストH", 200, 7, 12, 14);
        return songs;
    }

    static void Add(List<SongData> songs, string title, string artist, float bpm, params int[] levels)
    {
        var song = new SongData { id = songs.Count, title = title, artist = artist, bpm = bpm };
        for (int i = 0; i < levels.Length; i++)
        {
            //譜面はまだ1つしかないので、すべてtest1013を仮で割り当てる
            song.difficulties.Add(new DifficultyData { name = DifficultyNames[i], level = levels[i], chartName = "test1013" });
        }
        songs.Add(song);
    }
}
