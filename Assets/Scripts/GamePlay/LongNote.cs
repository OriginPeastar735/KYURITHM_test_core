using System.Collections;
using System.Collections.Generic;
using JetBrains.Annotations;
using UnityEngine;

public enum LongNoteState
{
    None, Holding, Finished
}

public class LongNote : MonoBehaviour
{
    public LongNoteState state = LongNoteState.None;//このロングノーツの現在の状態を示す
    public float startBar;//ノーツの小節位置
    public float scrollSpeed = 1000f;//スクロール定数
    public float startExpectedTime;
    public float endExpectedTime; //予定ヒット時間。今は座標0が理想タイミングと仮定してプログラム
    private float presentBar;//楽曲の現在の小節位置
    private float endBar;//ロングノーツ終点
    public float startLanePos;
    public float endLanePos; 
    public bool judged = false;
    public string railStr;
    public float endZ;
    public float startZ;


    [Header("Children Objects")]
    public Transform StartNote;
    public Transform EndNote;
    public Transform HoldNote;

    public void Init(float startBar, float endBar, float startExpectedTime, float endExpectedTime, float startLanePos, float endLanePos)
    {
        this.startBar = startBar;
        this.endBar = endBar;
        this.endExpectedTime = endExpectedTime;
        this.startExpectedTime = startExpectedTime;
        this.startLanePos = startLanePos;
        this.endLanePos = endLanePos;
        // this.railStr = railStr;

        transform.localPosition = new Vector3(startLanePos, 0, 0);
    
    }

    public void UpdatePosition(float presentBar)
    {
        this.presentBar = presentBar;
        
        startZ = (presentBar - startBar) * scrollSpeed;
        endZ = (presentBar - endBar) * scrollSpeed;

        transform.localPosition = Vector3.zero;

        if(state == LongNoteState.Holding)
        {
            startZ = 0;
            if(StartNote != null) StartNote.gameObject.SetActive(false);//StartNoteを削除
        }

        if(StartNote != null)StartNote.localPosition = new Vector3(0,0.01f,startZ);
        if(EndNote != null)EndNote.localPosition = new Vector3(0,0.01f,endZ);
        if(HoldNote != null)
        {
            float length = Mathf.Min(0, endZ-startZ);
            HoldNote.localPosition = new Vector3(0f,0.01f,(startZ+endZ)/2);
            HoldNote.localScale = new Vector3(0.8f,0.1f,length);
        }
        

    }

    public void OnStartPress()
    {
        state = LongNoteState.Holding;
    }

    public void OnReleaseEarly()
    {
        Finish(railStr);
    }

    public void Finish(string railStr)
    {
        state = LongNoteState.Finished;
        //NoteManagerのリストから消してDestroyする処理を追記
        NoteManager.instance.RemoveLongNote(this, railStr);
        Destroy(gameObject);
    }

    /*public void Delete(string lane)
    {
        NoteManager.instance.RemoveNote(this, lane);//自身をListから削除
        Destroy(gameObject);
    }*/
}
