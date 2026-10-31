using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public enum JudgeResult
{
    PrecisePlus, Precise, Approx, Miss
}

//InputManagerのレーン入力とノーツのlane/widthを照らし合わせて判定する
public class JudgeManager : MonoBehaviour
{
    public static JudgeManager instance;

    //判定の種類は4種類（PRECISE+ / PRECISE / APPROX / MISS）
    public static event Action PrecisePlus;
    public static event Action Precise;
    public static event Action Approx;
    public static event Action Miss;
    //タイミングで判定するノーツ（tap・slide始点）がPRECISE/APPROXだったときに、ずれ(秒)を通知する。負なら早い(FAST)
    public static event Action<float> FastLate;
    //判定が出たときに、判定の種類とノーツの中心レーン位置を通知する（ノーツの上に判定を表示するのに使う）
    public static event Action<JudgeResult, float> Judged;

    //ノーツの判定猶予をここで設定（だれでも楽しめるように広めにしている）
    [Header("判定幅(ms)")]
    public float precisePlusMs = 40f;
    public float preciseMs = 70f;
    public float approxMs = 110f;
    public float missMs = 130f;//approxMsより外でもこの範囲内で押すとMISS（早押し・遅押しでノーツを消費する）

    //スライドノーツの判定猶予設定
    [Header("slide")]
    public float slideReleaseGrace = 0.1f;//判定点の前後で指が離れていても許容する秒数（キーの持ち替え対策）
    public int slideLaneMargin = 0;//slideの範囲を左右に何レーン広げて判定するか（判定を甘くしたい場合に使う）


    private float currentPlayTime;//このフレームの判定に使う時刻（秒）。曲の再生時間から判定タイミングの調整を引いたもの
    //入力を受け付ける一番外側の範囲（秒）。これを過ぎたノーツは見逃しMISSになる
    private float MissWindow => missMs / 1000f;
    //判定タイミングの調整(秒)。筐体の調整＋選曲画面の設定。押した時刻と判定する時刻をこれだけ早めて扱うので、+にすると遅めに押してちょうどになる
    private static float JudgeOffset => (DisplayFit.Current.judgeOffsetMs + PlaySession.JudgeOffsetMs) / 1000f;

    //このフレームで既に判定に使った入力。1回の入力で複数ノーツを取らないようにする
    private readonly bool[] consumed = new bool[InputManager.LaneCount];
    //このフレームで押されたレーンを押した時刻（曲の再生時間）。tap・slide始点はフレームの時刻ではなくこの時刻で判定する
    private readonly float[] pressTime = new float[InputManager.LaneCount];
    //このフレームで押された入力のうち、一番遅い押した時刻。これより判定幅以上先のノーツは判定対象にならない
    private float latestPressTime;

    void Awake()
    {
        if (instance == null)
        {
            instance = this;
        }
    }

    void Update()
    {
        if (MusicManager.instance == null || NoteManager.instance == null || InputManager.instance == null) return;

        float judgeOffset = JudgeOffset;
        currentPlayTime = MusicManager.instance.CurrentPlayTime - judgeOffset;

        //入力を未使用に戻し、このフレームで押されたレーンの押した時刻を曲の再生時間に直す
        Array.Clear(consumed, 0, consumed.Length);
        bool anyDown = false;
        latestPressTime = float.NegativeInfinity;
        for (int i = 0; i < InputManager.LaneCount; i++)
        {
            if (!InputManager.instance.laneDown[i]) continue;
            pressTime[i] = MusicManager.instance.ToPlayTime(InputManager.instance.laneDownTimestamp[i]) - judgeOffset;
            latestPressTime = Mathf.Max(latestPressTime, pressTime[i]);
            anyDown = true;
        }

        if (anyDown) JudgeTaps();
        JudgeHolds();
        JudgeSlides();
        CheckMiss();
    }

