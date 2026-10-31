using System.Collections;
using System.Collections.Generic;
using UnityEngine;

//起動時にキービーム・区切り線・判定ライン・床・壁の線をLaneLayoutに合わせて生成する
//左右の傾斜レーンの分は、面に沿って傾けて置く
//このオブジェクトはワールド原点(位置0・回転0・スケール1)に置くこと
public class LaneFieldBuilder : MonoBehaviour
{
    public GameObject LaneLightPrefab;//キービームのプレハブ（KeyBeamFadeが付いたもの）
    public GameObject LaneLinePrefab;//区切り線のプレハブ
    public GameObject JudgeLinePrefab;//判定ラインのプレハブ（未設定なら生成しない）。傾斜に沿うよう1レーンずつ並べる
    public GameObject RailVisualPrefab;//レーンの床のプレハブ（未設定なら生成しない）。判定ラインと同じく1レーンずつ並べる

    public int thickLineInterval = 4;//この本数ごとに区切り線を太くする（キー単位）
    public float thickLineScale = 2f;//太い区切り線の太さ倍率
    public float foldLineScale = 5f;//底面と斜面の境目・斜面の端の線（境界線）の太さ倍率
    public float maxLineAlpha = 0.5f;//区切り線の濃さ100%のときのアルファ（100%でも真っ白だと邪魔なので抑える）
    public float boundaryLineAlpha = 0.8f;//境界線の濃さ。区切り線の濃さの設定とは別に、常にはっきり見せる
    public Color judgeLineColor = new Color(0.8f, 0.8f, 0.8f, 1f);//判定ラインと壁の線の色
    public float bottomSmoothness = 0.15f;//底面の床の滑らかさ。上を向いた底面は光を強く反射して白っぽく見えるので、斜面より下げる
    public float laneLength = 30f;//判定ラインからレーンの奥端までの長さ。これより奥のノーツは表示しない
    public float wallLineThickness = 2f / 3f;//壁の線の太さ（判定ラインの太さに対する割合）

    //半透明のものを描く順番（数字が大きいほど後に描かれて手前に見える）。床 → キービーム → 区切り線 → 判定ライン・壁の線
    const int RailQueue = 3000;
    const int BeamQueue = 3001;
    const int LineQueue = 3002;
    const int JudgeLineQueue = 3003;

    public static LaneFieldBuilder instance;

    //斜面のものを、チュートリアルで斜面を使わない間だけ隠すために覚えておく
    private readonly List<GameObject> slopeObjects = new List<GameObject>();//斜面のキービーム・区切り線・判定ライン・壁の線
    private readonly List<Renderer> slopeRails = new List<Renderer>();//斜面の床
    private Material slopeRailMaterial;//斜面の床の普段のマテリアル
    private Material hiddenRailMaterial;//隠している間の斜面の床（光を反射しないもの）

    [Header("チュートリアルで斜面を使わない間の見え方")]
    [Range(0f, 1f)] public float hiddenSlopeLineAlpha = 0.3f;//斜面の区切り線・判定ライン・壁の線の濃さ（元の濃さに掛ける。0で見えない）

    void Awake()
    {
        instance = this;
        LaneLayout.LaneLength = laneLength;
        //判定ラインが筐体のセンサーに重なるようにカメラを置く
        Camera cam = Camera.main;
        if (cam != null && cam.GetComponent<SensorCameraFit>() == null) cam.gameObject.AddComponent<SensorCameraFit>();

        BuildLights();
        BuildLines();
        Material judgeMaterial = QueuedMaterial(JudgeLinePrefab, JudgeLineQueue);
        if (judgeMaterial != null) judgeMaterial.SetColor("_BaseColor", judgeLineColor);
        BuildPerLane(JudgeLinePrefab, "JudgeLine", false, judgeMaterial, judgeMaterial);
        //床の濃さは選曲画面の設定。0%なら床を作らない（透明にしても光の反射は残るため）
        if (PlaySession.LaneOpacityPercent > 0)
        {
            Material sideMaterial = RailMaterial();
            Material bottomMaterial = sideMaterial != null ? new Material(sideMaterial) : null;
            if (bottomMaterial != null) bottomMaterial.SetFloat("_Smoothness", bottomSmoothness);
            BuildPerLane(RailVisualPrefab, "RailVisual", true, sideMaterial, bottomMaterial);
            slopeRailMaterial = sideMaterial;
        }
        BuildWallLine(judgeMaterial);
    }

