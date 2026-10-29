using System;
using System.Collections;
using UnityEngine;
using UnityEngine.UI;

//UIをコードで組み立てる画面（オープニング・選曲・楽曲紹介・エンディングなど）で共通に使う処理
//各画面のスクリプトでは using static UIUtil; を書くと、UIUtil. を付けずに呼べる
public static class UIUtil
{
    //Canvasの基準解像度
    public const float RefWidth = 1920f;
    public const float RefHeight = 1080f;

    // ===== アニメーション =====
    //duration秒かけてstep(0→1)を呼び続ける
    public static IEnumerator Animate(float duration, Action<float> step)
    {
        for (float time = 0f; time < duration; time += Time.deltaTime)
        {
            step(time / duration);
            yield return null;
        }
        step(1f);
    }

    //最初は速く、最後はゆっくり止まる動き
    public static float EaseOut(float t)
    {
        return 1f - (1f - t) * (1f - t);
    }

    // ===== Canvas・フォント =====
    //画面いっぱいのCanvasを作る。sortingOrderが大きいほど手前に描かれる
    //matchWidthOrHeight: 0なら横幅基準、1なら高さ基準で拡大縮小する
    public static RectTransform CreateCanvas(string name, int sortingOrder, float matchWidthOrHeight)
    {
        var canvasObj = new GameObject(name, typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
        var canvas = canvasObj.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = sortingOrder;
        var scaler = canvasObj.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(RefWidth, RefHeight);
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
        scaler.matchWidthOrHeight = matchWidthOrHeight;
        return (RectTransform)canvasObj.transform;
    }

    //日本語を表示するため、WindowsのフォントをレガシーTextで使う
    public static Font CreateFont()
    {
        return Font.CreateDynamicFontFromOSFont(new[] { "Yu Gothic UI", "Meiryo UI", "Meiryo", "MS Gothic", "Arial" }, 32);
    }

    // ===== UI部品 =====
    public static RectTransform CreateRect(string name, Transform parent)
    {
        var rect = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
        rect.SetParent(parent, false);
        return rect;
    }

    public static Image CreateImage(string name, Transform parent, Color color, Sprite sprite = null)
    {
        var image = CreateRect(name, parent).gameObject.AddComponent<Image>();
        image.sprite = sprite;
        image.color = color;
        image.raycastTarget = false;
        return image;
    }

    //はみ出しても折り返さない1行のテキスト
    public static Text CreateText(string name, Transform parent, Font font, Color color, string text, int size, TextAnchor alignment, FontStyle style = FontStyle.Normal)
    {
        var t = CreateRect(name, parent).gameObject.AddComponent<Text>();
        t.font = font;
        t.text = text;
        t.fontSize = size;
        t.fontStyle = style;
        t.alignment = alignment;
        t.color = color;
        t.horizontalOverflow = HorizontalWrapMode.Overflow;
        t.verticalOverflow = VerticalWrapMode.Overflow;
        t.raycastTarget = false;
        return t;
    }

    // ===== 配置 =====
    //画面中央を基準に、位置と大きさを決める
    public static void Place(RectTransform rect, Vector2 position, Vector2 size)
    {
        var center = new Vector2(0.5f, 0.5f);
        Place(rect, center, center, center, position, size);
    }

    public static void Place(RectTransform rect, Vector2 anchorMin, Vector2 anchorMax, Vector2 pivot, Vector2 position, Vector2 size)
    {
        rect.anchorMin = anchorMin;
        rect.anchorMax = anchorMax;
        rect.pivot = pivot;
        rect.anchoredPosition = position;
        rect.sizeDelta = size;
    }

    //親いっぱいに広げる（left, bottom, right, topは内側への余白）
    public static void Stretch(RectTransform rect, float left = 0f, float bottom = 0f, float right = 0f, float top = 0f)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.offsetMin = new Vector2(left, bottom);
        rect.offsetMax = new Vector2(-right, -top);
    }
}