    // ===== tap と slide始点 =====
    //押された入力に対して、判定幅内で一番早いノーツから順に判定する
    //ずれは「押した時刻 - ノーツの時刻」で求めるので、フレームレートに関係なく押したタイミングどおりに判定される
    //ノーツは時刻順に並んでいるので、どの入力の判定幅にも入らない時刻まで来たら探すのをやめる
    void JudgeTaps()
    {
        float searchEnd = latestPressTime + MissWindow;
        while (true)
        {
            Note bestNote = null;
            LongNote bestSlide = null;
            float bestTime = float.MaxValue;
            float bestPressTime = 0f;//候補のノーツを押した時刻

            //判定幅内に押した入力があるノーツのうち、一番早いものを探す
            foreach (var note in NoteManager.instance.notes)
            {
                if (note == null) continue;
                //時刻順なので、候補より遅いノーツ・判定幅より先のノーツが来たら以降はすべて対象外
                if (note.expectedTime >= bestTime || note.expectedTime > searchEnd) break;
                //ノーツの範囲に、まだ使われていない「押した瞬間」の入力が判定幅(±MISS)内の時刻になければ飛ばす
                if (!TryFreeDown(note.lane, note.width, note.expectedTime, out float press)) continue;
                bestNote = note;
                bestSlide = null;//候補は常に1つだけにする
                bestTime = note.expectedTime;//次のノーツと比べるために時刻を記録する
                bestPressTime = press;
            }

            //slideの始点も同じbestTimeと比べるので、tapとslide始点を合わせた中で一番早いものが選ばれる
            foreach (var ln in NoteManager.instance.LongNotes)
            {
                if (ln == null) continue;
                if (ln.startExpectedTime >= bestTime || ln.startExpectedTime > searchEnd) break;
                if (!ln.isFirstSection || ln.state != LongNoteState.None) continue;
                if (!TryFreeDown(ln.startLane, ln.startWidth, ln.startExpectedTime, out float press)) continue;
                bestNote = null;
                bestSlide = ln;
                bestTime = ln.startExpectedTime;
                bestPressTime = press;
            }

            if (bestNote != null)
            {
                float diff = bestPressTime - bestNote.expectedTime;
                Consume(bestNote.lane, bestNote.width);
                ShowTimingResult(GradeByTiming(diff), bestNote.centerLane, diff);
                bestNote.Delete();
            }
            else if (bestSlide != null)
            {
                float diff = bestPressTime - bestSlide.startExpectedTime;
                float centerLane = bestSlide.startLane + bestSlide.startWidth / 2f;
                Consume(bestSlide.startLane, bestSlide.startWidth);
                ShowTimingResult(GradeByTiming(diff), centerLane, diff);
                bestSlide.Activate();
            }
            else
            {
                break;//判定できるノーツがもうない
            }
        }
    }

    // ===== hold =====
    //判定ラインを通過する時にノーツの範囲が押されていればPRECISE+
    //判定したholdはDeleteでリストから消えるので、そのときは添字を進めない
    void JudgeHolds()
    {
        List<Hold> holds = NoteManager.instance.holds;
        for (int i = 0; i < holds.Count;)
        {
            Hold hold = holds[i];
            if (hold == null) { i++; continue; }
            float diff = currentPlayTime - hold.expectedTime;
            if (diff < 0) break;//時刻順なので、これ以降のholdもまだ判定ラインに来ていない

            if (InputManager.instance.AnyHeld(hold.lane, hold.width))
            {
                ShowResult(JudgeResult.PrecisePlus, hold.centerLane, diff);
                hold.Delete();
            }
            else if (diff > MissWindow)
            {
                ShowResult(JudgeResult.Miss, hold.centerLane, diff);
                hold.Delete();
            }
            else
            {
                i++;
            }
        }
    }

    // ===== slide（判定点） =====
    //チュウニズム方式：一定間隔の判定点ごとに「その時点でslideの範囲に触れているか」を判定する
    //離したタイミングは判定せず、途中で離しても再び触れれば以降の判定点は取れる
    //判定し終えたセクションはFinishでリストから消えるので、後ろから回す
    void JudgeSlides()
    {
        List<LongNote> longNotes = NoteManager.instance.LongNotes;
        for (int i = longNotes.Count - 1; i >= 0; i--)
        {
            LongNote ln = longNotes[i];
            if (ln == null) continue;

            if (ln.state == LongNoteState.None)
            {
                //2つ目以降のセクションは始点の時刻になったら判定を始める（始点の判定はない）
                if (!ln.isFirstSection && currentPlayTime >= ln.startExpectedTime) ln.Activate();
                else continue;
            }

            bool held = IsHoldingSlide(ln);
            if (held) ln.lastHeldTime = currentPlayTime;
            UpdateHoldEffect(ln, held);
            JudgeCheckpoints(ln);
        }
    }

    void JudgeCheckpoints(LongNote ln)
    {
        while (ln.nextCheckpoint < ln.checkpointTimes.Count)
        {
            float checkpoint = ln.checkpointTimes[ln.nextCheckpoint];
            if (currentPlayTime < checkpoint) break;//まだ判定点に来ていない

            float centerLane = ln.GetCenterLaneAt(checkpoint);
            if (checkpoint - ln.lastHeldTime <= slideReleaseGrace)
            {
                //判定点の直前(猶予内)から今までに触れていた
                ShowResult(JudgeResult.PrecisePlus, centerLane, 0);
            }
            else if (currentPlayTime - checkpoint > slideReleaseGrace)
            {
                ShowResult(JudgeResult.Miss, centerLane, currentPlayTime - checkpoint);
            }
            else
            {
                break;//猶予時間内に触れるのを待つ
            }
            ln.nextCheckpoint++;
        }

        //分岐の終点が親の中間ノーツにつながる場合は最後に判定点がないので、終点の時刻まで待ってから消す
        if (ln.nextCheckpoint >= ln.checkpointTimes.Count && currentPlayTime >= ln.endExpectedTime) ln.Finish();
    }

    //触れている間だけエフェクトを出す（位置はLongNote.UpdatePositionで追従）
    void UpdateHoldEffect(LongNote ln, bool held)
    {
        if (held && ln.holdEffect == null)
        {
            ln.holdEffect = EffectManager.instance.HoldEffect(ln.GetCenterLaneAt(currentPlayTime));
        }
        else if (!held && ln.holdEffect != null)
        {
            Destroy(ln.holdEffect);
            ln.holdEffect = null;
        }
    }

