using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;
using UnityEngine;

//StreamingAssets/Songs から曲を読み込むstaticクラス
//
//  StreamingAssets/Songs/<曲フォルダ>/
//      譜面json（譜面エディタの出力そのまま。難易度ごとに1つ、ファイル名は自由）
//      音源ファイル（譜面のmeta.audioFileNameと同じ名前）
//      ジャケット画像（jacket.png など。無ければフォルダ内の最初の画像を使う）
//
//曲名・作曲者・難易度などは譜面のmetaから読むので、曲の情報を書いたファイルは別に作らない
//曲フォルダの中身はGoogle Driveで管理していて、gitには入れない（.gitignore）
public static class SongLibrary
{
    public static string SongsRoot => Path.Combine(Application.streamingAssetsPath, "Songs");

    static readonly string[] imageExtensions = { ".png", ".jpg", ".jpeg" };
    static readonly Dictionary<string, Sprite> jacketCache = new Dictionary<string, Sprite>();

    //譜面jsonのうち、曲リストを作るのに使う部分だけ読む
    class ChartFile
    {
        public ChartMeta meta;
    }

    class ChartMeta
    {
        public string title;
        public string artist;
        public string difficulty;
        public int level;
        public string chartDesigner;
        public string audioFileName;
    }

    //すべての曲フォルダを読み込む。曲が1つも無ければ空のリストを返す
    public static List<SongData> LoadAll()
    {
        var songs = new List<SongData>();
        if (!Directory.Exists(SongsRoot)) return songs;

        string[] folders = Directory.GetDirectories(SongsRoot);
        Array.Sort(folders, StringComparer.Ordinal);//PCによって順番が変わらないように
        foreach (string folder in folders)
        {
            SongData song = LoadSong(folder, songs.Count);
            if (song != null) songs.Add(song);
        }
        Debug.Log($"StreamingAssets/Songs から {songs.Count} 曲を読み込みました");
        return songs;
    }

    static SongData LoadSong(string folder, int id)
    {
        var charts = new DifficultyData[DifficultyInfo.Names.Length];//難易度の番号ごと
        ChartMeta songMeta = null;//曲名などに使うmeta（一番低い難易度の譜面のものを優先）
        int songMetaDifficulty = int.MaxValue;

        foreach (string path in Directory.GetFiles(folder, "*.json"))
        {
            ChartMeta meta;
            try
            {
                meta = JsonConvert.DeserializeObject<ChartFile>(File.ReadAllText(path))?.meta;
            }
            catch (Exception e)
            {
                Debug.LogWarning($"譜面を読めませんでした: {path}\n{e.Message}");
                continue;
            }
            if (meta == null)
            {
                Debug.LogWarning($"譜面にmetaがありません: {path}");
                continue;
            }

            int difficulty = Array.FindIndex(DifficultyInfo.Names, name => string.Equals(name, meta.difficulty, StringComparison.OrdinalIgnoreCase));
            if (difficulty < 0)
            {
                Debug.LogWarning($"難易度 \"{meta.difficulty}\" が分かりません: {path}");
                continue;
            }
            if (charts[difficulty] != null)
            {
                Debug.LogWarning($"{DifficultyInfo.Names[difficulty]} の譜面が2つあります。片方は使いません: {path}");
                continue;
            }

            charts[difficulty] = new DifficultyData
            {
                level = meta.level,
                chartName = ToChartName(path),
                designer = meta.chartDesigner,
            };
            if (difficulty < songMetaDifficulty)
            {
                songMeta = meta;
                songMetaDifficulty = difficulty;
            }
        }
        if (songMeta == null) return null;//譜面が無いフォルダ

        var song = new SongData
        {
            id = id,
            title = string.IsNullOrEmpty(songMeta.title) ? Path.GetFileName(folder) : songMeta.title,
            artist = songMeta.artist ?? "",
            jacketPath = FindJacket(folder),
            audioPath = FindAudio(folder, songMeta.audioFileName),
        };

        //選曲画面は PARALLEL → INTERPLAY → ELUSIVE と低い難易度から欠けずに並んでいる前提なので、途中が欠けたらそこまでにする
        for (int i = 0; i < charts.Length && charts[i] != null; i++) song.difficulties.Add(charts[i]);
        for (int i = song.difficulties.Count; i < charts.Length; i++)
        {
            if (charts[i] != null) Debug.LogWarning($"{song.title}: {DifficultyInfo.Names[i]} より低い難易度の譜面が無いので、{DifficultyInfo.Names[i]} は表示しません");
        }
        return song.difficulties.Count > 0 ? song : null;
    }

    //譜面の文字列を読む
    //chartNameが".json"で終わればStreamingAssetsからの相対パス、そうでなければResources内の名前（テスト用のデフォルト譜面）
    public static string ReadChart(string chartName)
    {
        if (chartName.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
        {
            return File.ReadAllText(Path.Combine(Application.streamingAssetsPath, chartName));
        }
        return Resources.Load<TextAsset>(chartName).text;
    }

    //ジャケット画像を読み込む（一度読んだものは使い回す）。読めなければnull
    public static Sprite LoadJacket(string path)
    {
        if (string.IsNullOrEmpty(path)) return null;
        if (jacketCache.TryGetValue(path, out Sprite cached)) return cached;

        Sprite sprite = null;
        try
        {
            var texture = new Texture2D(2, 2);
            if (texture.LoadImage(File.ReadAllBytes(path)))
            {
                sprite = Sprite.Create(texture, new Rect(0, 0, texture.width, texture.height), new Vector2(0.5f, 0.5f));
            }
        }
        catch (Exception e)
        {
            Debug.LogWarning($"ジャケットを読めませんでした: {path}\n{e.Message}");
        }
        jacketCache[path] = sprite;
        return sprite;
    }

    //StreamingAssetsからの相対パス（区切りは/）
    static string ToChartName(string path)
    {
        return Path.GetRelativePath(Application.streamingAssetsPath, path).Replace('\\', '/');
    }

    //jacket.png などを優先し、無ければフォルダ内の最初の画像
    static string FindJacket(string folder)
    {
        string firstImage = null;
        string[] files = Directory.GetFiles(folder);
        Array.Sort(files, StringComparer.Ordinal);
        foreach (string file in files)
        {
            if (Array.IndexOf(imageExtensions, Path.GetExtension(file).ToLowerInvariant()) < 0) continue;
            if (Path.GetFileNameWithoutExtension(file).Equals("jacket", StringComparison.OrdinalIgnoreCase)) return file;
            if (firstImage == null) firstImage = file;
        }
        return firstImage;
    }

    static string FindAudio(string folder, string audioFileName)
    {
        if (string.IsNullOrEmpty(audioFileName)) return null;
        string path = Path.Combine(folder, audioFileName);
        if (File.Exists(path)) return path;
        Debug.LogWarning($"音源ファイルがありません: {path}");
        return null;
    }
}