    void OnDestroy()
    {
        if (instance == this) instance = null;
        if (hiddenRailMaterial != null) Destroy(hiddenRailMaterial);
    }

    //true: 斜面を暗くする（チュートリアルで底面だけを使う間）。真っ黒にすると斜面が無いように見えるので、かすかに見える暗さにする
    //  床は光を反射しないようにし、区切り線・判定ライン・壁の線は hiddenSlopeLineAlpha の濃さにする。キービームは消す
    //底面と斜面の境目の線は底面の端として残す
    public void SetSlopesHidden(bool hidden)
    {
        foreach (GameObject obj in slopeObjects)
        {
            if (obj.GetComponentInChildren<KeyBeamFade>(true) != null || !DimLines(obj, hidden)) obj.SetActive(!hidden);
        }

        if (slopeRailMaterial == null) return;
        if (hiddenRailMaterial == null)
        {
            hiddenRailMaterial = new Material(slopeRailMaterial) { name = "HiddenSlopeRail" };
            hiddenRailMaterial.SetFloat("_Smoothness", 0f);//光の反射で白っぽく見えないようにする
        }
        foreach (Renderer rail in slopeRails) rail.sharedMaterial = hidden ? hiddenRailMaterial : slopeRailMaterial;
    }

    //線のアルファを下げる（マテリアルは底面と共有しているので、斜面の線だけMaterialPropertyBlockで上書きする）
    //_BaseColorの無いマテリアルなら何もせずfalse（その場合は消す）
    bool DimLines(GameObject obj, bool dim)
    {
        Renderer[] renderers = obj.GetComponentsInChildren<Renderer>(true);
        if (renderers.Length == 0) return false;
        foreach (Renderer rend in renderers)
        {
            if (rend.sharedMaterial == null || !rend.sharedMaterial.HasProperty("_BaseColor")) return false;
        }
        foreach (Renderer rend in renderers)
        {
            if (!dim)
            {
                rend.SetPropertyBlock(null);
                continue;
            }
            Color color = rend.sharedMaterial.GetColor("_BaseColor");
            color.a *= hiddenSlopeLineAlpha;
            var block = new MaterialPropertyBlock();
            block.SetColor("_BaseColor", color);
            rend.SetPropertyBlock(block);
        }
        return true;
    }

    static bool IsSlopeLane(int lane)
    {
        return lane < LaneLayout.LeftFold || lane >= LaneLayout.RightFold;
    }

    //prefabのマテリアルを複製し、描く順番をqueueにする
    private Material QueuedMaterial(GameObject prefab, int queue)
    {
        Renderer rend = prefab != null ? prefab.GetComponentInChildren<Renderer>() : null;
        if (rend == null) return null;
        Material material = new Material(rend.sharedMaterial);
        material.renderQueue = queue;
        return material;
    }

    //床のマテリアルを複製し、アルファを設定の濃さにする（32枚の床で共有する）
    private Material RailMaterial()
    {
        Renderer rend = RailVisualPrefab != null ? RailVisualPrefab.GetComponentInChildren<Renderer>() : null;
        if (rend == null || !rend.sharedMaterial.HasProperty("_BaseColor")) return null;

        Material material = new Material(rend.sharedMaterial);
        Color color = material.GetColor("_BaseColor");
        color.a = PlaySession.LaneOpacityPercent / 100f;
        material.SetColor("_BaseColor", color);
        material.renderQueue = RailQueue;
        return material;
    }

