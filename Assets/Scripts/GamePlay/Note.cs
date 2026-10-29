using System.Collections;
using System.Collections.Generic;
using UnityEngine;

//tapノーツ。NoteManagerが生成して毎フレーム動かし、JudgeManagerが判定する
public class Note : MonoBehaviour
{
    public float noteBar;//ノーツの小節位置
    public float scrollSpeed = 1000f;//1小節あたりに進む距離（NoteManagerがハイスピードから設定する）
    public float expectedTime;//判定ラインに来る時刻（曲の再生時間、秒）
    public int lane;//ノーツの始点レーン
    public int width;//ノーツが占めるレーン数
    public float centerLane; //ノーツ中心のレーン位置（エフェクトの位置などで使用）
    private float presentBar;//楽曲の現在の小節位置
    public bool judged = false;
    private float frontOffset;//ノーツの中心から手前の面までの距離

    public void Init(float noteBar, float expectedTime, int lane, int width)
    {
        this.noteBar = noteBar;
        this.expectedTime = expectedTime;
        this.lane = lane;
        this.width = width;
        this.centerLane = lane + width / 2f;

        //x,yと傾きはLaneSurfaceShapeが子の板で作るので、自身はz方向に動かすだけ
        //高さは判定ラインより上（LaneLayout.NoteHeight）
        LaneSurfaceShape shape = gameObject.AddComponent<LaneSurfaceShape>();
        NoteManager.instance.ApplyNoteStyle(shape, true);//角を丸め、縁と中心の線を付ける
        shape.SetRange(lane, width, LaneLayout.NoteHeight);
        frontOffset = shape.Depth / 2f;

        transform.localPosition = Vector3.zero;//z位置はUpdatePositionで動かす
    }

    //現在の小節位置に合わせてz位置を動かし、壁より奥なら非表示にする（NoteManagerが毎フレーム呼ぶ）
    public void UpdatePosition(float presentBar)
    {
        this.presentBar = presentBar;
        float z = (presentBar - noteBar) * scrollSpeed;

        Vector3 local = transform.localPosition;
        local.z = z - frontOffset;//手前の面が判定ラインに触れたときが判定のタイミング
        transform.localPosition = local;

        //壁より遠いノーツは表示しない（zは判定前はマイナスなので、-VisibleLengthより小さければ奥）
        bool visible = z >= -LaneLayout.VisibleLength;
        if (gameObject.activeSelf != visible) gameObject.SetActive(visible);
    }

    //NoteManagerのリストから外してオブジェクトを消す
    public void Delete()
    {
        NoteManager.instance.RemoveNote(this);//自身をListから削除
        Destroy(gameObject);
    }
}
