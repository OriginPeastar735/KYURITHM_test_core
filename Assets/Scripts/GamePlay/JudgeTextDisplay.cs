using System.Collections;
using TMPro;
using UnityEngine;

//ノーツが判定されたときに、判定ライン上のノーツの中央の上に判定（PRECISE+など）を表示する
//シーンには置かず、GameManagerが作る（フォントはコンボ表示と同じものを使う）
public class JudgeTextDisplay : MonoBehaviour
{
    public float height = 0.25f;//フィールドの面から文字までの高さ（真上方向）
    public float riseDistance = 0.1f;//表示している間に上へ動く距離
    public float showSeconds = 0.4f;//表示してから消えるまでの秒数
    public float fontSize = 1f;

    public Color precisePlusColor = new Color(1f, 0.85f, 0.3f);
    public Color preciseColor = new Color(1f, 0.6f, 0.2f);
    public Color approxColor = new Color(0.4f, 0.9f, 0.5f);
    public Color missColor = new Color(0.6f, 0.6f, 0.6f);

    private TMP_FontAsset font;

    public static JudgeTextDisplay Create(TMP_FontAsset font)
    {
        JudgeTextDisplay display = new GameObject("JudgeTextDisplay").AddComponent<JudgeTextDisplay>();
        display.font = font;
        return display;
    }

    void OnEnable()
    {
        JudgeManager.Judged += Show;
    }

    void OnDisable()
    {
        JudgeManager.Judged -= Show;
    }

    void Show(JudgeResult result, float centerLane)
    {
        GameObject obj = new GameObject($"JudgeText_{result}");
        obj.transform.SetParent(transform, false);

        TextMeshPro text = obj.AddComponent<TextMeshPro>();
        if (font != null) text.font = font;
        text.fontSize = fontSize;
        text.alignment = TextAlignmentOptions.Center;
        text.textWrappingMode = TextWrappingModes.NoWrap;
        text.text = Label(result);
        text.color = ColorOf(result);

        //傾斜の上でも文字が横にずれないよう、面に垂直ではなく真上に浮かせる
        Vector3 basePos = LaneLayout.SurfacePos(centerLane) + Vector3.up * height;
        basePos.z = EffectManager.instance != null ? EffectManager.instance.judgeLineZ : 0f;
        StartCoroutine(Animate(text, basePos));
    }

    //少しずつ上に動かしながら薄くして消す。文字は常にカメラの方を向ける
    IEnumerator Animate(TextMeshPro text, Vector3 basePos)
    {
        Camera cam = Camera.main;
        float startTime = Time.time;
        while (true)
        {
            float t = (Time.time - startTime) / showSeconds;
            if (t >= 1f) break;

            text.transform.position = basePos + Vector3.up * riseDistance * t;
            if (cam != null) text.transform.rotation = cam.transform.rotation;
            text.alpha = 1f - t * t;
            yield return null;
        }
        Destroy(text.gameObject);
    }

    static string Label(JudgeResult result)
    {
        switch (result)
        {
            case JudgeResult.PrecisePlus: return "PRECISE+";
            case JudgeResult.Precise: return "PRECISE";
            case JudgeResult.Approx: return "APPROX";
            default: return "MISS";
        }
    }

    Color ColorOf(JudgeResult result)
    {
        switch (result)
        {
            case JudgeResult.PrecisePlus: return precisePlusColor;
            case JudgeResult.Precise: return preciseColor;
            case JudgeResult.Approx: return approxColor;
            default: return missColor;
        }
    }
}
