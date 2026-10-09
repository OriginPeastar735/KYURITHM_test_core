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
    public float lanePos; //ノーツ中心のx座標
    private float presentBar;//楽曲の現在の小節位置
    public bool judged = false;

    public void Init(float noteBar, float expectedTime, int lane, int width)
    {
        this.noteBar = noteBar;
        this.expectedTime = expectedTime;
        this.lane = lane;
        this.width = width;
        this.lanePos = LaneLayout.LanePos(lane + width / 2f);

        //プレハブは横幅1のCubeなので、scale.xがそのまま見た目の横幅になる
        Vector3 scale = transform.localScale;
        scale.x = width * LaneLayout.LaneWidth;
        transform.localScale = scale;

        transform.localPosition = new Vector3(lanePos, 0, 0);//transform.positonはworld基準で座標を指定する。localPositionにすれば親基準の座標を指定できる。
    }

    public void UpdatePosition(float presentBar)
    {
        this.presentBar = presentBar;
        float z = (presentBar - noteBar) * scrollSpeed;

        Vector3 local = transform.localPosition;
        local.z = z;
        local.y = 0.01f;
        transform.localPosition = local;

    }

    public void Delete()
    {
        NoteManager.instance.RemoveHold(this);//自身をListから削除
        Destroy(gameObject);
    }
}
