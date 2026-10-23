using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

//カード1枚に表示する内容。楽曲カード・設定カードのどちらもこれで表す
public class CardInfo
{
    public Color background = Color.gray;
    public string topSmall = "";//上の小さい文字（難易度名など）
    public string topLarge = "";//上の大きい文字（レベルなど）
    public Color squareColor = Color.gray;//真ん中の正方形（ジャケット）
    public Color squareBorder = Color.clear;//正方形の枠の色（透明なら枠なし）
    public Sprite squareSprite;
    public string squareText = "";
    public int squareTextSize = 40;
    public string title = "";
    public int titleSize = 0;//0なら曲名用の大きさ
    public bool titleInBottomCenter = false;//true: 曲名欄をジャケットの下端とカードの下端の真ん中に置く（ランダム選曲カード用）
    public string subtitle = "";
    public int subtitleSize = 0;//0なら作曲者名用の大きさ
    public string rank;//nullなら表示しない
    public string highScore;//nullなら表示しない

    public static CardInfo FromSong(SongData song, int difficulty)
    {
        DifficultyData diff = song.difficulties[difficulty];
        Sprite sprite = string.IsNullOrEmpty(song.jacketPath) ? null : Resources.Load<Sprite>(song.jacketPath);
        return new CardInfo
        {
            background = DifficultyInfo.Colors[difficulty],
            topSmall = DifficultyInfo.Names[difficulty],
            topLarge = $"Lv.{diff.level}",
            squareSprite = sprite,
            squareColor = sprite != null ? Color.white : Color.HSVToRGB(song.id * 0.13f % 1f, 0.45f, 0.85f),
            squareText = sprite != null ? "" : "JACKET\n(仮)",
            title = song.title,
            subtitle = song.artist,
            rank = "RANK -",//ランク基準が決まったら差し替える
            highScore = diff.highScore > 0 ? $"HIGH SCORE\n{diff.highScore:N0}" : "HIGH SCORE\n未プレイ",
        };
    }
}

//選曲画面の表示。UIはすべてこのスクリプトがコードで生成する（シーンにはMusicSelectManagerだけあればよい）
//
//筐体の画面下部（正面から見た図）。底面センサーの長さ2に対して斜面センサーは長さ1で45°傾いている
//  ┌──────────────────────────────┐
//  │＼                          ／│
//  │隠＼                      ／隠│  ← 斜面センサーで隠れて見えない三角形
//  └───┴──────────────────┴───┘
//  1/√2          2           1/√2   ← 画面下辺の比
public class MusicSelectView : MonoBehaviour
{
    const float RefWidth = 1920f;
    const float RefHeight = 1080f;
    static readonly float SlopeLength = RefWidth / (2f + Mathf.Sqrt(2f));//斜面センサー1本分の長さ（キャンバス座標）
    static readonly float SlopeSpan = SlopeLength / Mathf.Sqrt(2f);//斜面が画面下辺・側辺を占める長さ
    static readonly float BottomWidth = SlopeLength * 2f;//底面センサーの長さ

    //カード。選択中のサイズで作り、選択されていないカードは縮小して表示する
    public const float CardWidth = 540f;
    public const float CardHeight = 712f;
    const float CardPadding = 6f;
    const float JacketSize = 420f;
    const float JacketTop = 112f;//カードの上端からジャケットの上端まで
    const float TitleTop = 542f;
    const float TitleHeight = 40f;
    //曲名はひらがな22字が1行に収まる文字サイズ（全角1字の幅 ≒ 文字サイズ）。それより長い曲名は縮小する
    static readonly int TitleFontSize = Mathf.FloorToInt((CardWidth - CardPadding * 2f) / 22f);
    const int SubtitleFontSize = 20;

    //カードの配置。中央からの距離(0,1,2,3枚目)ごとのx座標
    const int VisibleRange = 3;//中央から左右に何枚まで置くか（3枚目は画面外で、スクロール時に入ってくる）
    static readonly float[] cardX = { 0f, 441f, 764f, 1086f };
    const float SelectedY = 20f;
    const float SideY = 40f;
    const float SideScale = 0.56f;//選択されていないカードの大きさ（選択中のカードに対する倍率）
    const float ScrollSpeed = 20f;//スクロールの追従の速さ

    //操作エリアの光の太さ
    const float BottomGlowHeight = 70f;
    const float SlopeGlowHeight = 50f;

    [Tooltip("斜面センサーで隠れる部分を暗く表示する（センサー無しのモニターでテストするとき用）")]
    public bool showHiddenArea = true;

