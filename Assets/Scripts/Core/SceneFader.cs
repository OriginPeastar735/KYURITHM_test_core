using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using static UIUtil;

//シーンをぱっと切り替えずに、画面を色で覆ってから切り替え、次のシーンの上で覆いを外す
//覆いはシーンが変わっても残るCanvasに作り、外し終えたら消す（次のシーンの読み込みは覆っている間に済ませる）
public static class SceneFader
{
    //シーンが変わっても残る側で演出を回すための入れ物
    class Host : MonoBehaviour { }

    public static void FadeTo(GameScene next, Color color, float coverSeconds = 0.5f, float revealSeconds = 0.5f)
    {
        RectTransform canvas = CreateCanvas("SceneFader", 200, 0f);//どの画面のUIよりも手前に描く
        Object.DontDestroyOnLoad(canvas.gameObject);
        Image cover = CreateImage("Cover", canvas, new Color(color.r, color.g, color.b, 0f));
        Stretch(cover.rectTransform);
        canvas.gameObject.AddComponent<Host>().StartCoroutine(Run(next, canvas, cover, color, coverSeconds, revealSeconds));
    }

    static IEnumerator Run(GameScene next, RectTransform canvas, Image cover, Color color, float coverSeconds, float revealSeconds)
    {
        AsyncOperation loading = SceneLoader.PreloadScene(next);
        yield return Animate(coverSeconds, t => cover.color = new Color(color.r, color.g, color.b, t * t));

        while (loading.progress < 0.9f) yield return null;
        loading.allowSceneActivation = true;
        while (!loading.isDone) yield return null;

        yield return Animate(revealSeconds, t => cover.color = new Color(color.r, color.g, color.b, 1f - EaseOut(t)));
        Object.Destroy(canvas.gameObject);
    }
}
