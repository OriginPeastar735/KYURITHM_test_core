using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public enum JudgeResult
{
    Perfect, Great, Good, Miss
}

//InputManagerのレーン入力とノーツのlane/widthを照らし合わせて判定する
public class JudgeManager : MonoBehaviour
{
    public static JudgeManager instance;

    public static event Action Perfect;
    public static event Action Great;
    public static event Action Good;
    public static event Action Miss;
    //以前の音ゲーの演出用（RailBaseManager/MusicManagerが購読しているため残している）
    public static event Action DRailMove;
    public static event Action KRailMove;
    public static event Action PlayIsoSound;

    [Header("判定幅(ms)")]
    public float perfectMs = 22.25f;
    public float greatMs = 40f;
    public float goodMs = 70f;

    [Header("slide")]
    public float slideReleaseGrace = 0.1f;//判定点の前後で指が離れていても許容する秒数（キーの持ち替え対策）
    public int slideLaneMargin = 0;//slideの範囲を左右に何レーン広げて判定するか（判定を甘くしたい場合に使う）

    private float currentPlayTime;
    private float GoodWindow => goodMs / 1000f;

    //このフレームで既に判定に使った入力。1回の入力で複数ノーツを取らないようにする
    private readonly bool[] consumed = new bool[InputManager.LaneCount];

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
        Array.Clear(consumed, 0, consumed.Length);

        JudgeTaps();
        JudgeHolds();
        JudgeSlides();
        CheckMiss();
    }

    // ===== tap と slide始点 =====
    //押された入力に対して、判定幅内で一番早いノーツから順に判定する
    void JudgeTaps()
    {
        while (true)
        {
            Note bestNote = null;
            LongNote bestSlide = null;
            float bestTime = float.MaxValue;

            foreach (var note in NoteManager.instance.notes)
            {
                if (note == null || note.expectedTime >= bestTime) continue;
                if (Mathf.Abs(currentPlayTime - note.expectedTime) > GoodWindow) continue;
                if (!HasFreeDown(note.lane, note.width)) continue;
                bestNote = note;
                bestSlide = null;
                bestTime = note.expectedTime;
            }

            foreach (var ln in NoteManager.instance.LongNotes)
            {
                if (ln == null || !ln.isFirstSection || ln.state != LongNoteState.None) continue;
                if (ln.startExpectedTime >= bestTime) continue;
                if (Mathf.Abs(currentPlayTime - ln.startExpectedTime) > GoodWindow) continue;
                if (!HasFreeDown(ln.startLane, ln.startWidth)) continue;
                bestNote = null;
                bestSlide = ln;
                bestTime = ln.startExpectedTime;
            }

            if (bestNote != null)
            {
                float diff = currentPlayTime - bestNote.expectedTime;
                Consume(bestNote.lane, bestNote.width);
                ShowResult(GradeByTiming(diff), bestNote.lanePos, diff);
                bestNote.Delete();
            }
            else if (bestSlide != null)
            {
                float diff = currentPlayTime - bestSlide.startExpectedTime;
                float x = LaneLayout.LanePos(bestSlide.startLane + bestSlide.startWidth / 2f);
                Consume(bestSlide.startLane, bestSlide.startWidth);
                ShowResult(GradeByTiming(diff), x, diff);
                bestSlide.Activate();
            }
            else
            {
                break;//判定できるノーツがもうない
            }
        }
    }

    // ===== hold =====
    //判定ラインを通過する時にノーツの範囲が押されていればPerfect
    void JudgeHolds()
    {
        foreach (var hold in new List<Hold>(NoteManager.instance.holds))
        {
            if (hold == null) continue;
            float diff = currentPlayTime - hold.expectedTime;
            if (diff < 0) continue;//まだ判定ラインに来ていない

            if (InputManager.instance.AnyHeld(hold.lane, hold.width))
            {
                ShowResult(JudgeResult.Perfect, hold.lanePos, diff);
                hold.Delete();
            }
            else if (diff > GoodWindow)
            {
                ShowResult(JudgeResult.Miss, hold.lanePos, diff);
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

            float x = ln.GetCenterXAt(checkpoint);
            if (checkpoint - ln.lastHeldTime <= slideReleaseGrace)
            {
                //判定点の直前(猶予内)から今までに触れていた
                ShowResult(JudgeResult.Perfect, x, 0);
            }
            else if (currentPlayTime - checkpoint > slideReleaseGrace)
            {
                ShowResult(JudgeResult.Miss, x, currentPlayTime - checkpoint);
            }
            else
            {
                break;//猶予時間内に触れるのを待つ
            }
            ln.nextCheckpoint++;
        }

        if (ln.nextCheckpoint >= ln.checkpointTimes.Count) ln.Finish();
    }

    //触れている間だけエフェクトを出す（位置はLongNote.UpdatePositionで追従）
    void UpdateHoldEffect(LongNote ln, bool held)
    {
        if (held && ln.holdEffect == null)
        {
            ln.holdEffect = EffectManager.instance.HoldEffect(ln.GetCenterXAt(currentPlayTime));
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
            if (currentPlayTime - note.expectedTime > GoodWindow)
            {
                ShowResult(JudgeResult.Miss, note.lanePos, currentPlayTime - note.expectedTime);
                note.Delete();
            }
        }

        foreach (var ln in new List<LongNote>(NoteManager.instance.LongNotes))
        {
            if (ln == null || !ln.isFirstSection || ln.state != LongNoteState.None) continue;
            float diff = currentPlayTime - ln.startExpectedTime;
            if (diff > GoodWindow)
            {
                //始点を逃してもslideは続き、以降の判定点は触れれば取れる
                float x = LaneLayout.LanePos(ln.startLane + ln.startWidth / 2f);
                ShowResult(JudgeResult.Miss, x, diff);
                ln.Activate();
            }
        }
    }

    // ===== 共通処理 =====
    JudgeResult GradeByTiming(float diffSec)
    {
        float ms = Mathf.Abs(diffSec * 1000f);
        if (ms <= perfectMs) return JudgeResult.Perfect;
        if (ms <= greatMs) return JudgeResult.Great;
        if (ms <= goodMs) return JudgeResult.Good;
        return JudgeResult.Miss;
    }

    //判定結果に応じてエフェクトとイベントを発行する
    void ShowResult(JudgeResult result, float x, float diffSec)
    {
        Debug.Log($"{result}: {diffSec * 1000f:F1}ms");
        switch (result)
        {
            case JudgeResult.Perfect:
                EffectManager.instance.PerfectEffect(x);
                Perfect?.Invoke();
                break;
            case JudgeResult.Great:
                EffectManager.instance.GreatEffect(x);
                Great?.Invoke();
                break;
            case JudgeResult.Good:
                EffectManager.instance.GoodEffect(x);
                Good?.Invoke();
                break;
            case JudgeResult.Miss:
                Miss?.Invoke();
                break;
        }
    }

    //範囲内に、まだ使われていない「押した瞬間」の入力があるか
    bool HasFreeDown(int lane, int width)
    {
        int start = Mathf.Max(0, lane);
        int end = Mathf.Min(InputManager.LaneCount, lane + width);
        for (int i = start; i < end; i++)
        {
            if (InputManager.instance.laneDown[i] && !consumed[i]) return true;
        }
        return false;
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
