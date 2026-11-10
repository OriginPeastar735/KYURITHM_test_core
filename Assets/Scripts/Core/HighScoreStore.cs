using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;
using UnityEngine;

//譜面ごとのハイスコアを持つstaticクラス。スコアの保存・読み出しは必ずここを通す
//
//  ハイスコアは筐体の記録としてファイル（highscores.json、Application.persistentDataPath の中）に保存し、ゲームを終了しても残る
//  ゲストの記録も筐体の記録として残り、次の人の選曲画面にも出る
//  keyは譜面の目印（DifficultyData.chartId = 曲名|作曲者|難易度）。譜面のファイル名やフォルダ名を変えても記録は引き継がれる
//
//  Webサイトとの連携（今後）: ログインしたプレイヤーの結果は、プレイを終えたときにまとめて送る
//  今は送る相手がまだ無いので、送る結果を pending_uploads.json にためておくところまで（QueueForUpload）。ゲストの結果はためない
//  画面に出すスコアはいつも手元の記録から読むので、通信を待ったり、通信の失敗で止まったりしない
public static class HighScoreStore
{
    //譜面1つ分の筐体の記録
    class Record
    {
        public string chartId;
        public int score;
        public string playerName;
        public string playedAt;
    }

    //1人が1回のプレイで遊んだ結果のまとまり（Webへ送る単位）
    class UploadBatch
    {
        public string playerId;
        public string playerName;
        public List<TrackResult> tracks;
    }

    static Dictionary<string, Record> records;//最初に使うときにファイルから読む

    //エディタとビルドは保存先が同じなので、エディタで試した記録が筐体の記録に混ざらないようファイルを分ける
    static string FilePath(string name)
    {
        return Path.Combine(Application.persistentDataPath, Application.isEditor ? name + "_editor.json" : name + ".json");
    }
    static string RecordsPath => FilePath("highscores");
    static string PendingPath => FilePath("pending_uploads");

    //エディタで「ドメインのリロード」を切っていても、再生を始めるたびにファイルから読み直す
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ClearCache()
    {
        records = null;
    }

    //記録が無ければ0（未プレイ）
    public static int Get(string chartId)
    {
        if (string.IsNullOrEmpty(chartId)) return 0;
        return Records.TryGetValue(chartId, out Record record) ? record.score : 0;
    }

    public static bool Has(string chartId)
    {
        return !string.IsNullOrEmpty(chartId) && Records.ContainsKey(chartId);
    }

    //今回の結果を登録する。ハイスコアを更新したらtrue（更新したときだけファイルに保存する）
    public static bool Submit(TrackResult result)
    {
        if (result == null || string.IsNullOrEmpty(result.chartId)) return false;
        bool isNewRecord = !Records.TryGetValue(result.chartId, out Record best) || result.score > best.score;
        if (!isNewRecord) return false;

        Records[result.chartId] = new Record
        {
            chartId = result.chartId,
            score = result.score,
            playerName = result.playerName,
            playedAt = result.playedAt,
        };
        Write(RecordsPath, new List<Record>(Records.Values));
        return true;
    }

    //プレイを終えたプレイヤーの結果を、Webへ送るものとしてファイルにためる（PlaySession.EndSessionから呼ぶ）
    //ゲスト（playerIdが空）や、1曲も遊び終えていないときは何もしない
    //送信ができるようになったら、ここにたまった分を送り、送れた分だけファイルから消す（通信に失敗した分は残って次に送られる）
    public static void QueueForUpload(string playerId, string playerName, List<TrackResult> tracks)
    {
        if (string.IsNullOrEmpty(playerId) || tracks == null || tracks.Count == 0) return;
        List<UploadBatch> pending = Read<List<UploadBatch>>(PendingPath) ?? new List<UploadBatch>();
        pending.Add(new UploadBatch { playerId = playerId, playerName = playerName, tracks = new List<TrackResult>(tracks) });
        Write(PendingPath, pending);
    }

    static Dictionary<string, Record> Records
    {
        get
        {
            if (records != null) return records;
            records = new Dictionary<string, Record>();
            List<Record> saved = Read<List<Record>>(RecordsPath);
            if (saved != null)
            {
                foreach (Record record in saved)
                {
                    if (record != null && !string.IsNullOrEmpty(record.chartId)) records[record.chartId] = record;
                }
            }
            return records;
        }
    }

    //ファイルが無い・読めないときはnull
    static T Read<T>(string path) where T : class
    {
        if (!File.Exists(path)) return null;
        try
        {
            return JsonConvert.DeserializeObject<T>(File.ReadAllText(path));
        }
        catch (Exception e)
        {
            Debug.LogWarning($"記録を読めませんでした: {path}\n{e.Message}");
            return null;
        }
    }

    //保存できなくても、今回の起動中はメモリ上の記録で動く
    static void Write(string path, object data)
    {
        try
        {
            File.WriteAllText(path, JsonConvert.SerializeObject(data, Formatting.Indented));
        }
        catch (Exception e)
        {
            Debug.LogWarning($"記録を保存できませんでした: {path}\n{e.Message}");
        }
    }
}
