using TMPro;
using UnityEngine;

//tap・slide始点がPRECISE/APPROXだったときに、早かったか(FAST)遅かったか(LATE)を表示する
//シーンには置かず、GameManagerがコンボ表示を複製して作る（選曲画面の設定がOFFなら作らない）
public class FastLateDisplay : MonoBehaviour
{
    public float showSeconds = 0.4f;//表示してから消えるまでの秒数
    public Color fastColor = new Color(0.3f, 0.7f, 1f);
    public Color lateColor = new Color(1f, 0.45f, 0.3f);

    private TextMeshProUGUI text;
    private float shownTime;

    //templateと同じ見た目のテキストを、templateからoffsetだけずらした位置に作る
    public static FastLateDisplay Create(TextMeshProUGUI template, Vector2 offset, float fontSize)
    {
        TextMeshProUGUI text = Instantiate(template, template.transform.parent);
        text.name = "FastLateText";
        text.rectTransform.anchoredPosition += offset;
        text.fontSize = fontSize;
        text.text = "";

        FastLateDisplay display = text.gameObject.AddComponent<FastLateDisplay>();
        display.text = text;
        return display;
    }

    void OnEnable()
    {
        JudgeManager.FastLate += Show;
    }

    void OnDisable()
    {
        JudgeManager.FastLate -= Show;
    }

    //diffSecが負なら早押し
    void Show(float diffSec)
    {
        text.text = diffSec < 0 ? "FAST" : "LATE";
        text.color = diffSec < 0 ? fastColor : lateColor;
        shownTime = Time.time;
    }

    //表示してからshowSecondsかけて薄くして消す
    void Update()
    {
        if (text == null || text.text.Length == 0) return;

        float t = (Time.time - shownTime) / showSeconds;
        if (t >= 1f)
        {
            text.text = "";
            return;
        }
        text.alpha = 1f - t * t;
    }
}
