using UnityEngine;
using UnityEngine.UI;
using static UIUtil;

//運営用の操作（プレイスキップ・センサーリセットなど）を実行したことを知らせる小さなポップアップ。画面の上の真ん中に出て、少しすると消える
//シーンが変わっても残るCanvasに出すので、シーンを切り替える直前に出してもよい。続けて出すと前のものを置き換える
public class Notice : MonoBehaviour
{
    const float ShowSeconds = 2.5f;//はっきり見せる時間
    const float FadeSeconds = 0.5f;

    static Notice current;

    private CanvasGroup group;
    private Text text;
    private float shownTime;

    public static void Show(string message)
    {
        if (current == null)
        {
            RectTransform canvas = CreateCanvas("Notice", 400, 0f);//シーンの切り替えの覆い(200)や設定画面(300)よりも手前に描く
            DontDestroyOnLoad(canvas.gameObject);
            current = canvas.gameObject.AddComponent<Notice>();
            current.Build(canvas);
        }
        current.text.text = message;
        current.shownTime = Time.unscaledTime;
        current.group.alpha = 1f;
        Debug.Log("お知らせ: " + message);
    }

    void Build(RectTransform canvas)
    {
        var panel = CreateImage("Panel", canvas, new Color(0.05f, 0.06f, 0.12f, 0.92f)).rectTransform;
        Place(panel, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -130f), new Vector2(900f, 90f));
        group = panel.gameObject.AddComponent<CanvasGroup>();
        var line = CreateImage("Line", panel, new Color(1f, 0.72f, 0.2f)).rectTransform;
        Place(line, Vector2.zero, new Vector2(0f, 1f), new Vector2(0f, 0.5f), Vector2.zero, new Vector2(10f, 0f));
        text = CreateText("Message", panel, CreateFont(), Color.white, "", 40, TextAnchor.MiddleCenter, FontStyle.Bold);
        Stretch(text.rectTransform, 20f, 0f, 10f, 0f);
    }

    void Update()
    {
        float elapsed = Time.unscaledTime - shownTime;
        group.alpha = 1f - Mathf.Clamp01((elapsed - ShowSeconds) / FadeSeconds);
    }
}