    //小節線（NoteManagerが流す）のマテリアル。区切り線と同じ白い半透明で、裏表を気にしなくていいように両面を描く
    public Material CreateBarLineMaterial(float alpha)
    {
        Material material = LineMaterial(alpha);
        if (material != null) material.SetFloat("_Cull", 0f);
        return material;
    }

    //区切り線のマテリアル。白で、アルファをalphaにする
    //区切り線のプレハブのマテリアルは不透明なので、半透明の判定ラインのマテリアルを元にする
    private Material LineMaterial(float alpha)
    {
        Material material = QueuedMaterial(JudgeLinePrefab, LineQueue);
        if (material == null || !material.HasProperty("_BaseColor")) return null;
        material.SetColor("_BaseColor", new Color(1f, 1f, 1f, alpha));
        //光の反射は透明度に関係なく足されるので、薄くしても白く見えてしまう。反射を弱めて透明度どおりの濃さにする
        material.SetFloat("_Smoothness", 0f);
        return material;
    }


    //押したレーンに出るキービーム。判定ラインから奥へ、選曲画面で設定した長さ（判定ラインから壁までの長さに対する%）だけ伸ばす
    //壁より奥はノーツが見えない所なので、100%で壁まで届くようにする
    //プレハブの箱の代わりにBeamMeshの板を使い、BeamTextureで上に凸の形にして奥ほど薄くする
    private void BuildLights()
    {
        if (LaneLightPrefab == null || PlaySession.KeyBeamPercent <= 0) return;

        float length = LaneLayout.VisibleLength * PlaySession.KeyBeamPercent / 100f;
        Mesh mesh = BeamMesh();
        Material material = QueuedMaterial(LaneLightPrefab, BeamQueue);
        if (material != null)
        {
            material.SetTexture("_BaseMap", BeamTexture());
            material.SetFloat("_Cull", 0f);//板の裏表を気にしなくていいように両面を描く
        }

        for (int lane = 0; lane < InputManager.LaneCount; lane++)
        {
            GameObject obj = Instantiate(LaneLightPrefab, transform);
            obj.name = $"LaneLight_{lane}";
            if (IsSlopeLane(lane)) slopeObjects.Add(obj);
            obj.GetComponentInChildren<MeshFilter>().sharedMesh = mesh;
            if (material != null) obj.GetComponentInChildren<Renderer>().sharedMaterial = material;

            //板は幅・奥行きとも1なので、スケールがそのまま幅・長さになる。判定ライン(z=0)から奥(-z)へ伸ばす
            Transform t = obj.transform;
            t.localScale = new Vector3(LaneLayout.LaneWidth, t.localScale.y, length);
            t.localPosition = new Vector3(t.localPosition.x, t.localPosition.y, -length / 2f);
            PlaceOnSurface(t, lane + 0.5f, LaneLayout.SurfaceAngle(lane + 0.5f));//レーンの中心

            KeyBeamFade fade = obj.GetComponentInChildren<KeyBeamFade>();
            if (fade != null) fade.laneIndex = lane;
        }
    }

    //区切り線は、選曲画面で設定したレーン数ごとに引く（濃さも設定どおり）
    //底面と斜面の境目・斜面の端の線（境界線）は設定に関係なく、太く濃い線で常に引く
    private void BuildLines()
    {
        if (LaneLinePrefab == null) return;
        Material lineMaterial = LineMaterial(PlaySession.LineOpacityPercent / 100f * maxLineAlpha);
        Material boundaryMaterial = LineMaterial(boundaryLineAlpha);
        int division = PlaySession.LineOpacityPercent > 0 ? PlaySession.LineDivision : 0;//0なら区切り線を引かない

        //両端を含めてLaneCount+1本
        for (int i = 0; i <= InputManager.LaneCount; i++)
        {
            bool isBoundary = i == 0 || i == InputManager.LaneCount || i == LaneLayout.LeftFold || i == LaneLayout.RightFold;
            if (!isBoundary && (division <= 0 || i % division != 0)) continue;

            GameObject obj = Instantiate(LaneLinePrefab, transform);
            obj.name = $"LaneLine_{i}";
            if (i < LaneLayout.LeftFold || i > LaneLayout.RightFold) slopeObjects.Add(obj);//斜面の中と外側の端の線
            Material material = isBoundary ? boundaryMaterial : lineMaterial;
            if (material != null) obj.GetComponentInChildren<Renderer>().sharedMaterial = material;
            //壁より奥（ノーツが見えない所）には区切り線を引かない
            FitDepth(obj, LaneLayout.VisibleLength);
            //折れ目の線は両側の面の中間の角度にする
            PlaceOnSurface(obj.transform, i, LaneLayout.BoundaryAngle(i));

            //境界線は一番太く、thickLineIntervalごとの線はその次に太くする
            float lineScale = 1f;
            if (isBoundary) lineScale = foldLineScale;
            else if (thickLineInterval > 0 && i % thickLineInterval == 0) lineScale = thickLineScale;
            Vector3 scale = obj.transform.localScale;
            scale.x *= lineScale;
            obj.transform.localScale = scale;
        }
    }

