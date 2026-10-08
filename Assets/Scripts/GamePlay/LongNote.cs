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


    public float startWidth = 0.8f;//始点の帯の幅
    public float endWidth = 0.8f;//終点の帯の幅

    [Header("Children Objects")]
    public Transform StartNote;
    public Transform EndNote;
    public Transform HoldNote;

    private Mesh bodyMesh;//斜めの帯を描画するための動的メッシュ

    void Awake()
    {
        if (HoldNote != null)
        {
            //HoldNoteのCubeメッシュを差し替え、頂点座標をそのまま使えるようにTransformを初期化
            bodyMesh = new Mesh();
            bodyMesh.MarkDynamic();
            HoldNote.GetComponent<MeshFilter>().mesh = bodyMesh;
            HoldNote.localPosition = Vector3.zero;
            HoldNote.localRotation = Quaternion.identity;
            HoldNote.localScale = Vector3.one;
        }
    }

    public void Init(float startBar, float endBar, float startExpectedTime, float endExpectedTime, float startLanePos, float endLanePos)
    {
        this.startBar = startBar;
        this.endBar = endBar;
        this.endExpectedTime = endExpectedTime;
        this.startExpectedTime = startExpectedTime;
        this.startLanePos = startLanePos;
        this.endLanePos = endLanePos;
        // this.railStr = railStr;

        //親は原点に置き、x位置は子オブジェクト側で決める
        transform.localPosition = Vector3.zero;
    }

    //現在の小節位置におけるslideのx座標（判定などで使用）
    public float GetCurrentLanePos(float presentBar)
    {
        float t = Mathf.InverseLerp(startBar, endBar, presentBar);
        return Mathf.Lerp(startLanePos, endLanePos, t);
    }

    public void UpdatePosition(float presentBar)
    {
        this.presentBar = presentBar;

        startZ = (presentBar - startBar) * scrollSpeed;
        endZ = (presentBar - endBar) * scrollSpeed;

        transform.localPosition = Vector3.zero;

        float startX = startLanePos;
        float currentStartWidth = startWidth;

        if(state == LongNoteState.Holding)
        {
            //判定ライン上の位置まで始点を進める
            float t = Mathf.InverseLerp(startBar, endBar, presentBar);
            startX = Mathf.Lerp(startLanePos, endLanePos, t);
            currentStartWidth = Mathf.Lerp(startWidth, endWidth, t);
            startZ = 0;
            if(StartNote != null) StartNote.gameObject.SetActive(false);//StartNoteを削除
        }

        if(StartNote != null)StartNote.localPosition = new Vector3(startX,0.01f,startZ);
        if(EndNote != null)EndNote.localPosition = new Vector3(endLanePos,0.01f,endZ);
        if(bodyMesh != null)
        {
            UpdateBodyMesh(startX, startZ, currentStartWidth, endLanePos, Mathf.Min(endZ, startZ), endWidth);
        }
    }

    //始点・終点の4頂点で帯（平行四辺形/台形）のメッシュを作る
    private void UpdateBodyMesh(float sx, float sz, float sw, float ex, float ez, float ew)
    {
        const float y = 0.01f;
        bodyMesh.Clear();
        bodyMesh.vertices = new Vector3[]
        {
            new Vector3(sx - sw / 2, y, sz), // 0: 始点左
            new Vector3(sx + sw / 2, y, sz), // 1: 始点右
            new Vector3(ex - ew / 2, y, ez), // 2: 終点左
            new Vector3(ex + ew / 2, y, ez), // 3: 終点右
        };
        bodyMesh.uv = new Vector2[]
        {
            new Vector2(0, 0), new Vector2(1, 0), new Vector2(0, 1), new Vector2(1, 1),
        };
        bodyMesh.triangles = new int[] { 0, 1, 2, 1, 3, 2 };//上から見て時計回り（表面が上向き）
        bodyMesh.RecalculateNormals();
        bodyMesh.RecalculateBounds();
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
