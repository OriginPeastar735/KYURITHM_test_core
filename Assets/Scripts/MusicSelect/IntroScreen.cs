using System.Collections;
using UnityEngine;
using UnityEngine.UI;

//楽曲紹介画面の進行。画面の部品はMusicSelectViewが作り、このコンポーネントを付けたCanvasはシーンが変わっても残る
//
//  ジャケットが出る → 曲名・作曲者・譜面制作者を1文字ずつ表示
//  （その間に裏でプレイ画面を読み込み、紹介画面の下でプレイ画面に切り替えて譜面も読み込んでおく）
//  → 表示時間が終わったら紹介画面を薄くしてプレイ画面を見せ、曲を再生する
public class IntroScreen : MonoBehaviour
{
    private const string HiddenColorTag = "<color=#00000000>";//まだ出していない文字を透明にする（文字の位置がずれないように）

    public void Play(CanvasGroup group, RectTransform content, Text[] typingTexts, float fadeIn, float hold, float fadeOut, float charInterval)
    {
        StartCoroutine(Run(group, content, typingTexts, fadeIn, hold, fadeOut, charInterval));
    }

    IEnumerator Run(CanvasGroup group, RectTransform content, Text[] typingTexts, float fadeIn, float hold, float fadeOut, float charInterval)
    {
        PlaySession.WaitingForIntro = true;//プレイ画面に切り替わっても、紹介が終わるまで曲を再生しない
        AsyncOperation loading = SceneLoader.PreloadScene(GameScene.Play);

        string[] fullTexts = new string[typingTexts.Length];
        for (int i = 0; i < typingTexts.Length; i++)
        {
            fullTexts[i] = typingTexts[i].text;
            typingTexts[i].text = "";
        }

        //ジャケットを出す
        yield return MusicSelectView.Animate(fadeIn, t =>
        {
            group.alpha = t;
            content.localScale = Vector3.one * Mathf.Lerp(0.96f, 1f, MusicSelectView.EaseOut(t));
        });
        float holdEnd = Time.time + hold;

        //曲名・作曲者・譜面制作者を1文字ずつ出す
        int maxLength = 0;
        foreach (string text in fullTexts) maxLength = Mathf.Max(maxLength, text.Length);
        for (int count = 1; count <= maxLength; count++)
        {
            for (int i = 0; i < typingTexts.Length; i++) typingTexts[i].text = Partial(fullTexts[i], count);
            yield return new WaitForSeconds(charInterval);
        }

        //読み込みが終わったら、紹介画面の下でプレイ画面に切り替える（譜面の読み込みもここで行われる）
        while (loading.progress < 0.9f) yield return null;
        loading.allowSceneActivation = true;
        while (!loading.isDone) yield return null;

        //表示時間が残っていれば待つ
        while (Time.time < holdEnd) yield return null;

        //曲の再生を始め、紹介画面を薄くしてプレイ画面を見せる
        PlaySession.WaitingForIntro = false;
        yield return MusicSelectView.Animate(fadeOut, t => group.alpha = 1f - t);
        Destroy(gameObject);
    }

    void OnDestroy()
    {
        PlaySession.WaitingForIntro = false;//途中で消えても曲が始まらなくならないように
    }

    //先頭からcount文字だけ見せる。残りは透明にして場所だけ取っておく
    static string Partial(string text, int count)
    {
        if (count >= text.Length) return text;
        return text.Substring(0, count) + HiddenColorTag + text.Substring(count) + "</color>";
    }
}