    //判定ラインや床は1本のCubeだと折り曲げられないので、1レーンずつ並べて面に沿わせる
    //fitDepth: 奥行きをレーンの長さに合わせるか（床はtrue、判定ラインはfalse）
    //sideMaterial / bottomMaterial: 斜面・底面でプレハブのマテリアルの代わりに使うマテリアル（nullならプレハブのまま）
    private void BuildPerLane(GameObject prefab, string name, bool fitDepth, Material sideMaterial = null, Material bottomMaterial = null)
    {
        if (prefab == null) return;

        for (int lane = 0; lane < InputManager.LaneCount; lane++)
        {
            GameObject obj = Instantiate(prefab, transform);
            obj.name = $"{name}_{lane}";
            bool isBottom = lane >= LaneLayout.LeftFold && lane < LaneLayout.RightFold;
            Material material = isBottom ? bottomMaterial : sideMaterial;
            if (material != null) obj.GetComponentInChildren<Renderer>().sharedMaterial = material;
            FitWidth(obj, LaneLayout.LaneWidth);
            if (fitDepth) FitDepth(obj, LaneLayout.LaneLength);
            if (!isBottom)
            {
                //床は色を変え、それ以外（判定ライン）は消す
                if (prefab == RailVisualPrefab) slopeRails.Add(obj.GetComponentInChildren<Renderer>());
                else slopeObjects.Add(obj);
            }
            PlaceOnSurface(obj.transform, lane + 0.5f, LaneLayout.SurfaceAngle(lane + 0.5f));
        }
    }

    //見えるところと見えないところの境目（壁）の線。判定ラインのプレハブを細くして、判定ラインと同じく1レーンずつ並べる
    //壁が0%（全部見える）なら引かない
    private void BuildWallLine(Material material)
    {
        if (JudgeLinePrefab == null || PlaySession.WallPercent <= 0) return;

        for (int lane = 0; lane < InputManager.LaneCount; lane++)
        {
            GameObject obj = Instantiate(JudgeLinePrefab, transform);
            obj.name = $"WallLine_{lane}";
            if (IsSlopeLane(lane)) slopeObjects.Add(obj);
            if (material != null) obj.GetComponentInChildren<Renderer>().sharedMaterial = material;
            FitWidth(obj, LaneLayout.LaneWidth);
            obj.transform.localScale = Vector3.Scale(obj.transform.localScale, new Vector3(1, 1, wallLineThickness));
            obj.transform.localPosition += new Vector3(0, 0, -LaneLayout.VisibleLength);//PlaceOnSurfaceはzをそのまま使う
            PlaceOnSurface(obj.transform, lane + 0.5f, LaneLayout.SurfaceAngle(lane + 0.5f));
        }
    }

