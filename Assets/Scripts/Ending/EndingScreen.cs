using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using static UIUtil;

//エンディング画面。UIはすべてこのスクリプトがコードで生成する（シーンにはこのコンポーネントだけあればよい）
//
//  規定の曲数を遊び終えた後、Resultシーンの次に出す。「THANK YOU FOR PLAYING!」をフェードインで出し、
//  autoSeconds秒たつか何かキーが押されたら、文字を薄くしてオープニングへ戻る（オープニングも白背景なので、そのままつながる）
public class EndingScreen : MonoBehaviour
{
    static readonly Color TextColor = new Color(0.12f, 0.13f, 0.16f);

    [SerializeField] private float fadeInSeconds = 0.8f;
    [SerializeField] private float fadeOutSeconds = 0.6f;
    [SerializeField] private float autoSeconds = 5f;//何も押されなくても、この秒数でオープニングへ戻る
    [SerializeField] private float inputLockSeconds = 1f;//リザルトで押したキーで、すぐ飛ばされないようにする

    private Font font;

    void Start()
    {
        font = CreateFont();
        StartCoroutine(Run(BuildCanvas()));
    }

    IEnumerator Run(CanvasGroup content)
    {
        float startTime = Time.time;
        yield return Animate(fadeInSeconds, t => content.alpha = EaseOut(t));

        while (Time.time - startTime < autoSeconds)
        {
            if (Time.time - startTime >= inputLockSeconds && AnyPressed()) break;
            yield return null;
        }

        yield return Animate(fadeOutSeconds, t => content.alpha = 1f - t);
        SceneLoader.ChangeScene(GameScene.Opening);
    }

    bool AnyPressed()
    {
        if (InputManager.instance != null && InputManager.instance.AnyDown(0, InputManager.LaneCount)) return true;
        return Input.anyKeyDown;
    }

    // ===== 画面の組み立て =====
    CanvasGroup BuildCanvas()
    {
        RectTransform canvasRect = CreateCanvas("EndingCanvas", 0, 0.5f);
        Stretch(CreateImage("Background", canvasRect, Color.white).rectTransform);

        RectTransform content = CreateRect("Content", canvasRect);
        Stretch(content);
        var group = content.gameObject.AddComponent<CanvasGroup>();
        group.alpha = 0f;

        Text thanks = UIUtil.CreateText("Thanks", content, font, TextColor, "THANK YOU FOR PLAYING!", 120, TextAnchor.MiddleCenter, FontStyle.Bold);
        Place(thanks.rectTransform, Vector2.zero, new Vector2(1700f, 160f));
        return group;
    }
}
