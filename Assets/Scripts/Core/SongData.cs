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
    public int highScore;//ハイスコア。0なら未プレイ（選曲画面を開いたときにHighScoreStoreの記録で上書きする）
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