    //キービームの板。幅・奥行き1の上面だけの板で、高さはプレハブの箱の上面(y=0.5)に合わせる
    //uはレーンの幅方向(0〜1)、vは判定ライン側が0で奥が1
    private static Mesh BeamMesh()
    {
        var mesh = new Mesh { name = "KeyBeam" };
        mesh.vertices = new[]
        {
            new Vector3(-0.5f, 0.5f, 0.5f), new Vector3(0.5f, 0.5f, 0.5f),//判定ライン側
            new Vector3(-0.5f, 0.5f, -0.5f), new Vector3(0.5f, 0.5f, -0.5f),//奥側
        };
        mesh.uv = new[] { new Vector2(0, 0), new Vector2(1, 0), new Vector2(0, 1), new Vector2(1, 1) };
        mesh.triangles = new[] { 0, 1, 3, 0, 3, 2 };
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        return mesh;
    }

    //キービームの形。レーンの中央ほど奥まで伸びる上に凸の放物線で、奥ほど薄くなる白いテクスチャ
    //  奥  ...▁▂▃▂▁...
    //      .▂▄▆▇▆▄▂.
    //  手前█████████   ← 判定ライン
    private static Texture2D BeamTexture()
    {
        const int width = 32, height = 64;
        var texture = new Texture2D(width, height, TextureFormat.RGBA32, false) { name = "KeyBeam", wrapMode = TextureWrapMode.Clamp };
        var pixels = new Color[width * height];
        for (int y = 0; y < height; y++)
        {
            float v = (y + 0.5f) / height;
            for (int x = 0; x < width; x++)
            {
                float u = (x + 0.5f) / width;
                float reach = 1f - (2f * u - 1f) * (2f * u - 1f);//この位置でビームが届く奥行き（中央で1、両端で0）
                float alpha = reach > 0f ? Mathf.Clamp01(1f - v / reach) : 0f;
                pixels[y * width + x] = new Color(1f, 1f, 1f, alpha);
            }
        }
        texture.SetPixels(pixels);
        texture.Apply();
        return texture;
    }

    //レーン位置laneの面上に置き、angle度だけ傾ける
    //プレハブのyは面からの高さ（面に垂直な方向）、zはそのまま使う
    private void PlaceOnSurface(Transform t, float lane, float angle)
    {
        Quaternion tilt = Quaternion.Euler(0, 0, angle);
        Vector3 prefabPos = t.localPosition;
        Vector3 pos = LaneLayout.SurfacePos(lane) + tilt * Vector3.up * prefabPos.y;
        pos.z = prefabPos.z;
        t.localPosition = pos;
        t.localRotation = tilt * t.localRotation;
    }

    //見た目の横幅がwidthになるようにx方向のスケールを合わせる
    private void FitWidth(GameObject obj, float width)
    {
        Renderer rend = obj.GetComponentInChildren<Renderer>();
        if (rend == null) return;

        //今の見た目の横幅（スケールや親の影響を含むワールド座標）
        float currentWidth = rend.bounds.size.x;
        if (currentWidth <= 0) return;

        //「目標の幅 ÷ 今の幅」倍にするので、プレハブの元の大きさに関係なく目標の幅になる
        Vector3 scale = obj.transform.localScale;
        scale.x *= width / currentWidth;
        obj.transform.localScale = scale;
    }

    //奥の端が z = -farLength になるようにz方向のスケールと位置を合わせる（手前の端はプレハブのまま）
    //FitWidthと同じくboundsで測るので、傾ける前に呼ぶ
    private void FitDepth(GameObject obj, float farLength)
    {
        Renderer rend = obj.GetComponentInChildren<Renderer>();
        if (rend == null) return;

        Bounds bounds = rend.bounds;
        if (bounds.size.z <= 0) return;
        float nearZ = bounds.max.z;//手前の端（ノーツは-z側から流れてくるので、+z側が手前）

        Vector3 scale = obj.transform.localScale;
        scale.z *= (nearZ + farLength) / bounds.size.z;
        obj.transform.localScale = scale;

        //スケールを変えると中心を基準に伸び縮みして手前の端がずれるので、元の位置に戻す
        obj.transform.localPosition += new Vector3(0, 0, nearZ - rend.bounds.max.z);
    }
}
