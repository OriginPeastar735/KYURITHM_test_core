//Holdノーツ: レーンを押しっぱなしでも判定ライン通過時にPRECISE+として処理される。
//判定ラインを過ぎてから判定幅(MISS)以内に一度も押されなければMISSになる。

using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class Hold : MonoBehaviour
{
    public float noteBar;//ノーツの小節位置
    public float scrollSpeed = 1000f;//1小節あたりに進む距離（NoteManagerがハイスピードから設定する）
    public float expectedTime;//判定ラインに来る時刻（曲の再生時間、秒）
    public int lane;//ノーツの始点レーン
    public int width;//ノーツが占めるレーン数
    public float centerLane; //ノーツ中心のレーン位置（エフェクトの位置などで使用）
    private float presentBar;//楽曲の現在の小節位置
    public bool judged = false;//判定済みか（判定済みのノーツはDeleteで消すので参照していない）
    private float frontOffset;//ノーツの中心から手前の面までの距離

    public void Init(float noteBar, float expectedTime, int lane, int width)
    {
        this.noteBar = noteBar;
        this.expectedTime = expectedTime;
        this.lane = lane;
        this.width = width;
        //ノーツの中心は、始点レーンから幅の半分だけ進んだ位置（width=4なら2レーン分）。
        //例: lane=8, width=4 → 中心はレーン10の位置。座標への変換はLaneLayoutで行う
        this.centerLane = lane + width / 2f;

        //x,yと傾きはLaneSurfaceShapeが子の板で作る（傾斜の折れ目をまたぐ場合は折れ目で分割される）
        //高さはslideより上（LaneLayout.TopNoteHeight）
        LaneSurfaceShape shape = gameObject.AddComponent<LaneSurfaceShape>();
        NoteManager.instance.ApplyNoteStyle(shape, true);//角を丸め、縁と中心の線を付ける
        shape.SetRange(lane, width, LaneLayout.TopNoteHeight);
        frontOffset = shape.Depth / 2f;

        //自身はz方向に動かすだけなので原点に置く
        transform.localPosition = Vector3.zero;
    }

    //現在の小節位置に合わせてz位置を動かし、壁より奥なら非表示にする（NoteManagerが毎フレーム呼ぶ）
    public void UpdatePosition(float presentBar)
    {
        this.presentBar = presentBar;

        //判定ラインからの距離 = (現在の小節位置 - ノーツの小節位置) × scrollSpeed
        //zは判定前はマイナスで、判定タイミングでちょうど0(判定ライン)になる。
        //scrollSpeedが大きいほど遠くに置かれ、その分速く流れてくるように見える。
        float z = (presentBar - noteBar) * scrollSpeed;

        //ノーツ自身のローカル座標を指定（浮かせる高さはInitでLaneSurfaceShapeに渡している）
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
        NoteManager.instance.RemoveHold(this);
        Destroy(gameObject);
    }
}