    //判定ライン上のslideの範囲（の一部でも）が押されているか
    bool IsHoldingSlide(LongNote ln)
    {
        ln.GetLaneRangeAt(currentPlayTime, out float lane, out float width);
        int from = Mathf.FloorToInt(lane) - slideLaneMargin;
        int to = Mathf.CeilToInt(lane + width) + slideLaneMargin;
        return InputManager.instance.AnyHeld(from, to - from);
    }

    // ===== 見逃し =====
    //ノーツは時刻順なので、まだ判定幅を過ぎていないノーツが来たらそこで終わる
    void CheckMiss()
    {
        List<Note> notes = NoteManager.instance.notes;
        for (int i = 0; i < notes.Count;)
        {
            Note note = notes[i];
            if (note == null) { i++; continue; }
            float diff = currentPlayTime - note.expectedTime;
            if (diff <= MissWindow) break;
            ShowResult(JudgeResult.Miss, note.centerLane, diff);
            note.Delete();//リストから消えるので添字は進めない
        }

        foreach (var ln in NoteManager.instance.LongNotes)
        {
            if (ln == null) continue;
            float diff = currentPlayTime - ln.startExpectedTime;
            if (diff <= MissWindow) break;
            if (!ln.isFirstSection || ln.state != LongNoteState.None) continue;

            //始点を逃してもslideは続き、以降の判定点は触れれば取れる（Activateはリストを変えないので、そのまま回してよい）
            float centerLane = ln.startLane + ln.startWidth / 2f;
            ShowResult(JudgeResult.Miss, centerLane, diff);
            ln.Activate();
        }
    }

    //判定ごとのログ。ビルドでは呼び出しごと消える（ログ出力でフレームが重くならないように）
    [System.Diagnostics.Conditional("UNITY_EDITOR")]
    static void LogJudge(JudgeResult result, float diffSec)
    {
        Debug.Log($"{result}: {diffSec * 1000f:F1}ms");
    }

    // ===== 共通処理 =====
    JudgeResult GradeByTiming(float diffSec)
    {
        float ms = Mathf.Abs(diffSec * 1000f);
        if (ms <= precisePlusMs) return JudgeResult.PrecisePlus;
        if (ms <= preciseMs) return JudgeResult.Precise;
        if (ms <= approxMs) return JudgeResult.Approx;
        return JudgeResult.Miss;//approxMs〜missMsの範囲で押した場合
    }

    //押したタイミングで判定した結果を出す。PRECISE+でもMISSでもないときはFAST/LATEも通知する
    void ShowTimingResult(JudgeResult result, float centerLane, float diffSec)
    {
        ShowResult(result, centerLane, diffSec);
        if (result == JudgeResult.Precise || result == JudgeResult.Approx) FastLate?.Invoke(diffSec);
    }

    //判定結果に応じてエフェクトとイベントを発行する（centerLaneはエフェクトを出すレーン位置）
    void ShowResult(JudgeResult result, float centerLane, float diffSec)
    {
        LogJudge(result, diffSec);
        Judged?.Invoke(result, centerLane);
        switch (result)
        {
            case JudgeResult.PrecisePlus:
                EffectManager.instance.PrecisePlusEffect(centerLane);
                PrecisePlus?.Invoke();
                break;
            case JudgeResult.Precise:
                EffectManager.instance.PreciseEffect(centerLane);
                Precise?.Invoke();
                break;
            case JudgeResult.Approx:
                EffectManager.instance.ApproxEffect(centerLane);
                Approx?.Invoke();
                break;
            case JudgeResult.Miss:
                Miss?.Invoke();
                break;
        }
    }

    //範囲内に、まだ使われていない「押した瞬間」の入力が、expectedTimeの判定幅(±MISS)内の時刻にあるか
    //複数あればexpectedTimeに一番近い時刻をpressに返す
    bool TryFreeDown(int lane, int width, float expectedTime, out float press)
    {
        press = 0f;
        float bestDiff = float.MaxValue;
        int start = Mathf.Max(0, lane);
        int end = Mathf.Min(InputManager.LaneCount, lane + width);
        for (int i = start; i < end; i++)
        {
            if (!InputManager.instance.laneDown[i] || consumed[i]) continue;
            float diff = Mathf.Abs(pressTime[i] - expectedTime);
            if (diff > MissWindow || diff >= bestDiff) continue;
            bestDiff = diff;
            press = pressTime[i];
        }
        return bestDiff <= MissWindow;
    }

    //範囲内の「押した瞬間」の入力を使用済みにする
    void Consume(int lane, int width)
    {
        int start = Mathf.Max(0, lane);
        int end = Mathf.Min(InputManager.LaneCount, lane + width);
        for (int i = start; i < end; i++)
        {
            if (InputManager.instance.laneDown[i]) consumed[i] = true;
        }
    }
}