    private Font font;
    private Sprite gradient;
    private RectTransform root;
    private Text playerText, trackText, timerText, infoText;
    private readonly List<SongCard> cards = new List<SongCard>();
    private readonly ZoneGlow[] glows = new ZoneGlow[MusicSelectManager.ZoneCount];
    private SlopeVisual leftSlope, rightSlope;

    //ソートのポップアップ
    private RectTransform popup;
    private CanvasGroup popupGroup;
    private readonly List<Text> popupItems = new List<Text>();
    private Image popupHighlight;
    private bool popupVisible;

    private RectTransform cardLayer;
    private CanvasGroup cardGroup;
    public bool IsTransitioning { get; private set; }//画面切り替えのアニメーション中

    private Func<int, CardInfo> itemAt;
    private float scroll;//表示上の位置。targetに向かってなめらかに動く
    private int target;
    private float decideFlash;

    // ===== Managerから呼ぶ =====
    //itemAt: 位置pに表示するカードの内容。nullを返すとその位置にはカードを出さない
    public void Build(Func<int, CardInfo> itemAt)
    {
        this.itemAt = itemAt;

        //日本語を表示できるTMPフォントがまだ無いので、WindowsのフォントをレガシーTextで使う
        font = Font.CreateDynamicFontFromOSFont(new[] { "Yu Gothic UI", "Meiryo UI", "Meiryo", "MS Gothic", "Arial" }, 32);
        gradient = CreateGradientSprite();

        root = CreateCanvas("MusicSelectCanvas", 0);

        Stretch(CreateImage("Background", root, new Color(0.05f, 0.06f, 0.12f)).rectTransform);

        cardLayer = CreateRect("Cards", root);
        Stretch(cardLayer);
        cardGroup = cardLayer.gameObject.AddComponent<CanvasGroup>();
        for (int i = 0; i < VisibleRange * 2 + 1; i++) cards.Add(new SongCard(this, cardLayer));
        BuildEdgeShade();

        BuildGlows();
        BuildHeader();
        BuildPopup();
    }

    public void SetPlayer(string playerName, int trackNumber)
    {
        playerText.text = playerName;
        trackText.text = $"{trackNumber}曲目";
    }

    public void SetTimer(int seconds)
    {
        timerText.text = seconds.ToString();
        timerText.color = seconds <= 10 ? new Color(1f, 0.35f, 0.3f) : Color.white;
    }

    //左上の表示（今のソートや、今の画面の名前）
    public void SetInfo(string text)
    {
        infoText.text = text;
    }

    //positionのカードが中央に来るようにスクロールする
    public void SetTarget(int position)
    {
        target = position;
    }

    //スクロールせずにすぐ移動する（ソート変更・画面の切り替え時など）
    public void Snap(int position)
    {
        target = position;
        scroll = position;
        RefreshCards();
    }

    //カードの内容が変わったときに作り直す
    public void RefreshCards()
    {
        foreach (var card in cards) card.assignedPosition = int.MinValue;
    }

    public void SetZoneLit(SelectZone zone, bool lit)
    {
        glows[(int)zone].lit = lit;
    }

    //押しても何も起きないエリアは光らせない
    public void SetZoneEnabled(SelectZone zone, bool enabled)
    {
        glows[(int)zone].enabled = enabled;
    }

    //エリアの文字（底面は「決定」などの文字、斜面は「←」「+」などの記号）
    public void SetZoneLabel(SelectZone zone, string text)
    {
        glows[(int)zone].label.text = text;
    }

    //斜面の真ん中の文字（「ソート切り替え」「戻る」など）
    public void SetSlopeTitle(bool left, string text)
    {
        (left ? leftSlope : rightSlope).title.text = text;
    }

    //true: 斜面の2つのエリアを1つのボタンとして光らせる（戻るボタンなど）
    public void SetSlopeMerged(bool left, bool merged)
    {
        (left ? leftSlope : rightSlope).merged = merged;
    }

    public void ShowSortPopup(string[] names, int current)
    {
        if (popupItems.Count != names.Length) BuildPopupItems(names.Length);
        for (int i = 0; i < names.Length; i++)
        {
            popupItems[i].text = names[i];
            popupItems[i].color = i == current ? Color.white : new Color(1f, 1f, 1f, 0.45f);
        }
        popupHighlight.rectTransform.anchoredPosition = popupItems[current].rectTransform.anchoredPosition;
        popupVisible = true;
    }

    public void HideSortPopup()
    {
        popupVisible = false;
    }

    public void PlayDecide()
    {
        decideFlash = 1f;
    }

