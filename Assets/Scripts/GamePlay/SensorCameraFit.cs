using UnityEngine;

//プレイ画面のカメラを、判定ラインが筐体のセンサーの位置（選曲画面のボタン表示と同じ位置）に重なるように置く
//
//判定ラインはz=0の面（カメラの方を向いた縦の面）にあるので、カメラを傾けずに-z方向へまっすぐ向ければ、
//判定ラインは形を保ったまま拡大縮小されて映る。斜面の角度をセンサーと同じ45°にしておけば、
//底面の幅を底面センサーの幅に、底面を画面の下端に合わせるだけで、斜面も斜面センサーに重なる。
//レーンの消失点を画面の上の方に置くため、カメラは傾けずに映す範囲だけを上にずらす（レンズシフト）
//
//judgeLineOffsetを0より大きくすると、レーンの見え方はそのままでカメラを後ろへ下げ、判定ラインを奥に見せる
//（判定ラインはセンサーより小さく、画面の下端より上に映る）
//
//LaneFieldBuilderがメインカメラに付ける。画面の大きさが変わったら計算し直す
[RequireComponent(typeof(Camera))]
public class SensorCameraFit : MonoBehaviour
{
    [Range(0.5f, 1f)] public float vanishingPointHeight = 0.95f;//レーンの消失点の高さ（画面の下端0〜上端1）
    public float distance = 4f;//判定ラインがセンサーに重なるとき(judgeLineOffset=0)の、カメラから判定ラインまでの距離。小さいほど遠近感が強い
    [Range(0f, 0.3f)] public float judgeLineOffset = 0.1f;//判定ラインの底面の高さ（画面の高さに対する割合）。0で画面の下端（センサーに重なる）

    private Camera cam;
    private int lastWidth, lastHeight;
    private float lastVanishing, lastDistance, lastOffset;

    void Awake()
    {
        cam = GetComponent<Camera>();

        //映す範囲をずらすとScreen Space - CameraのCanvasがずれるので、画面に直接描く方式に変える
        foreach (Canvas canvas in FindObjectsByType<Canvas>(FindObjectsSortMode.None))
        {
            if (canvas.renderMode == RenderMode.ScreenSpaceCamera && canvas.worldCamera == cam) canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        }
        Fit();
    }

    void LateUpdate()
    {
        if (cam.pixelWidth == lastWidth && cam.pixelHeight == lastHeight && vanishingPointHeight == lastVanishing && distance == lastDistance && judgeLineOffset == lastOffset) return;
        Fit();
    }

    void Fit()
    {
        lastWidth = cam.pixelWidth;
        lastHeight = cam.pixelHeight;
        lastVanishing = vanishingPointHeight;
        lastDistance = distance;
        lastOffset = judgeLineOffset;
        float width = lastWidth;
        float height = lastHeight;

        //z=0の面で、1単位が画面の何ピクセルになるか。判定ラインの底面が底面センサーと同じ幅になるようにする
        float bottomLength = (LaneLayout.RightFold - LaneLayout.LeftFold) * LaneLayout.LaneWidth;
        float pixelsPerUnit = width * SensorLayout.BottomWidthRatio / bottomLength;

        //底面の中央の正面に置く。高さは、カメラの正面（消失点）から画面の下端までの分
        float centerX = LaneLayout.SurfacePos((LaneLayout.LeftFold + LaneLayout.RightFold) / 2f).x;
        float cameraY = vanishingPointHeight * height / pixelsPerUnit;

        //ここまででz=0の面がセンサーに重なる。カメラを後ろへ下げると、判定ラインは消失点に向かって小さくなり、
        //画面の高さに対する割合で vanishingPointHeight * (1 - distance / 判定ラインまでの距離) の高さに映る。
        //これがjudgeLineOffsetになる距離まで下げる（レーンの見え方は、レーンに沿って下がるだけなので変わらない）
        float offset = Mathf.Clamp(judgeLineOffset, 0f, vanishingPointHeight * 0.9f);
        float judgeDistance = distance * vanishingPointHeight / (vanishingPointHeight - offset);
        transform.SetPositionAndRotation(new Vector3(centerX, cameraY, judgeDistance), Quaternion.Euler(0f, 180f, 0f));

        //z=0の面で映す範囲（カメラの正面からのずれ）を、ニアクリップ面の上の大きさに直して射影行列を作る
        float near = cam.nearClipPlane;
        float toNear = near / distance / pixelsPerUnit;
        float left = -width / 2f * toNear;
        float bottom = -vanishingPointHeight * height * toNear;
        float top = (1f - vanishingPointHeight) * height * toNear;
        cam.projectionMatrix = Matrix4x4.Frustum(left, -left, bottom, top, near, cam.farClipPlane);
    }
}
