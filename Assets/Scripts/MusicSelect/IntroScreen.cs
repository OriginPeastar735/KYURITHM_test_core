using System.Collections;
using UnityEngine;
using UnityEngine.UI;

//楽曲紹介画面の進行。画面の部品はMusicSelectViewが作り、このコンポーネントを付けたCanvasはシーンが変わっても残る
//
//  1. ジャケットと「作曲」などの見出しを出す（fadeIn秒）
//  2. 曲名・作曲者・譜面制作者を1文字ずつ出し、同時にジャケット裏の正方形を右下へずらして見せる（reveal秒。文字数に関係なくこの時間で出し切る）
//  3. そのまま見せる（hold秒）。この間に紹介画面の下でプレイ画面に切り替えて譜面も読み込んでおく
//  4. 紹介画面を薄くしてプレイ画面を見せ、曲を再生する（fadeOut秒）
public class IntroScreen : MonoBehaviour
{
    private const string HiddenColorTag = "<color=#00000000>";//まだ出していない文字を透明にする（文字の位置がずれないように）

    public void Play(CanvasGroup group, RectTransform content, Text[] typingTexts, RectTransform jacketBack, Vector2 jacketBackOffset,
                     float fadeIn, float reveal, float hold, float fadeOut)
    {
        StartCoroutine(Run(group, content, typingTexts, jacketBack, jacketBackOffset, fadeIn, reveal, hold, fadeOut));
    }

    IEnumerator Run(CanvasGroup group, RectTransform content, Text[] typingTexts, RectTransform jacketBack, Vector2 jacketBackOffset,
                    float fadeIn, float reveal, float hold, float fadeOut)
    {
        PlaySession.WaitingForIntro = true;//プレイ画面に切り替わっても、紹介が終わるまで曲を再生しない
        AsyncOperation loading = SceneLoader.PreloadScene(GameScene.Play);

        string[] fullTexts = new string[typingTexts.Length];
        for (int i = 0; i < typingTexts.Length; i++)
        {
            fullTexts[i] = typingTexts[i].text;
            typingTexts[i].text = "";
        }

        //1. ジャケットと見出し（裏の正方形はジャケットと同じ位置にあるので、まだ隠れている）
        yield return MusicSelectView.Animate(fadeIn, t =>
        {
            group.alpha = t;
            content.localScale = Vector3.one * Mathf.Lerp(0.96f, 1f, MusicSelectView.EaseOut(t));
        });

        //2. 文字送りと、裏の正方形を右下へずらす
        Vector2 backStart = jacketBack.anchoredPosition;
        yield return MusicSelectView.Animate(reveal, t =>
        {
            for (int i = 0; i < typingTexts.Length; i++)
            {
                //それぞれの文字数に合わせて進めるので、長さに関係なく同時に出し終わる
                typingTexts[i].text = Partial(fullTexts[i], Mathf.CeilToInt(fullTexts[i].Length * t));
            }
            jacketBack.anchoredPosition = backStart + jacketBackOffset * MusicSelectView.EaseOut(t);
        });
        float holdEnd = Time.time + hold;

        //3. 見せている間に、紹介画面の下でプレイ画面に切り替える（譜面の読み込みもここで行われる）
        while (loading.progress < 0.9f) yield return null;
        loading.allowSceneActivation = true;
        while (!loading.isDone) yield return null;
        while (Time.time < holdEnd) yield return null;

        //4. 曲の再生を始め、紹介画面を薄くしてプレイ画面を見せる
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
