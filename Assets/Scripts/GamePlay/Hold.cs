//Holdノーツ: レールを押しっぱなしでも判定ライン通過時にPRECISE+として処理される。
//判定ラインを過ぎてから判定幅(Good)以内に一度も押されなければMissになる。

using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class Hold : MonoBehaviour
{
    public float noteBar;//ノーツの小節位置
    public float scrollSpeed = 1000f;//スクロール定数
    public float expectedTime; //予定ヒット時間。今は座標0が理想タイミングと仮定してプログラム
    public int lane;//ノーツの始点レーン
    public int width;//ノーツが占めるレーン数
    public float centerLane; //ノーツ中心のレーン位置（エフェクトの位置などで使用）
    private float presentBar;//楽曲の現在の小節位置
    public bool judged = false;//判定（PRECISE+, PRECISE...）が下されたかの変数。現在は未使用（判定済みのノーツはDeleteで消しているため）

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
        //y=0.01fは、床と同じ高さだとチラつく(Zファイティング)ので少し浮かせるための値
        gameObject.AddComponent<LaneSurfaceShape>().SetRange(lane, width, 0.01f);

        //自身はz方向に動かすだけなので原点に置く
        transform.localPosition = Vector3.zero;//transform.positonはworld基準で座標を指定する。localPositionにすれば親基準の座標を指定できる。
    }

    public void UpdatePosition(float presentBar)
    {
        //毎フレーム実行される関数なので現在の小節位置が常に更新される
        this.presentBar = presentBar;

        //現在の小節位置とノーツ自身の小節位置のギャップ×scrollSpeed(ユーザー設定)。
        //zは判定前はマイナスで、判定タイミングでちょうど0(判定ライン)になる。
        //scrollSpeedが大きいほど遠くに置かれ、その分速く流れてくるように見える。
        float z = (presentBar - noteBar) * scrollSpeed;

        //ノーツ自身のローカル座標を指定（浮かせる高さはInitでLaneSurfaceShapeに渡している）
        Vector3 local = transform.localPosition;
        local.z = z;
        transform.localPosition = local;

    }

    //ノーツ自身の削除を行う
    public void Delete()
    {
        NoteManager.instance.RemoveHold(this);//自身をListから削除
        Destroy(gameObject);//オブジェクト自体を削除
    }
}
