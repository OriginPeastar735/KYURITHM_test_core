using System.Collections;
using System.Collections.Generic;
using JetBrains.Annotations;
using UnityEngine;

public enum LongNoteState
{
    None, Holding, Finished
}

//slideのセクションの曲がり方
public enum SlideCurve
{
    Linear,//一定の速さで移動
    In,//in_curve：始点側で大きく動き、終点に近づくほどゆっくり
    Out//out_curve：始点側はゆっくり、終点に近づくほど大きく動く
}

//slideの1セクション分（from→to）を表すノーツ
public class LongNote : MonoBehaviour
{
    public LongNoteState state = LongNoteState.None;//このロングノーツの現在の状態を示す
    public float startBar;//ノーツの小節位置
    public float scrollSpeed = 1000f;//スクロール定数
    public float startExpectedTime;
    public float endExpectedTime; //予定ヒット時間。今は座標0が理想タイミングと仮定してプログラム
    private float presentBar;//楽曲の現在の小節位置
    private float endBar;//ロングノーツ終点
    public float endZ;
    public float startZ;

    [Header("Lane")]
    public int startLane;//始点レーン
    public int startWidth;//始点のレーン数
    public int endLane;//終点レーン
    public int endWidth;//終点のレーン数
    public float bodyWidthRatio = 0.8f;//ノーツ幅に対する帯の幅の割合

    [Header("Curve")]
    public SlideCurve curve = SlideCurve.Linear;
    public int curveSegments = 16;//曲線の帯を何分割して描くか（多いほど滑らか）

    [Header("Section")]
    public bool isFirstSection = true;//slideの最初のセクションか（始点をタップで判定する）
    public bool isLastSection = true;//slideの最後のセクションか

    [HideInInspector] public List<float> checkpointTimes = new List<float>();//slide中の判定点の時刻
    [HideInInspector] public int nextCheckpoint = 0;//次に判定する判定点の番号
    [HideInInspector] public float lastHeldTime = float.NegativeInfinity;//最後に押されていた時刻（判定点の猶予用）
    [HideInInspector] public GameObject holdEffect;//押している間のエフェクト

    [Header("Children Objects")]
    public Transform StartNote;
    public Transform EndNote;
    public Transform HoldNote;

    private Mesh bodyMesh;//斜めの帯を描画するための動的メッシュ
    private Vector3[] bodyVertices;//帯の頂点（始点側から順に左右2つずつ）

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

    public void Init(float startBar, float endBar, float startExpectedTime, float endExpectedTime,
                     int startLane, int startWidth, int endLane, int endWidth,
                     bool isFirstSection, bool isLastSection, List<float> checkpointTimes, SlideCurve curve)
    {
        this.curve = curve;
        this.checkpointTimes = checkpointTimes;
        this.nextCheckpoint = 0;
        this.startBar = startBar;
        this.endBar = endBar;
        this.endExpectedTime = endExpectedTime;
        this.startExpectedTime = startExpectedTime;
        this.startLane = startLane;
        this.startWidth = startWidth;
        this.endLane = endLane;
        this.endWidth = endWidth;
        this.isFirstSection = isFirstSection;
        this.isLastSection = isLastSection;

        //親は原点に置き、x位置は子オブジェクト側で決める
        transform.localPosition = Vector3.zero;

        SetNoteWidth(StartNote, startWidth);
        SetNoteWidth(EndNote, endWidth);

        //2つ目以降のセクションの始点は前のセクションの終点と重なるので表示しない
        if (!isFirstSection && StartNote != null) StartNote.gameObject.SetActive(false);

        //直線なら1区間、曲線なら分割して帯を描く
        if (bodyMesh != null) SetupBodyMesh(curve == SlideCurve.Linear ? 1 : Mathf.Max(1, curveSegments));
    }

    //jsonの"curve"の文字列をSlideCurveに変換する
    public static SlideCurve ParseCurve(string curve)
    {
        switch (curve)
        {
            case "in_curve": return SlideCurve.In;
            case "out_curve": return SlideCurve.Out;
            default: return SlideCurve.Linear;
        }
    }

    //進み具合t(0〜1)を、曲線の種類に応じた移動の割合(0〜1)に変換する
    private float Ease(float t)
    {
        switch (curve)
        {
            case SlideCurve.In: return 1f - (1f - t) * (1f - t);//最初に速く動く
            case SlideCurve.Out: return t * t;//最後に速く動く
            default: return t;
        }
    }

    //進み具合t(0〜1)におけるレーン範囲
    private void GetLaneRangeAtProgress(float t, out float lane, out float width)
    {
        float e = Ease(t);
        lane = Mathf.Lerp(startLane, endLane, e);
        width = Mathf.Lerp(startWidth, endWidth, e);
    }

    private void SetNoteWidth(Transform note, int width)
    {
        if (note == null) return;
        Vector3 scale = note.localScale;
        scale.x = width * LaneLayout.LaneWidth;
        note.localScale = scale;
    }