    //画面の切り替えアニメーション。今のカードを横に流して消し、change()で中身を切り替えてから反対側から出す
    //forward: 先に進むとき（楽曲選択→確定→設定）はtrue、戻るときはfalse
    public void Transition(bool forward, Action change)
    {
        StartCoroutine(TransitionRoutine(forward, change));
    }

    IEnumerator TransitionRoutine(bool forward, Action change)
    {
        const float distance = 160f;
        float dir = forward ? -1f : 1f;
        IsTransitioning = true;
        yield return Animate(0.15f, t =>
        {
            float e = t * t;
            cardGroup.alpha = 1f - t;
            cardLayer.anchoredPosition = new Vector2(dir * distance * e, 0f);
            cardLayer.localScale = Vector3.one * Mathf.Lerp(1f, 0.94f, e);
        });
        change();
        yield return Animate(0.25f, t =>
        {
            float e = EaseOut(t);
            cardGroup.alpha = t;
            cardLayer.anchoredPosition = new Vector2(-dir * distance * (1f - e), 0f);
            cardLayer.localScale = Vector3.one * Mathf.Lerp(0.94f, 1f, e);
        });
        IsTransitioning = false;
    }

    //楽曲紹介画面を出し、そのままプレイ画面へ移る（進行はIntroScreenが行う）
    //紹介画面はプレイ画面に切り替わった後も残す必要があるので、選曲画面とは別の、シーンが変わっても消えないCanvasに作る
    public void PlayIntro(SongData song, int difficulty, float fadeIn, float reveal, float hold, float fadeOut)
    {
        const float jacketSize = 560f;
        const float jacketY = 60f;
        var center = new Vector2(0.5f, 0.5f);

        RectTransform intro = CreateCanvas("IntroCanvas", 100);
        DontDestroyOnLoad(intro.gameObject);
        var group = intro.gameObject.AddComponent<CanvasGroup>();
        group.alpha = 0f;
        //背景は単色。難易度の色を少しだけ混ぜる
        Stretch(CreateImage("Background", intro, Color.Lerp(new Color(0.42f, 0.43f, 0.48f), DifficultyInfo.Colors[difficulty], 0.2f)).rectTransform);

        var content = CreateRect("Content", intro);
        Stretch(content);

        //ジャケットの裏に難易度の色の正方形を置く。最初はジャケットと同じ位置で隠れていて、後で右下にずれて見える
        var jacketBack = CreateImage("JacketBack", content, DifficultyInfo.Colors[difficulty]).rectTransform;
        Place(jacketBack, center, center, center, new Vector2(0f, jacketY), new Vector2(jacketSize, jacketSize));
        CardInfo card = CardInfo.FromSong(song, difficulty);
        var jacket = CreateImage("Jacket", content, card.squareColor, card.squareSprite);
        Place(jacket.rectTransform, center, center, center, new Vector2(0f, jacketY), new Vector2(jacketSize, jacketSize));
        var jacketText = CreateText("JacketText", jacket.rectTransform, card.squareText, 48, TextAnchor.MiddleCenter, FontStyle.Bold);
        Stretch(jacketText.rectTransform);

        //ジャケットの下に曲名
        var title = CreateText("Title", content, song.title, 88, TextAnchor.MiddleCenter);
        Place(title.rectTransform, center, center, center, new Vector2(0f, jacketY - jacketSize / 2f - 90f), new Vector2(1500f, 120f));
        FitOneLine(title);
        AddShadow(title);

        //ジャケットの左に作曲者・譜面制作者（右揃え）
        float infoX = -jacketSize / 2f - 30f;
        Text artist = BuildIntroInfo(content, "作曲", song.artist, new Vector2(infoX, jacketY + 190f));
        Text designer = BuildIntroInfo(content, "譜面制作", song.difficulties[difficulty].designer ?? "", new Vector2(infoX, jacketY - 130f));

        //曲名・作曲者・譜面制作者は1文字ずつ出す
        intro.gameObject.AddComponent<IntroScreen>().Play(group, content, new[] { title, artist, designer }, jacketBack, new Vector2(28f, -28f),
                                                          fadeIn, reveal, hold, fadeOut);
    }

    //戻り値は値の文字（1文字ずつ出すため）
    Text BuildIntroInfo(RectTransform parent, string label, string value, Vector2 rightEdge)
    {
        var anchor = new Vector2(0.5f, 0.5f);
        var labelText = CreateText(label, parent, label, 28, TextAnchor.MiddleRight);
        labelText.color = new Color(1f, 1f, 1f, 0.85f);
        Place(labelText.rectTransform, anchor, anchor, new Vector2(1f, 0.5f), rightEdge, new Vector2(700f, 40f));
        AddShadow(labelText);
        var valueText = CreateText(label + "Value", parent, value, 46, TextAnchor.MiddleRight);
        Place(valueText.rectTransform, anchor, anchor, new Vector2(1f, 0.5f), rightEdge + new Vector2(0f, -46f), new Vector2(700f, 56f));
        FitOneLine(valueText);
        AddShadow(valueText);
        return valueText;
    }

