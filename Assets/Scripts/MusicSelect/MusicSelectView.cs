using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

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

    //カードの配置。中央からの距離(0,1,2,3枚目)ごとのx座標
    const int VisibleRange = 3;//中央から左右に何枚まで置くか（3枚目は画面外で、スクロール時に入ってくる）
    static readonly float[] cardX = { 0f, 440f, 780f, 1120f };
    const float SelectedY = 50f;
    const float SideY = 60f;
    const float SideScale = 0.62f;//選択されていないカードの大きさ（選択中のカードに対する倍率）
    const float ScrollSpeed = 14f;//スクロールの追従の速さ

    [Tooltip("斜面センサーで隠れる部分を暗く表示する（センサー無しのモニターでテストするとき用）")]
    public bool showHiddenArea = true;

    static readonly Color[] difficultyColors =
    {
        new Color(0.15f, 0.45f, 0.75f),//NORMAL
        new Color(0.80f, 0.35f, 0.15f),//HARD
        new Color(0.50f, 0.20f, 0.75f),//EXTRA
    };

    private Font font;
    private Sprite gradient;
    private RectTransform root;
    private RectTransform cardLayer;
    private Text playerText, trackText, timerText, sortText;
    private readonly List<SongCard> cards = new List<SongCard>();
    private readonly ZoneGlow[] glows = new ZoneGlow[MusicSelectManager.ZoneCount];

    private Func<int, SongData> songAt;
    private Func<SongData, int> displayDifficulty;
    private float scroll;//表示上の位置。targetに向かってなめらかに動く
    private int target;
    private float decideFlash;

    // ===== Managerから呼ぶ =====
    public void Build(Func<int, SongData> songAt, Func<SongData, int> displayDifficulty)
    {
        this.songAt = songAt;
        this.displayDifficulty = displayDifficulty;

        //日本語を表示できるTMPフォントがまだ無いので、WindowsのフォントをレガシーTextで使う
        font = Font.CreateDynamicFontFromOSFont(new[] { "Yu Gothic UI", "Meiryo UI", "Meiryo", "MS Gothic", "Arial" }, 32);
        gradient = CreateGradientSprite();

        var canvasObj = new GameObject("MusicSelectCanvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
        var canvas = canvasObj.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 10;//シーンに元からあるCanvasより手前に描く
        var scaler = canvasObj.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(RefWidth, RefHeight);
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
        scaler.matchWidthOrHeight = 0f;//横幅基準。センサーの位置は画面の横幅で決まるため
        root = (RectTransform)canvasObj.transform;

        Stretch(CreateImage("Background", root, new Color(0.05f, 0.06f, 0.12f)).rectTransform);

        cardLayer = CreateRect("Cards", root);
        Stretch(cardLayer);
        for (int i = 0; i < VisibleRange * 2 + 1; i++) cards.Add(new SongCard(this, cardLayer));

        BuildHeader();
        BuildGlows();
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

    public void SetSortName(string sortName)
    {
        sortText.text = $"SORT : {sortName}";
    }

    //positionの曲が中央に来るようにスクロールする
    public void SetTarget(int position)
    {
        target = position;
    }

    //スクロールせずにすぐ移動する（ソート変更時など）
    public void Snap(int position)
    {
        target = position;
        scroll = position;
        RefreshCards();
    }

    //難易度やソートが変わったときにカードの内容を作り直す
    public void RefreshCards()
    {
        foreach (var card in cards) card.assignedPosition = int.MinValue;
    }

    public void SetZoneLit(SelectZone zone, bool lit)
    {
        glows[(int)zone].lit = lit;
    }

    public void PlayDecide()
    {
        decideFlash = 1f;
    }

    // ===== 毎フレームの更新 =====
    void Update()
    {
        if (cards.Count == 0) return;

        scroll = Mathf.Lerp(scroll, target, 1f - Mathf.Exp(-ScrollSpeed * Time.deltaTime));
        if (Mathf.Abs(scroll - target) < 0.001f) scroll = target;
        decideFlash = Mathf.Max(0f, decideFlash - Time.deltaTime);

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
                SongData song = songAt(p);
                card.SetSong(song, displayDifficulty(song));
            }

            float offset = p - scroll;//中央から何枚ぶんずれているか（小数あり）
            float dist = Mathf.Abs(offset);
            float focus = Mathf.Clamp01(1f - dist);//中央で1、隣の位置で0
            card.rect.anchoredPosition = new Vector2(Mathf.Sign(offset) * CardX(dist), Mathf.Lerp(SideY, SelectedY, focus));
            card.rect.localScale = Vector3.one * Mathf.Lerp(SideScale, 1f, focus);
            card.SetFocus(focus, decideFlash);
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
        float idle = 0.3f + 0.08f * Mathf.Sin(Time.time * 2.5f);
        float k = 1f - Mathf.Exp(-20f * Time.deltaTime);
        for (int z = 0; z < glows.Length; z++)
        {
            ZoneGlow glow = glows[z];
            bool lit = glow.lit || (z == (int)SelectZone.Decide && decideFlash > 0f);
            glow.level = Mathf.Lerp(glow.level, lit ? 1f : idle, k);
            Color c = glow.color;
            c.a = glow.level;
            glow.image.color = c;
        }
    }

    // ===== 画面の組み立て =====
    void BuildHeader()
    {
        var header = CreateImage("Header", root, new Color(0f, 0f, 0f, 0.55f)).rectTransform;
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

        sortText = CreateText("Sort", root, "", 30, TextAnchor.MiddleLeft);
        Place(sortText.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(60f, -115f), new Vector2(600f, 50f));
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
        BuildBottomGlow(layer, SelectZone.Left, left, 5 * laneWidth, "← 左", amber);
        BuildBottomGlow(layer, SelectZone.Decide, left + 5 * laneWidth, 6 * laneWidth, "決定", green);
        BuildBottomGlow(layer, SelectZone.Right, left + 11 * laneWidth, 5 * laneWidth, "右 →", amber);

        //斜面: 4・4レーン
        //左斜面は画面左辺から右下へ下る線、右斜面は画面下辺から右上へ上る線。どちらもレーン番号が小さい側がローカルの-x
        var cyan = new Color(0.3f, 0.8f, 1f);
        var pink = new Color(1f, 0.4f, 0.75f);
        BuildSlopeGlow(layer, new Vector2(0f, 0f), new Vector2(SlopeSpan / 2f, SlopeSpan / 2f), -45f,
                       SelectZone.SortPrev, SelectZone.SortNext, "←  ソート切り替え  →", cyan);
        BuildSlopeGlow(layer, new Vector2(1f, 0f), new Vector2(-SlopeSpan / 2f, SlopeSpan / 2f), 45f,
                       SelectZone.DiffDown, SelectZone.DiffUp, "−  難易度切り替え  +", pink);
    }

    void BuildHiddenArea(RectTransform parent, Vector2 corner)
    {
        var area = CreateImage("HiddenArea", parent, new Color(0f, 0f, 0f, 0.7f)).rectTransform;
        Place(area, corner, corner, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(SlopeLength, SlopeLength));
        area.localRotation = Quaternion.Euler(0f, 0f, 45f);
    }

    void BuildBottomGlow(RectTransform parent, SelectZone zone, float x, float width, string label, Color color)
    {
        const float gap = 4f;//隣のエリアとの境目
        var image = CreateImage(zone.ToString(), parent, color, gradient);
        Place(image.rectTransform, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), Vector2.zero, new Vector2(x + gap, 0f), new Vector2(width - gap * 2f, 150f));
        glows[(int)zone] = new ZoneGlow { image = image, color = color };

        var text = CreateText(zone + "Label", parent, label, 36, TextAnchor.MiddleCenter, FontStyle.Bold);
        Place(text.rectTransform, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(x + width / 2f, 145f), new Vector2(width, 50f));
    }

    //斜面のセンサーに沿って光る帯。帯の下辺がセンサーの線に重なり、見える側(+y)に向かって薄くなる
    void BuildSlopeGlow(RectTransform parent, Vector2 corner, Vector2 center, float angle,
                        SelectZone lowerLaneZone, SelectZone upperLaneZone, string label, Color color)
    {
        var slope = CreateRect("Slope", parent);
        Place(slope, corner, corner, new Vector2(0.5f, 0.5f), center, new Vector2(SlopeLength, 0f));
        slope.localRotation = Quaternion.Euler(0f, 0f, angle);

        const float gap = 4f;
        float half = SlopeLength / 2f;
        SelectZone[] zones = { lowerLaneZone, upperLaneZone };
        for (int i = 0; i < 2; i++)
        {
            var image = CreateImage(zones[i].ToString(), slope, color, gradient);
            Place(image.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(-half + i * half + gap, 0f), new Vector2(half - gap * 2f, 130f));
            glows[(int)zones[i]] = new ZoneGlow { image = image, color = color };
        }

        var text = CreateText("Label", slope, label, 32, TextAnchor.MiddleCenter, FontStyle.Bold);
        Place(text.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0f), new Vector2(0f, 110f), new Vector2(SlopeLength, 50f));
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
        public bool lit;
        public float level;
    }

    // ===== 楽曲カード =====
    //選択中のサイズ(520x760)で作り、選択されていないカードは縮小して表示する
    class SongCard
    {
        public RectTransform rect;
        public int assignedPosition = int.MinValue;//今表示している選択位置
        public float distance;//中央から何枚ぶん離れているか（描画順に使う）

        private readonly Image frame, background, jacket;
        private readonly Text difficultyName, level, jacketLabel, title, artist, rank, highScore;
        private readonly CanvasGroup detail;//選択中のカードだけに出す情報（ランク・ハイスコア）

        public SongCard(MusicSelectView view, Transform parent)
        {
            rect = view.CreateRect("SongCard", parent);
            Place(rect, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(520f, 760f));

            frame = view.CreateImage("Frame", rect, Color.white);
            Stretch(frame.rectTransform, -6f, -6f, -6f, -6f);
            background = view.CreateImage("Background", rect, Color.gray);
            Stretch(background.rectTransform);

            difficultyName = view.CreateText("DifficultyName", rect, "", 28, TextAnchor.MiddleCenter);
            PlaceTop(difficultyName.rectTransform, 12f, new Vector2(480f, 34f));
            level = view.CreateText("Level", rect, "", 56, TextAnchor.MiddleCenter, FontStyle.Bold);
            PlaceTop(level.rectTransform, 44f, new Vector2(480f, 64f));

            jacket = view.CreateImage("Jacket", rect, Color.gray);
            PlaceTop(jacket.rectTransform, 116f, new Vector2(440f, 440f));
            jacketLabel = view.CreateText("JacketLabel", jacket.rectTransform, "JACKET\n(仮)", 40, TextAnchor.MiddleCenter);
            jacketLabel.color = new Color(1f, 1f, 1f, 0.7f);
            Stretch(jacketLabel.rectTransform);

            title = view.CreateText("Title", rect, "", 44, TextAnchor.MiddleCenter, FontStyle.Bold);
            PlaceTop(title.rectTransform, 566f, new Vector2(490f, 56f));
            FitWidth(title, 20);
            artist = view.CreateText("Artist", rect, "", 28, TextAnchor.MiddleCenter);
            PlaceTop(artist.rectTransform, 622f, new Vector2(490f, 36f));
            FitWidth(artist, 16);

            var detailRect = view.CreateRect("Detail", rect);
            PlaceTop(detailRect, 664f, new Vector2(490f, 84f));
            detail = detailRect.gameObject.AddComponent<CanvasGroup>();
            rank = view.CreateText("Rank", detailRect, "", 48, TextAnchor.MiddleLeft, FontStyle.Bold);
            Stretch(rank.rectTransform);
            highScore = view.CreateText("HighScore", detailRect, "", 26, TextAnchor.MiddleRight);
            Stretch(highScore.rectTransform);
        }

        public void SetSong(SongData song, int difficultyIndex)
        {
            DifficultyData diff = song.difficulties[difficultyIndex];
            background.color = difficultyColors[Mathf.Min(difficultyIndex, difficultyColors.Length - 1)];
            difficultyName.text = diff.name;
            level.text = $"Lv.{diff.level}";
            title.text = song.title;
            artist.text = song.artist;
            rank.text = "RANK -";//プレイ状況.jsonができたら差し替える
            highScore.text = "HIGH SCORE\n0";

            Sprite sprite = string.IsNullOrEmpty(song.jacketPath) ? null : Resources.Load<Sprite>(song.jacketPath);
            jacket.sprite = sprite;
            jacket.color = sprite != null ? Color.white : Color.HSVToRGB(song.id * 0.13f % 1f, 0.45f, 0.85f);
            jacketLabel.enabled = sprite == null;
        }

        //focus: 選択中で1、隣の位置で0。flash: 決定時の点滅
        public void SetFocus(float focus, float flash)
        {
            detail.alpha = focus;
            float blink = flash > 0f ? 0.5f + 0.5f * Mathf.Sin(flash * 40f) : 0f;
            frame.color = Color.Lerp(new Color(0.4f, 0.85f, 1f, focus * 0.9f), new Color(1f, 1f, 1f, focus), blink);
        }

        static void PlaceTop(RectTransform rt, float top, Vector2 size)
        {
            Place(rt, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -top), size);
        }

        //長い文字列は枠に収まるまで文字を小さくする
        static void FitWidth(Text text, int minSize)
        {
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.resizeTextForBestFit = true;
            text.resizeTextMinSize = minSize;
            text.resizeTextMaxSize = text.fontSize;
        }
    }
}