    //時刻timeにおける判定ライン上のレーン範囲（lane, width）。小数になりうる
    public void GetLaneRangeAt(float time, out float lane, out float width)
    {
        float t = Mathf.InverseLerp(startExpectedTime, endExpectedTime, time);
        GetLaneRangeAtProgress(t, out lane, out width);
    }

    //時刻timeにおける判定ライン上の中心x座標（エフェクト位置などで使用）
    public float GetCenterXAt(float time)
    {
        GetLaneRangeAt(time, out float lane, out float width);
        return LaneLayout.LanePos(lane + width / 2f);
    }

    public void UpdatePosition(float presentBar)
    {
        this.presentBar = presentBar;

        startZ = (presentBar - startBar) * scrollSpeed;
        endZ = (presentBar - endBar) * scrollSpeed;

        transform.localPosition = Vector3.zero;

        float t0 = 0f;//帯を描き始める進み具合

        if(state == LongNoteState.Holding)
        {
            //判定ライン上の位置まで始点を進める
            t0 = Mathf.InverseLerp(startBar, endBar, presentBar);
            startZ = 0;
            if(StartNote != null) StartNote.gameObject.SetActive(false);//StartNoteを削除
        }

        GetLaneRangeAtProgress(t0, out float startLaneF, out float startWidthF);
        float startX = LaneLayout.LanePos(startLaneF + startWidthF / 2f);
        float endX = LaneLayout.LanePos(endLane + endWidth / 2f);

        if(StartNote != null)StartNote.localPosition = new Vector3(startX,0.01f,startZ);
        if(EndNote != null)EndNote.localPosition = new Vector3(endX,0.01f,endZ);
        if(bodyMesh != null)
        {
            UpdateBodyMesh(t0);
        }

        //押している間のエフェクトを判定ライン上の現在位置に追従させる
        if(holdEffect != null)
        {
            Vector3 pos = holdEffect.transform.position;
            pos.x = startX;
            holdEffect.transform.position = pos;
        }
    }

    //帯をsegments個の四角形に分割したメッシュを用意する（頂点数と三角形は以後変わらない）
    //  0 ── 1   ← 始点側
    //  2 ── 3
    //  4 ── 5   ← 終点側（segments=2の場合）
    private void SetupBodyMesh(int segments)
    {
        int vertexCount = (segments + 1) * 2;
        bodyVertices = new Vector3[vertexCount];
        var uv = new Vector2[vertexCount];
        var normals = new Vector3[vertexCount];
        var triangles = new int[segments * 6];

        for (int i = 0; i <= segments; i++)
        {
            float v = (float)i / segments;
            uv[i * 2] = new Vector2(0, v);
            uv[i * 2 + 1] = new Vector2(1, v);
            normals[i * 2] = Vector3.up;
            normals[i * 2 + 1] = Vector3.up;
        }
        for (int i = 0; i < segments; i++)
        {
            int a = i * 2;//この区間の始点側の左
            //上から見て時計回り（表面が上向き）
            triangles[i * 6 + 0] = a;
            triangles[i * 6 + 1] = a + 1;
            triangles[i * 6 + 2] = a + 2;
            triangles[i * 6 + 3] = a + 1;
            triangles[i * 6 + 4] = a + 3;
            triangles[i * 6 + 5] = a + 2;
        }

        bodyMesh.Clear();
        bodyMesh.vertices = bodyVertices;
        bodyMesh.uv = uv;
        bodyMesh.normals = normals;//帯は常に水平なので法線は上向きで固定
        bodyMesh.triangles = triangles;
    }

    //進み具合t0から終点までの帯を、曲線に沿って頂点を並べて描く
    private void UpdateBodyMesh(float t0)
    {
        const float y = 0.01f;
        int segments = bodyVertices.Length / 2 - 1;
        float lw = LaneLayout.LaneWidth * bodyWidthRatio;

        for (int i = 0; i <= segments; i++)
        {
            float t = Mathf.Lerp(t0, 1f, (float)i / segments);
            //zは時間に比例（一定速度でスクロール）、xは曲線に沿って変化する
            float z = Mathf.Min((presentBar - Mathf.Lerp(startBar, endBar, t)) * scrollSpeed, startZ);
            GetLaneRangeAtProgress(t, out float lane, out float width);
            float x = LaneLayout.LanePos(lane + width / 2f);
            float w = width * lw;

            bodyVertices[i * 2] = new Vector3(x - w / 2, y, z);
            bodyVertices[i * 2 + 1] = new Vector3(x + w / 2, y, z);
        }

        bodyMesh.vertices = bodyVertices;
        bodyMesh.RecalculateBounds();
    }

    //始点の判定が終わり、判定点の判定を始める（始点を逃した場合も呼ぶ）
    public void Activate()
    {
        state = LongNoteState.Holding;
    }

    public void Finish()
    {
        state = LongNoteState.Finished;
        if (holdEffect != null) Destroy(holdEffect);
        //NoteManagerのリストから消してDestroyする
        NoteManager.instance.RemoveLongNote(this);
        Destroy(gameObject);
    }
}