    //画面いっぱいのCanvasを作る。sortingOrderが大きいほど手前に描かれる
    RectTransform CreateCanvas(string name, int sortingOrder)
    {
        var canvasObj = new GameObject(name, typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
        var canvas = canvasObj.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = sortingOrder;
        var scaler = canvasObj.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(RefWidth, RefHeight);
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
        scaler.matchWidthOrHeight = 0f;//横幅基準。センサーの位置は画面の横幅で決まるため
        return (RectTransform)canvasObj.transform;
    }

    static void AddShadow(Text text)
    {
        var shadow = text.gameObject.AddComponent<Shadow>();
        shadow.effectColor = new Color(0f, 0f, 0f, 0.5f);
        shadow.effectDistance = new Vector2(2f, -2f);
    }

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

    public static float EaseOut(float t)
    {
        return 1f - (1f - t) * (1f - t);
    }

    // ===== 毎フレームの更新 =====
    void Update()
    {
        if (cards.Count == 0) return;

        scroll = Mathf.Lerp(scroll, target, 1f - Mathf.Exp(-ScrollSpeed * Time.deltaTime));
        if (Mathf.Abs(scroll - target) < 0.001f) scroll = target;
        decideFlash = Mathf.Max(0f, decideFlash - Time.deltaTime);
        popupGroup.alpha = Mathf.MoveTowards(popupGroup.alpha, popupVisible ? 1f : 0f, Time.deltaTime * 8f);

        LayoutCards();
        UpdateGlows();
    }

    void LayoutCards()
    {
        int center = Mathf.RoundToInt(scroll);
        for (int k = -VisibleRange; k <= VisibleRange; k++)
        {
            int p = center + k;
            //位置pのカードは常にcards[p mod 7]を使う。同じ位置のままなら中身を作り直さない
            SongCard card = cards[Mod(p, cards.Count)];
            if (card.assignedPosition != p)
            {
                card.assignedPosition = p;
                card.Set(itemAt(p));
            }

            float offset = p - scroll;//中央から何枚ぶんずれているか（小数あり）
            float dist = Mathf.Abs(offset);
            float focus = Mathf.Clamp01(1f - dist);//中央で1、隣の位置で0
            card.rect.anchoredPosition = new Vector2(Mathf.Sign(offset) * CardX(dist), Mathf.Lerp(SideY, SelectedY, focus));
            card.rect.localScale = Vector3.one * Mathf.Lerp(SideScale, 1f, focus);
            card.SetFocus(focus);
            card.distance = dist;
        }

        //中央に近いカードほど手前に描く（後ろの兄弟ほど手前）
        var sorted = new List<SongCard>(cards);
        sorted.Sort((a, b) => b.distance.CompareTo(a.distance));
        for (int i = 0; i < sorted.Count; i++) sorted[i].rect.SetSiblingIndex(i);
    }

    float CardX(float dist)
    {
        int i = Mathf.Min(Mathf.FloorToInt(dist), cardX.Length - 2);
        return Mathf.LerpUnclamped(cardX[i], cardX[i + 1], dist - i);
    }

    //触れている間は明るく、触れていない間はゆっくり明滅して操作できる場所を示す
    void UpdateGlows()
    {
        float idle = 0.25f + 0.06f * Mathf.Sin(Time.time * 2.5f);
        float k = 1f - Mathf.Exp(-20f * Time.deltaTime);
        for (int z = 0; z < glows.Length; z++)
        {
            ZoneGlow glow = glows[z];
            bool lit = glow.lit || (z == (int)SelectZone.Decide && decideFlash > 0f);
            float targetLevel = !glow.enabled ? 0f : lit ? 1f : idle;
            glow.level = Mathf.Lerp(glow.level, targetLevel, k);
            Color c = glow.color;
            c.a = glow.level;
            glow.image.color = c;
            glow.label.color = new Color(1f, 1f, 1f, glow.enabled ? 1f : 0.2f);
        }
        UpdateSlope(leftSlope);
        UpdateSlope(rightSlope);
    }

    void UpdateSlope(SlopeVisual slope)
    {
        ZoneGlow a = glows[(int)slope.lower];
        ZoneGlow b = glows[(int)slope.upper];
        //1つのボタンとして光らせるときは、左右に分かれた光を消して斜面全体の光を出す
        Color whole = a.color;
        whole.a = slope.merged ? Mathf.Max(a.level, b.level) : 0f;
        slope.whole.color = whole;
        if (slope.merged)
        {
            a.image.color = Color.clear;
            b.image.color = Color.clear;
        }
        //真ん中の文字は、両側とも押せないときだけ薄くする
        slope.title.color = new Color(1f, 1f, 1f, a.enabled || b.enabled ? 1f : 0.2f);
    }

    // ===== 画面の組み立て =====
    void BuildHeader()
    {
        var header = CreateImage("Header", root, new Color(0.02f, 0.03f, 0.07f, 0.9f)).rectTransform;
        Place(header, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f), Vector2.zero, new Vector2(0f, 100f));
        var line = CreateImage("Line", header, new Color(0.4f, 0.85f, 1f, 0.8f)).rectTransform;
        Place(line, new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0.5f, 1f), Vector2.zero, new Vector2(0f, 3f));

