using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class JudgeManager : MonoBehaviour
{
    public static JudgeManager instance;

    public static event Action Perfect;
    public static event Action Great;
    public static event Action Good;
    public static event Action Miss;
    public static event Action DRailMove;
    public static event Action KRailMove;
    public static event Action PlayIsoSound;

    public Transform DRailBase;
    public Transform FRailBase;
    public Transform JRailBase;
    public Transform KRailBase;


    private float currentPlayTime;
    private Note[] Notes;
    private int destroyedNotesCount;

    void Awake()
    {
        if (instance == null)
        {
            instance = this;
        }
    }
    // Start is called before the first frame update
    void Start()
    {
    }

    void JudgeRail(string key, List<Note> notes, List<LongNote> longNotes, Transform railBase)
    {
        CheckMiss(key, notes);
        CheckMissLongNote(key, longNotes);
        if (Input.GetKeyDown(key))
        {
            //インスタンスではなくリストのコピーを用いることで参照エラー回避。
            //現在の再生時間から探索範囲を絞れば数万ノーツでも軽い処理が可能になる
            var notesCopy = new List<Note>(notes);
            foreach (var note in notesCopy)
            {
                if (note == null) continue;
                float judgeTiming = (currentPlayTime - note.expectedTime) * 1000f;
                if (Mathf.Abs(judgeTiming) <= 22.25)
                {
                    EffectManager.instance.PerfectEffect(railBase);
                    Debug.Log($"parfect: {judgeTiming}ms");
                    note.Delete(key);
                    Perfect?.Invoke();
                    break; //多重判定回避
                }
                else if (Mathf.Abs(judgeTiming) <= 40)
                {
                    EffectManager.instance.GreatEffect(railBase);
                    Debug.Log($"great: {judgeTiming}ms");
                    note.Delete(key);
                    Great?.Invoke();
                    break; //多重判定回避
                }
                else if (Mathf.Abs(judgeTiming) <= 70)
                {
                    EffectManager.instance.GoodEffect(railBase);
                    Debug.Log($"good: {judgeTiming}ms");
                    note.Delete(key);
                    Good?.Invoke();
                    break; //多重判定回避
                }

            }

            var longNotesCopy = new List<LongNote>(longNotes);
            foreach (var ln in longNotesCopy)
            {
                if (ln.state != LongNoteState.None) continue;
                float judgeTiming = (currentPlayTime - ln.startExpectedTime) * 1000f;
                if (Mathf.Abs(judgeTiming) <= 22.25)
                {
                    EffectManager.instance.PerfectEffect(railBase);
                    EffectManager.instance.HoldEffect(railBase, key);
                    ln.OnStartPress();//別スクリプトでもちゃんと認識してくれる
                    Perfect?.Invoke();
                    break;
                }
                else if (Mathf.Abs(judgeTiming) <= 40)
                {
                    EffectManager.instance.GreatEffect(railBase);
                    EffectManager.instance.HoldEffect(railBase, key);
                    ln.OnStartPress();//別スクリプトでもちゃんと認識してくれる
                    Great?.Invoke();
                    break;
                }
                else if (Mathf.Abs(judgeTiming) <= 70)
                {
                    EffectManager.instance.GoodEffect(railBase);
                    EffectManager.instance.HoldEffect(railBase, key);
                    ln.OnStartPress();//別スクリプトでもちゃんと認識してくれる
                    Good?.Invoke();
                    break;
                }
            }
        }
        if (Input.GetKey(key))
        {
            var lnCopy = new List<LongNote>(longNotes);
            foreach (var ln in lnCopy)
            {
                if (ln.state == LongNoteState.Holding)
                {
                    //押し続けている間のコンボ加算などをここで行う
                    float endTiming = (currentPlayTime - ln.endExpectedTime) * 1000f;
                    if (endTiming >= 0)
                    {
                        EffectManager.instance.PerfectEffect(railBase);
                        DestroyHoldEffect(key);
                        ln.Finish(key);
                        Perfect?.Invoke();
                    }
                }
            }

        }
        if (Input.GetKeyUp(key))
        {
            var lnCopy = new List<LongNote>(longNotes);
            foreach (var ln in lnCopy)
            {
                if (ln.state == LongNoteState.Holding)//ホールドされてるロングノーツだけを判定するよ
                {
                    float judgeTiming = (currentPlayTime - ln.endExpectedTime) * 1000f;
                    Debug.Log($"release: {judgeTiming}ms");
                    if (judgeTiming < -70)
                    {
                        DestroyHoldEffect(key);
                        ln.OnReleaseEarly();
                        Miss?.Invoke();
                    }
                    else if (judgeTiming <= -40 && judgeTiming <= -70)
                    {
                        EffectManager.instance.GoodEffect(railBase);
                        DestroyHoldEffect(key);
                        ln.Finish(key);
                        Good?.Invoke();
                    }
                    else if (judgeTiming <= -22.25 && judgeTiming <= -40)
                    {
                        EffectManager.instance.GreatEffect(railBase);
                        DestroyHoldEffect(key);
                        ln.Finish(key);
                        Great?.Invoke();
                    }
                    else if (judgeTiming <= 0 && judgeTiming <= -22.25)
                    {
                        EffectManager.instance.PerfectEffect(railBase);
                        DestroyHoldEffect(key);
                        ln.Finish(ln.railStr);
                        Perfect?.Invoke();
                    }
                }
            }
        }
    }

    void SLJudgeRail(string key, List<Note> notes, Transform railBase)
    {
        CheckMiss(key, notes);
        if (Input.GetKeyDown(key))
        {
            var notesCopy = new List<Note>(notes);

            foreach (var note in notesCopy)
            {
                if (note == null) continue;
                float judgeTiming = (currentPlayTime - note.expectedTime) * 1000f;
                if (Mathf.Abs(judgeTiming) <= 22.25)
                {
                    PlayIsoSound?.Invoke();
                    EffectManager.instance.PerfectEffect(railBase);
                    Debug.Log($"parfect: {judgeTiming}ms");
                    note.Delete(key);
                    Perfect?.Invoke();
                    DestroyIsoNote(key);
                    break; //多重判定回避
                }
                else if (Mathf.Abs(judgeTiming) <= 40)
                {
                    PlayIsoSound?.Invoke();
                    EffectManager.instance.GreatEffect(railBase);
                    Debug.Log($"great: {judgeTiming}ms");
                    note.Delete(key);
                    Great?.Invoke();
                    DestroyIsoNote(key);
                    break; //多重判定回避
                }
                else if (Mathf.Abs(judgeTiming) <= 70)
                {
                    PlayIsoSound?.Invoke();
                    EffectManager.instance.GoodEffect(railBase);
                    Debug.Log($"good: {judgeTiming}ms");
                    note.Delete(key);
                    Good?.Invoke();
                    DestroyIsoNote(key);
                    break; //多重判定回避
                }
            }
        }
    }

    void CheckMiss(string key, List<Note> notes)
    {
        if(notes.Count == 0)return;
        var note = notes[0];
        if(note == null)return;

        //z座標が一定値を超えたらミス
        if(note.transform.position.z > 1.0f)
        {
            note.Delete(key);
            Miss?.Invoke();
        }
    }

    void CheckMissLongNote(string key, List<LongNote> longNotes)
    {
        if(longNotes.Count == 0)return;
        var ln = longNotes[0];
        if(ln == null)return;

        //z座標が一定値を超えたらミス
        if(ln.startZ > 1.0f && ln.state == LongNoteState.None)
        {
            ln.Finish(key);
            Miss?.Invoke();
        }
    }

    void DestroyIsoNote(string key)
    {
        switch (key)
        {
            case "s":
                DRailMove?.Invoke();
                break;
            case "l":
                KRailMove?.Invoke();
                break;
            default:
                break;
        }
    }

    void DestroyHoldEffect(string key)
    {
        switch (key)
        {
            case "d":
                Destroy(EffectManager.DHoldEffect);
                break;
            case "f":
                Destroy(EffectManager.FHoldEffect);
                break;
            case "j":
                Destroy(EffectManager.JHoldEffect);
                break;
            case "k":
                Destroy(EffectManager.KHoldEffect);
                break;
            default:
                break;

        }
    }
    // void Update()
    // {
    //     currentPlayTime = MusicManager.instance.CurrentPlayTime;
    //     JudgeRail("d", NoteManager.instance.DNotes, NoteManager.instance.DLongNotes, DRailBase);
    //     JudgeRail("f", NoteManager.instance.FNotes, NoteManager.instance.FLongNotes, FRailBase);
    //     JudgeRail("j", NoteManager.instance.JNotes, NoteManager.instance.JLongNotes, JRailBase);
    //     JudgeRail("k", NoteManager.instance.KNotes, NoteManager.instance.KLongNotes, KRailBase);
    //     SLJudgeRail("s", NoteManager.instance.SNotes, DRailBase);
    //     SLJudgeRail("l", NoteManager.instance.LNotes, KRailBase);
    // }
}
