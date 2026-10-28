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
    public float precisePlusMs = 30f;
    public float preciseMs = 60f;
    public float approxMs = 100f;
    public float missMs = 120f;//approxMsより外でもこの範囲内で押すとMISS（早押し・遅押しでノーツを消費する）

    //スライドノーツの判定猶予設定
    [Header("slide")]
    public float slideReleaseGrace = 0.1f;//判定点の前後で指が離れていても許容する秒数（キーの持ち替え対策）
    public int slideLaneMargin = 0;//slideの範囲を左右に何レーン広げて判定するか（判定を甘くしたい場合に使う）


    private float currentPlayTime;//現在の音楽再生時間を記録する変数
    //missMs(ミリ秒)を秒に直した値を返すプロパティ（120ms → 0.12秒）。
    //currentPlayTimeやexpectedTimeは秒なので、比べるために単位をそろえている
    //入力を受け付ける一番外側の範囲で、これを過ぎたノーツは見逃しMISSになる
    private float MissWindow => missMs / 1000f;

    //このフレームで既に判定に使った入力。1回の入力で複数ノーツを取らないようにする
    private readonly bool[] consumed = new bool[InputManager.LaneCount];
    //このフレームで押されたレーンを押した時刻（曲の再生時間）。tap・slide始点はフレームの時刻ではなくこの時刻で判定する
    private readonly float[] pressTime = new float[InputManager.LaneCount];

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

        currentPlayTime = MusicManager.instance.CurrentPlayTime;

        //Array.Clear(配列, 開始, 個数): 配列を初期値（false）に戻す
        Array.Clear(consumed, 0, consumed.Length);
        for (int i = 0; i < InputManager.LaneCount; i++)
        {
            if (InputManager.instance.laneDown[i]) pressTime[i] = MusicManager.instance.ToPlayTime(InputManager.instance.laneDownTimestamp[i]);
        }

        JudgeTaps();
        JudgeHolds();
        JudgeSlides();
        CheckMiss();
    }

    // ===== tap と slide始点 =====
    //押された入力に対して、判定幅内で一番早いノーツから順に判定する
    //ずれは「押した時刻 - ノーツの時刻」で求めるので、フレームレートに関係なく押したタイミングどおりに判定される
    void JudgeTaps()
    {
        while (true)
        {
            Note bestNote = null;
            LongNote bestSlide = null;
            float bestTime = float.MaxValue;
            float bestPressTime = 0f;//候補のノーツを押した時刻

            //「今判定できるノーツのうち、一番早いもの」を探す。
            //2つのifで条件に合わないノーツを飛ばし(continue)、全部通過したものを候補にする。
            //bestTimeは最初とても大きい値なので、最初に条件を満たしたノーツが必ず候補になり、
            //その後はより早いノーツが見つかるたびに入れ替わる。ループが終わると一番早いノーツが残る
            foreach (var note in NoteManager.instance.notes)
            {
                //すでに見つけた候補より遅いノーツは飛ばす
                if (note == null || note.expectedTime >= bestTime) continue;
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
                if (ln == null || !ln.isFirstSection || ln.state != LongNoteState.None) continue;
                if (ln.startExpectedTime >= bestTime) continue;
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
    void JudgeHolds()
    {
        foreach (var hold in new List<Hold>(NoteManager.instance.holds))
        {
            if (hold == null) continue;
            float diff = currentPlayTime - hold.expectedTime;
            if (diff < 0) continue;//まだ判定ラインに来ていない

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
        }
    }

    // ===== slide（判定点） =====
    //チュウニズム方式：一定間隔の判定点ごとに「その時点でslideの範囲に触れているか」を判定する
    //離したタイミングは判定せず、途中で離しても再び触れれば以降の判定点は取れる
    void JudgeSlides()
    {
        foreach (var ln in new List<LongNote>(NoteManager.instance.LongNotes))
        {
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
    void CheckMiss()
    {
        foreach (var note in new List<Note>(NoteManager.instance.notes))
        {
            if (note == null) continue;
            if (currentPlayTime - note.expectedTime > MissWindow)
            {
                ShowResult(JudgeResult.Miss, note.centerLane, currentPlayTime - note.expectedTime);
                note.Delete();
            }
        }

        foreach (var ln in new List<LongNote>(NoteManager.instance.LongNotes))
        {
            if (ln == null || !ln.isFirstSection || ln.state != LongNoteState.None) continue;
            float diff = currentPlayTime - ln.startExpectedTime;
            if (diff > MissWindow)
            {
                //始点を逃してもslideは続き、以降の判定点は触れれば取れる
                float centerLane = ln.startLane + ln.startWidth / 2f;
                ShowResult(JudgeResult.Miss, centerLane, diff);
                ln.Activate();
            }
        }
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
        Debug.Log($"{result}: {diffSec * 1000f:F1}ms");
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