        playerText = CreateText("PlayerName", header, "", 48, TextAnchor.MiddleLeft);
        Stretch(playerText.rectTransform, 60f, 0f, 60f, 0f);
        trackText = CreateText("TrackNumber", header, "", 48, TextAnchor.MiddleRight);
        Stretch(trackText.rectTransform, 60f, 0f, 60f, 0f);

        //制限時間（右上）
        var timer = CreateRect("Timer", root);
        Place(timer, Vector2.one, Vector2.one, Vector2.one, new Vector2(-60f, -110f), new Vector2(260f, 120f));
        var label = CreateText("Label", timer, "制限時間", 28, TextAnchor.UpperRight);
        Stretch(label.rectTransform);
        timerText = CreateText("Seconds", timer, "", 80, TextAnchor.LowerRight, FontStyle.Bold);
        Stretch(timerText.rectTransform);

        infoText = CreateText("Info", root, "", 30, TextAnchor.MiddleLeft);
        Place(infoText.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(60f, -115f), new Vector2(600f, 50f));
    }

    void BuildGlows()
    {
        var layer = CreateRect("Glows", root);
        Stretch(layer);

        if (showHiddenArea)
        {
            //角を中心に45°回した正方形を置くと、画面内に見える部分がちょうど隠れる三角形になる
            BuildHiddenArea(layer, new Vector2(0f, 0f));
            BuildHiddenArea(layer, new Vector2(1f, 0f));
        }

        //底面: 左5・決定6・右5レーン
        var amber = new Color(1f, 0.72f, 0.2f);
        var green = new Color(0.3f, 1f, 0.5f);
        float laneWidth = BottomWidth / 16f;
        float left = -BottomWidth / 2f;
        BuildBottomGlow(layer, SelectZone.Left, left, 5 * laneWidth, amber);
        BuildBottomGlow(layer, SelectZone.Decide, left + 5 * laneWidth, 6 * laneWidth, green);
        BuildBottomGlow(layer, SelectZone.Right, left + 11 * laneWidth, 5 * laneWidth, amber);

        //斜面: 4・4レーン
        //左斜面は画面左辺から右下へ下る線、右斜面は画面下辺から右上へ上る線。どちらもレーン番号が小さい側がローカルの-x
        var cyan = new Color(0.3f, 0.8f, 1f);
        var pink = new Color(1f, 0.4f, 0.75f);
        leftSlope = BuildSlopeGlow(layer, new Vector2(0f, 0f), new Vector2(SlopeSpan / 2f, SlopeSpan / 2f), -45f,
                                   SelectZone.SortPrev, SelectZone.SortNext, cyan);
        rightSlope = BuildSlopeGlow(layer, new Vector2(1f, 0f), new Vector2(-SlopeSpan / 2f, SlopeSpan / 2f), 45f,
                                    SelectZone.DiffDown, SelectZone.DiffUp, pink);
    }

    //画面の左右の端を暗くして奥行きを出す（カードより手前、操作エリアの光より奥に描く）
    void BuildEdgeShade()
    {
        const float width = 420f;
        Sprite shade = CreateEdgeShadeSprite();
        var color = new Color(0f, 0f, 0f, 0.75f);
        var left = CreateImage("EdgeShadeLeft", root, color, shade).rectTransform;
        Place(left, new Vector2(0f, 0f), new Vector2(0f, 1f), new Vector2(0f, 0.5f), Vector2.zero, new Vector2(width, 0f));
        var right = CreateImage("EdgeShadeRight", root, color, shade).rectTransform;
        Place(right, new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(0f, 0.5f), Vector2.zero, new Vector2(width, 0f));
        right.localScale = new Vector3(-1f, 1f, 1f);//左右反転して右端用にする
    }

    void BuildHiddenArea(RectTransform parent, Vector2 corner)
    {
        var area = CreateImage("HiddenArea", parent, new Color(0f, 0f, 0f, 0.7f)).rectTransform;
        Place(area, corner, corner, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(SlopeLength, SlopeLength));
        area.localRotation = Quaternion.Euler(0f, 0f, 45f);
    }

    //底面の光。文字は光の帯の中に置く
    void BuildBottomGlow(RectTransform parent, SelectZone zone, float x, float width, Color color)
    {
        const float gap = 4f;//隣のエリアとの境目
        var image = CreateImage(zone.ToString(), parent, color, gradient);
        Place(image.rectTransform, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), Vector2.zero, new Vector2(x + gap, 0f), new Vector2(width - gap * 2f, BottomGlowHeight));

        var text = CreateText(zone + "Label", parent, "", 32, TextAnchor.MiddleCenter, FontStyle.Bold);
        Place(text.rectTransform, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), Vector2.zero, new Vector2(x, 0f), new Vector2(width, BottomGlowHeight));
        glows[(int)zone] = new ZoneGlow { image = image, color = color, label = text };
    }

    //斜面のセンサーに沿って光る帯。帯の下辺がセンサーの線に重なり、見える側(+y)に向かって薄くなる
    SlopeVisual BuildSlopeGlow(RectTransform parent, Vector2 corner, Vector2 center, float angle,
                               SelectZone lowerLaneZone, SelectZone upperLaneZone, Color color)
    {
        var slope = CreateRect("Slope", parent);
        Place(slope, corner, corner, new Vector2(0.5f, 0.5f), center, new Vector2(SlopeLength, 0f));
        slope.localRotation = Quaternion.Euler(0f, 0f, angle);

        //斜面全体を1つのボタンとして光らせるとき用（戻るボタンなど）
        const float gap = 4f;
        var whole = CreateImage("Whole", slope, Color.clear, gradient);
        Place(whole.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0f), Vector2.zero, new Vector2(SlopeLength - gap * 2f, SlopeGlowHeight));

        float half = SlopeLength / 2f;
        SelectZone[] zones = { lowerLaneZone, upperLaneZone };
        for (int i = 0; i < 2; i++)
        {
            var image = CreateImage(zones[i].ToString(), slope, color, gradient);
            Place(image.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(-half + i * half + gap, 0f), new Vector2(half - gap * 2f, SlopeGlowHeight));
            //記号はそれぞれのエリアの中央に、斜面に沿って傾けて置く
            var sign = CreateText(zones[i] + "Sign", slope, "", 36, TextAnchor.MiddleCenter, FontStyle.Bold);
            Place(sign.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(-half / 2f + i * half, SlopeGlowHeight / 2f), new Vector2(60f, 60f));
            glows[(int)zones[i]] = new ZoneGlow { image = image, color = color, label = sign };
        }

        var title = CreateText("Title", slope, "", 24, TextAnchor.MiddleCenter, FontStyle.Bold);
        Place(title.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, SlopeGlowHeight / 2f), new Vector2(half, SlopeGlowHeight));
        return new SlopeVisual { whole = whole, title = title, lower = lowerLaneZone, upper = upperLaneZone };
    }

    //ソートのポップアップ（画面中央）。中身の行はShowSortPopupで作る
    void BuildPopup()
    {
        popup = CreateRect("SortPopup", root);
        popupGroup = popup.gameObject.AddComponent<CanvasGroup>();
        popupGroup.alpha = 0f;
        Stretch(CreateImage("Background", popup, new Color(0.03f, 0.05f, 0.12f, 0.95f)).rectTransform);
        var line = CreateImage("Line", popup, new Color(0.3f, 0.8f, 1f)).rectTransform;
        Place(line, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f), Vector2.zero, new Vector2(0f, 4f));

        var title = CreateText("Title", popup, "SORT", 36, TextAnchor.MiddleCenter, FontStyle.Bold);
        Place(title.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -16f), new Vector2(480f, 50f));
        popupHighlight = CreateImage("Highlight", popup, new Color(0.3f, 0.8f, 1f, 0.35f));
        var hint = CreateText("Hint", popup, "ソート切り替え ← → で変更", 24, TextAnchor.MiddleCenter);
        hint.color = new Color(1f, 1f, 1f, 0.7f);
        Place(hint.rectTransform, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 16f), new Vector2(480f, 40f));
    }

    void BuildPopupItems(int count)
    {
        const float rowHeight = 52f;
        Place(popup, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, 40f), new Vector2(520f, 140f + rowHeight * count));
        Place(popupHighlight.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), Vector2.zero, new Vector2(460f, rowHeight - 6f));
        for (int i = 0; i < count; i++)
        {
            var item = CreateText("Item" + i, popup, "", 32, TextAnchor.MiddleCenter, FontStyle.Bold);
            Place(item.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -78f - rowHeight * i), new Vector2(460f, rowHeight - 6f));
            popupItems.Add(item);
        }
    }

    //下が不透明で上に向かって透明になる縦グラデーション
    static Sprite CreateGradientSprite()
    {
        const int height = 64;
        var tex = new Texture2D(1, height, TextureFormat.RGBA32, false)
        {
            wrapMode = TextureWrapMode.Clamp,
            filterMode = FilterMode.Bilinear
        };
        for (int y = 0; y < height; y++)
        {
            float t = y / (float)(height - 1);
            tex.SetPixel(0, y, new Color(1f, 1f, 1f, (1f - t) * (1f - t)));
        }
        tex.Apply();
        return Sprite.Create(tex, new Rect(0, 0, 1, height), new Vector2(0.5f, 0.5f));
    }

    //左が不透明で右に向かって透明になる横グラデーション（画面端の影用）
    static Sprite CreateEdgeShadeSprite()
    {
        const int width = 64;
        var tex = new Texture2D(width, 1, TextureFormat.RGBA32, false)
        {
            wrapMode = TextureWrapMode.Clamp,
            filterMode = FilterMode.Bilinear
        };
        for (int x = 0; x < width; x++)
        {
            float t = x / (float)(width - 1);
            tex.SetPixel(x, 0, new Color(1f, 1f, 1f, (1f - t) * (1f - t)));
        }
        tex.Apply();
        return Sprite.Create(tex, new Rect(0, 0, width, 1), new Vector2(0.5f, 0.5f));
    }

    // ===== UI生成の共通処理 =====
    RectTransform CreateRect(string name, Transform parent)
    {
        var rt = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
        rt.SetParent(parent, false);
        return rt;
    }

    Image CreateImage(string name, Transform parent, Color color, Sprite sprite = null)
    {
        var image = CreateRect(name, parent).gameObject.AddComponent<Image>();
        image.sprite = sprite;
        image.color = color;
        image.raycastTarget = false;
        return image;
    }

    Text CreateText(string name, Transform parent, string text, int size, TextAnchor alignment, FontStyle style = FontStyle.Normal)
    {
        var t = CreateRect(name, parent).gameObject.AddComponent<Text>();
        t.font = font;
        t.text = text;
        t.fontSize = size;
        t.fontStyle = style;
        t.alignment = alignment;
        t.color = Color.white;
        t.horizontalOverflow = HorizontalWrapMode.Overflow;
        t.verticalOverflow = VerticalWrapMode.Overflow;
        t.raycastTarget = false;
        return t;
    }

    static void Place(RectTransform rt, Vector2 anchorMin, Vector2 anchorMax, Vector2 pivot, Vector2 position, Vector2 size)
    {
        rt.anchorMin = anchorMin;
        rt.anchorMax = anchorMax;
        rt.pivot = pivot;
        rt.anchoredPosition = position;
        rt.sizeDelta = size;
    }

    //親いっぱいに広げる（left, bottom, right, topは内側への余白）
    static void Stretch(RectTransform rt, float left = 0f, float bottom = 0f, float right = 0f, float top = 0f)
    {
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.offsetMin = new Vector2(left, bottom);
        rt.offsetMax = new Vector2(-right, -top);
    }

    static int Mod(int a, int n)
    {
        return (a % n + n) % n;
    }

    // ===== 操作エリアの光 =====
    class ZoneGlow
    {
        public Image image;
        public Color color;
        public Text label;//押せないときに薄くする文字
        public bool lit;
        public bool enabled = true;
        public float level;
    }

    //斜面1本分の表示
    class SlopeVisual
    {
        public Image whole;//斜面全体の光（mergedのときだけ使う）
        public Text title;
        public SelectZone lower, upper;
        public bool merged;
    }

    // ===== カード =====
    //選択中のサイズ(CardWidth x CardHeight)で作り、選択されていないカードは縮小して表示する
    class SongCard
    {
        public RectTransform rect;
        public int assignedPosition = int.MinValue;//今表示している位置
        public float distance;//中央から何枚ぶん離れているか（描画順に使う）

        private readonly Image background, squareBorder, square;
        private readonly Text topSmall, topLarge, squareText, title, subtitle, rank, highScore;
        private readonly CanvasGroup detail;//選択中のカードだけに出す情報（ランク・ハイスコア）

        public SongCard(MusicSelectView view, Transform parent)
        {
            rect = view.CreateRect("Card", parent);
            Place(rect, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(CardWidth, CardHeight));

            background = view.CreateImage("Background", rect, Color.gray);
            Stretch(background.rectTransform);

            float inner = CardWidth - CardPadding * 2f;
            topSmall = view.CreateText("TopSmall", rect, "", 26, TextAnchor.MiddleCenter);
            PlaceTop(topSmall.rectTransform, 14f, new Vector2(inner, 32f));
            topLarge = view.CreateText("TopLarge", rect, "", 52, TextAnchor.MiddleCenter, FontStyle.Bold);
            PlaceTop(topLarge.rectTransform, 46f, new Vector2(inner, 60f));
            FitOneLine(topLarge);

            const float border = 4f;
            squareBorder = view.CreateImage("SquareBorder", rect, Color.clear);
            PlaceTop(squareBorder.rectTransform, JacketTop - border, new Vector2(JacketSize + border * 2f, JacketSize + border * 2f));
            square = view.CreateImage("Square", rect, Color.gray);
            PlaceTop(square.rectTransform, JacketTop, new Vector2(JacketSize, JacketSize));
            squareText = view.CreateText("SquareText", square.rectTransform, "", 40, TextAnchor.MiddleCenter, FontStyle.Bold);
            squareText.alignByGeometry = true;//行の高さではなく文字の形で中央に揃える（大きい「?」が上下にずれないように）
            Stretch(squareText.rectTransform);

            title = view.CreateText("Title", rect, "", TitleFontSize, TextAnchor.MiddleCenter, FontStyle.Bold);
            PlaceTop(title.rectTransform, TitleTop, new Vector2(inner, TitleHeight));
            FitOneLine(title);
            subtitle = view.CreateText("Subtitle", rect, "", SubtitleFontSize, TextAnchor.MiddleCenter);
            PlaceTop(subtitle.rectTransform, 584f, new Vector2(inner, 34f));
            FitOneLine(subtitle);

            var detailRect = view.CreateRect("Detail", rect);
            PlaceTop(detailRect, 622f, new Vector2(inner - 12f, 80f));
            detail = detailRect.gameObject.AddComponent<CanvasGroup>();
            rank = view.CreateText("Rank", detailRect, "", 44, TextAnchor.MiddleLeft, FontStyle.Bold);
            Stretch(rank.rectTransform);
            highScore = view.CreateText("HighScore", detailRect, "", 22, TextAnchor.MiddleRight);
            Stretch(highScore.rectTransform);
        }

        //info == null のときはカードを隠す
        public void Set(CardInfo info)
        {
            rect.gameObject.SetActive(info != null);
            if (info == null) return;

            background.color = info.background;
            topSmall.text = info.topSmall;
            topLarge.text = info.topLarge;
            square.sprite = info.squareSprite;
            square.color = info.squareColor;
            squareBorder.color = info.squareBorder;
            squareText.text = info.squareText;
            squareText.fontSize = info.squareTextSize;
            squareText.color = info.squareSprite != null ? Color.white : new Color(1f, 1f, 1f, 0.8f);
            title.text = info.title;
            if (info.titleInBottomCenter)
            {
                //ジャケットの下端からカードの下端までを曲名欄にして、その真ん中に表示する
                PlaceTop(title.rectTransform, JacketTop + JacketSize, new Vector2(CardWidth - CardPadding * 2f, CardHeight - JacketTop - JacketSize));
            }
            else
            {
                PlaceTop(title.rectTransform, TitleTop, new Vector2(CardWidth - CardPadding * 2f, TitleHeight));
            }
            title.resizeTextMaxSize = info.titleSize > 0 ? info.titleSize : TitleFontSize;
            subtitle.text = info.subtitle;
            subtitle.resizeTextMaxSize = info.subtitleSize > 0 ? info.subtitleSize : SubtitleFontSize;
            rank.text = info.rank ?? "";
            highScore.text = info.highScore ?? "";
        }

        //focus: 選択中で1、隣の位置で0
        public void SetFocus(float focus)
        {
            detail.alpha = focus;
        }

        static void PlaceTop(RectTransform rt, float top, Vector2 size)
        {
            Place(rt, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -top), size);
        }
    }

    //改行せず1行に収める。枠に収まらない長さなら文字を小さくする
    static void FitOneLine(Text text)
    {
        text.horizontalOverflow = HorizontalWrapMode.Wrap;
        text.resizeTextForBestFit = true;
        text.resizeTextMinSize = 8;
        text.resizeTextMaxSize = text.fontSize;
    }
}
