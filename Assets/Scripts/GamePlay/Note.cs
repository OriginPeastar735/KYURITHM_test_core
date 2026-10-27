using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class Note : MonoBehaviour
{
    public float noteBar;//ノーツの小節位置
    public float scrollSpeed = 1000f;//スクロール定数
    public float expectedTime; //予定ヒット時間。今は座標0が理想タイミングと仮定してプログラム
    public int lane;//ノーツの始点レーン
    public int width;//ノーツが占めるレーン数
    public float centerLane; //ノーツ中心のレーン位置（エフェクトの位置などで使用）
    private float presentBar;//楽曲の現在の小節位置
    public bool judged = false;

    public void Init(float noteBar, float expectedTime, int lane, int width)
    {
        this.noteBar = noteBar;
        this.expectedTime = expectedTime;
        this.lane = lane;
        this.width = width;
        this.centerLane = lane + width / 2f;

        //x,yと傾きはLaneSurfaceShapeが子の板で作るので、自身はz方向に動かすだけ
        //y=0.01fは、床と同じ高さだとチラつく(Zファイティング)ので少し浮かせるための値
        LaneSurfaceShape shape = gameObject.AddComponent<LaneSurfaceShape>();
        NoteManager.instance.ApplyNoteStyle(shape, true);//角を丸め、中心に線を入れる
        shape.SetRange(lane, width, 0.01f);

        transform.localPosition = Vector3.zero;//transform.positonはworld基準で座標を指定する。localPositionにすれば親基準の座標を指定できる。
    }

    public void UpdatePosition(float presentBar)
    {
        this.presentBar = presentBar;
        float z = (presentBar - noteBar) * scrollSpeed;

        Vector3 local = transform.localPosition;
        local.z = z;
        transform.localPosition = local;

    }

    public void Delete()
    {
        NoteManager.instance.RemoveNote(this);//自身をListから削除
        Destroy(gameObject);
    }
}
