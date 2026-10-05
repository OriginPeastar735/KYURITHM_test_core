using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class Hold : MonoBehaviour
{
    public float noteBar;//ノーツの小節位置
    public float scrollSpeed = 1000f;//スクロール定数
    public float expectedTime; //予定ヒット時間。今は座標0が理想タイミングと仮定してプログラム
    public float lanePos; //どのレーンを始点とするか
    private float presentBar;//楽曲の現在の小節位置
    public bool judged = false;

    public void Init(float noteBar, float expectedTime, float lanePos)
    {
        this.noteBar = noteBar;
        this.expectedTime = expectedTime;
        this.lanePos = lanePos;

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

    public void Delete(string lane)
    {
        NoteManager.instance.RemoveHold(this, lane);//自身をListから削除
        Destroy(gameObject);
    }
}
