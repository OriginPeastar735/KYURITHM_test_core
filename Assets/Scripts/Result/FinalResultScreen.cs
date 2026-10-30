using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using static UIUtil;

//Resultシーン。規定の曲数を遊び終えた後、エンディングの前に通る（1曲ごとのリザルトはPlayシーンのResultScreenが出す）
//遊んだ曲ごとの結果（PlaySession.TrackResults）を斜めの帯で並べ、その下に合計スコアを出す。UIはすべてこのスクリプトがコードで生成する
//
//   ／ジャケット・曲名 ／ 難易度・Lv・スコア・判定数 ／   1曲目
//  ／ジャケット・曲名 ／ 難易度・Lv・スコア・判定数 ／    2曲目（下の段ほど左へずらし、斜めの辺を1本の線にそろえる）
// ／ジャケット・曲名 ／ 難易度・Lv・スコア・判定数 ／     3曲目
//                       ／ TOTAL  合計スコア ／ ◇AP     全曲ALL PRECISEならAP、全曲ミス無しならFC
//
//帯が順に流れ込み、合計スコアを数え上げた後、どこかのセンサーに触れる（か時間が経つ）とエンディングへ移る
public class FinalResultScreen : MonoBehaviour
{
    //帯の形と並び（キャンバス座標。画面の真ん中が原点）
    const float RowHeight = 180f;
    const float RowSpacing = 210f;//段の間隔（上の段の中心から下の段の中心まで）
    const float Skew = 56f;//帯の上辺を下辺よりこれだけ右へずらす
    const float JacketWidth = 620f;
    const float InfoWidth = 900f;
    const float FirstRowY = 290f;
    const float FirstRowX = 40f;//1段目の帯の中心
    static readonly float RowShift = RowSpacing * Skew / RowHeight;//1段下がるごとに左へずらす量（斜めの辺をそろえる）
    const float TotalWidth = 760f;
    const float TotalHeight = 130f;

    static readonly Color BackgroundColor = new Color(0.06f, 0.07f, 0.1f);//背景の画像が読めなかったときの色
    const string BackdropTexture = "G-xGTP1a8AE1oVH";//プレイ画面の背景と同じ画像（Resources）
    const float BackdropBrightness = 0.45f;
    static readonly Color InfoColor = new Color(0.13f, 0.15f, 0.2f, 0.9f);
    static readonly Color LabelColor = new Color(1f, 1f, 1f, 0.6f);
    static readonly Color BadgeColor = new Color(1f, 0.95f, 0.25f);

    [SerializeField] private float inputLockSeconds = 1.5f;//開始直後は入力を受け付けない（リザルトからの押しっぱなし対策）
    [SerializeField] private float autoLeaveSeconds = 30f;//触れなくてもこの秒数でエンディングへ移る

    private Font font;
    private Sprite gradient;
    private RectTransform root;
    private readonly List<CanvasGroup> rows = new List<CanvasGroup>();
    private CanvasGroup totalRow;
    private Text totalText;
    private RectTransform badge;
    private Text continueText;
    private float startTime;
    private bool ready;//演出が終わって入力を受け付けている
    private bool leaving;

    void Start()
    {
        List<TrackResult> results = new List<TrackResult>();
        foreach (TrackResult result in PlaySession.TrackResults)
        {
            if (result != null) results.Add(result);
        }
        //結果が無い（Resultシーンから直接起動したなど）ときは、見た目を確認できるよう仮の結果を出す
        if (results.Count == 0) results = PlaceholderResults();

        //このシーンにはInputManagerが置かれていないので作る（キーボードはInput.anyKeyDownで読む）
        if (InputManager.instance == null)
        {
            var input = new GameObject("InputManager").AddComponent<InputManager>();
            input.SetSource(InputSource.Controller);
        }

        startTime = Time.time;
        font = CreateFont();
        gradient = SensorGlow.CreateGradientSprite();
        Build(results);
        StartCoroutine(Play(results));
    }

    void Update()
    {
        if (leaving || root == null) return;

        if (continueText != null && ready)
        {
            Color c = continueText.color;
            c.a = 0.55f + 0.45f * Mathf.Sin(Time.time * 3f);
            continueText.color = c;
        }

        bool timeUp = Time.time - startTime >= autoLeaveSeconds;
        if ((ready && Time.time - startTime >= inputLockSeconds && AnyPressed()) || timeUp) Leave();
    }

    bool AnyPressed()
    {
        if (InputManager.instance != null && InputManager.instance.AnyDown(0, InputManager.LaneCount)) return true;
        return Input.anyKeyDown;
    }

    void Leave()
    {
        leaving = true;
        System.Array.Clear(PlaySession.TrackResults, 0, PlaySession.TrackResults.Length);//次のプレイヤーに残さない
        SceneFader.FadeTo(GameScene.Ending, Color.black);
    }

    //Resultシーン単体で動かすときの仮の結果。曲はStreamingAssets/Songsの先頭から借り（無ければ仮の曲名）、
    //1曲目はALL PRECISE、2曲目はAPPROXあり、3曲目はMISSありにして表示の違いを見られるようにする
    static List<TrackResult> PlaceholderResults()
    {
        List<SongData> songs = SongLibrary.LoadAll();
        var results = new List<TrackResult>();
        int[,] counts =
        {
            //PRECISE+, PRECISE, APPROX, MISS, FAST, LATE
            { 1200, 0, 0, 0, 0, 0 },
            { 820, 180, 12, 0, 104, 88 },
            { 400, 300, 60, 25, 200, 160 },
        };
        for (int i = 0; i < PlaySession.MaxTracks; i++)
        {
            SongData song = songs.Count > 0 ? songs[i % songs.Count] : null;
            int difficulty = i % DifficultyInfo.Names.Length;
            if (song != null && (difficulty >= song.difficulties.Count || song.difficulties[difficulty] == null)) difficulty = 0;
            var result = new TrackResult
            {
                title = song != null ? song.title : $"仮の曲 {i + 1}",
                difficulty = difficulty,
                level = song != null && song.difficulties.Count > difficulty && song.difficulties[difficulty] != null ? song.difficulties[difficulty].level : 10 + i,
                jacketPath = song != null ? song.jacketPath : "",
                precisePlus = counts[i % 3, 0],
                precise = counts[i % 3, 1],
                approx = counts[i % 3, 2],
                miss = counts[i % 3, 3],
                fast = counts[i % 3, 4],
                late = counts[i % 3, 5],
            };
            //GameManagerと同じ計算（PRECISE+ 101%・PRECISE 100%・APPROX 50%）
            int total = result.precisePlus + result.precise + result.approx + result.miss;
            long rateSum = result.precisePlus * 101L + result.precise * 100L + result.approx * 50L;
            result.score = (int)(1000000L * rateSum / (100L * total));
            results.Add(result);
        }
        return results;
    }

    // ===== 演出 =====
    //帯を上から順に右から流し込み → 合計スコアを数え上げ → バッジを出す
    IEnumerator Play(List<TrackResult> results)
    {
        const float slideDistance = 400f;
        const float slideSeconds = 0.45f;
        const float stagger = 0.15f;

        var allRows = new List<CanvasGroup>(rows) { totalRow };
        var shownPos = new Vector2[allRows.Count];
        for (int i = 0; i < allRows.Count; i++)
        {
            var rect = (RectTransform)allRows[i].transform;
            shownPos[i] = rect.anchoredPosition;
            allRows[i].alpha = 0f;
        }

        yield return new WaitForSeconds(0.3f);
        for (int i = 0; i < allRows.Count; i++)
        {
            int index = i;
            var rect = (RectTransform)allRows[index].transform;
            StartCoroutine(Animate(slideSeconds, t =>
            {
                float e = EaseOut(t);
                allRows[index].alpha = e;
                rect.anchoredPosition = shownPos[index] + new Vector2(slideDistance * (1f - e), 0f);
            }));
            yield return new WaitForSeconds(stagger);
        }
        yield return new WaitForSeconds(slideSeconds);

        int total = 0;
        foreach (TrackResult result in results) total += result.score;
        yield return Animate(0.8f, t => totalText.text = Mathf.RoundToInt(total * EaseOut(t)).ToString());

        if (badge != null)
        {
            var group = badge.GetComponent<CanvasGroup>();
            yield return Animate(0.3f, t =>
            {
                float e = EaseOut(t);
                group.alpha = e;
                badge.localScale = Vector3.one * Mathf.Lerp(1.6f, 1f, e);
            });
        }

        continueText.gameObject.SetActive(true);
        ready = true;
    }

    // ===== 画面の組み立て =====
    void Build(List<TrackResult> results)
    {
        root = CreateCanvas("FinalResultCanvas", 0, 0f);
        Stretch(CreateImage("Background", root, BackgroundColor).rectTransform);
        BuildBackdrop();

        var header = CreateText("Header", root, "RESULT", 56, TextAnchor.MiddleLeft, FontStyle.Bold);
        Place(header.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(60f, -30f), new Vector2(600f, 80f));

        for (int i = 0; i < results.Count; i++)
        {
            Vector2 pos = new Vector2(FirstRowX - i * RowShift, FirstRowY - i * RowSpacing);
            rows.Add(BuildTrackRow(i, results[i], pos));
        }

        //合計の帯は、曲の帯の右端と同じ斜めの線に右端をそろえる
        int totalIndex = PlaySession.MaxTracks;
        float rowRight = FirstRowX - totalIndex * RowShift + (JacketWidth + InfoWidth) / 2f;
        Vector2 totalPos = new Vector2(rowRight - TotalWidth / 2f, FirstRowY - totalIndex * RowSpacing + (RowHeight - TotalHeight) / 2f);
        totalRow = BuildTotalRow(totalPos);
        BuildBadge(results, totalPos + new Vector2(TotalWidth / 2f + 140f, 0f));

        continueText = CreateText("Continue", root, "TOUCH TO CONTINUE", 36, TextAnchor.MiddleCenter, FontStyle.Bold);
        Place(continueText.rectTransform, new Vector2(0f, -470f), new Vector2(700f, 60f));//下の真ん中
        continueText.gameObject.SetActive(false);
    }

    //プレイ画面と同じ背景を、文字が読めるよう暗くして画面いっぱいに敷く（縦横比はそのままではみ出す分を切る）
    void BuildBackdrop()
    {
        Texture2D texture = Resources.Load<Texture2D>(BackdropTexture);
        if (texture == null) return;
        var backdrop = CreateRaw("Backdrop", root, new Color(BackdropBrightness, BackdropBrightness, BackdropBrightness, 1f));
        backdrop.texture = texture;
        Stretch(backdrop.rectTransform);
        var fitter = backdrop.gameObject.AddComponent<AspectRatioFitter>();
        fitter.aspectMode = AspectRatioFitter.AspectMode.EnvelopeParent;
        fitter.aspectRatio = texture.width / (float)texture.height;
    }

    CanvasGroup BuildTrackRow(int index, TrackResult result, Vector2 position)
    {
        RectTransform row = CreateRect("Track" + (index + 1), root);
        Place(row, position, new Vector2(JacketWidth + InfoWidth, RowHeight));
        var group = row.gameObject.AddComponent<CanvasGroup>();

        BuildJacket(row, result);
        BuildInfo(row, result);
        return group;
    }

    //左側: ジャケットを横長に切り抜き、下を暗くして曲名を重ねる
    void BuildJacket(RectTransform row, TrackResult result)
    {
        Sprite sprite = SongLibrary.LoadJacket(result.jacketPath);
        SkewedRawImage jacket = CreateSkewed("Jacket", row, Color.white, 0f, JacketWidth);
        if (sprite != null)
        {
            jacket.texture = sprite.texture;
            jacket.uvRect = CropToBand(sprite.texture, JacketWidth / RowHeight);
        }
        else
        {
            jacket.color = Color.Lerp(DifficultyInfo.Colors[result.difficulty], Color.black, 0.4f);
        }

        //曲名を読みやすくするため、下側を暗くする（グラデーションは下が濃い）
        SkewedRawImage shade = CreateSkewed("Shade", row, new Color(0f, 0f, 0f, 0.75f), 0f, JacketWidth);
        shade.texture = gradient.texture;

        float left = -Skew / 2f + 30f;//下辺の左端から少し内側
        var title = CreateText("Title", row, result.title, 50, TextAnchor.LowerLeft, FontStyle.Bold);
        Place(title.rectTransform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(0f, 0f), new Vector2(left, -RowHeight / 2f + 18f), new Vector2(JacketWidth - 220f, 70f));
        ShrinkToFit(title);
        AddShadow(title);
    }

    //右側: 上の段に難易度・レベル（左）とスコア（右）、下の段に判定数
    void BuildInfo(RectTransform row, TrackResult result)
    {
        CreateSkewed("Info", row, InfoColor, JacketWidth, InfoWidth);

        //難易度は難易度の色の札に白い文字で書き、その右にレベルを並べる
        float left = JacketWidth + 50f;
        var tag = CreateImage("DifficultyTag", row, DifficultyInfo.Colors[result.difficulty]).rectTransform;
        Place(tag, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(left, 38f), new Vector2(200f, 46f));
        var difficulty = CreateText("Difficulty", tag, DifficultyInfo.Names[result.difficulty], 28, TextAnchor.MiddleCenter, FontStyle.Bold);
        Stretch(difficulty.rectTransform, 8f, 0f, 8f, 0f);
        ShrinkToFit(difficulty);
        var level = CreateText("Level", row, $"Lv.{result.level}", 48, TextAnchor.MiddleLeft, FontStyle.Bold);
        Place(level.rectTransform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(left + 220f, 38f), new Vector2(160f, 60f));

        var score = CreateText("Score", row, result.score.ToString(), 96, TextAnchor.MiddleRight, FontStyle.Bold);
        Place(score.rectTransform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(1f, 0.5f), new Vector2(JacketWidth + InfoWidth - 60f, 38f), new Vector2(440f, 110f));

        float x = JacketWidth + 110f;
        BuildCount(row, "PRECISE+", result.precisePlus, x);
        BuildCount(row, "PRECISE", result.precise, x + 140f);
        BuildCount(row, "APPROX", result.approx, x + 270f);
        BuildCount(row, "MISS", result.miss, x + 390f);

        //FAST・LATEは2行に並べる
        float labelX = x + 480f;
        BuildFastLate(row, "FAST", result.fast, labelX, -28f);
        BuildFastLate(row, "LATE", result.late, labelX, -60f);
    }

    void BuildCount(RectTransform row, string label, int count, float centerX)
    {
        var number = CreateText(label, row, count.ToString(), 44, TextAnchor.MiddleCenter, FontStyle.Bold);
        Place(number.rectTransform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(centerX, -36f), new Vector2(140f, 50f));
        var name = CreateText(label + "Label", row, label, 18, TextAnchor.MiddleCenter);
        name.color = LabelColor;
        Place(name.rectTransform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(centerX, -68f), new Vector2(140f, 24f));
    }

    void BuildFastLate(RectTransform row, string label, int count, float leftX, float y)
    {
        var name = CreateText(label + "Label", row, label, 26, TextAnchor.MiddleLeft);
        name.color = LabelColor;
        Place(name.rectTransform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(leftX, y), new Vector2(100f, 32f));
        var number = CreateText(label, row, count.ToString(), 26, TextAnchor.MiddleRight);
        Place(number.rectTransform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(1f, 0.5f), new Vector2(leftX + 190f, y), new Vector2(90f, 32f));
    }

    CanvasGroup BuildTotalRow(Vector2 position)
    {
        RectTransform row = CreateRect("Total", root);
        Place(row, position, new Vector2(TotalWidth, TotalHeight));
        var group = row.gameObject.AddComponent<CanvasGroup>();
        CreateSkewed("Panel", row, InfoColor, 0f, TotalWidth, TotalHeight);

        var label = CreateText("Label", row, "TOTAL", 28, TextAnchor.MiddleLeft, FontStyle.Bold);
        label.color = LabelColor;
        Place(label.rectTransform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(40f, 0f), new Vector2(160f, 40f));
        totalText = CreateText("Score", row, "0", 88, TextAnchor.MiddleRight, FontStyle.Bold);
        Place(totalText.rectTransform, new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(-50f, 0f), new Vector2(560f, 110f));
        return group;
    }

    //全曲ALL PRECISEならAP、全曲ミス無しならFCのバッジ（ひし形）。どちらでもなければ出さない
    void BuildBadge(List<TrackResult> results, Vector2 position)
    {
        bool allPrecise = results.TrueForAll(r => r.IsAllPrecise);
        bool fullCombo = results.TrueForAll(r => r.IsFullCombo);
        if (!allPrecise && !fullCombo) return;

        badge = CreateRect("Badge", root);
        Place(badge, position, new Vector2(200f, 200f));
        badge.gameObject.AddComponent<CanvasGroup>().alpha = 0f;

        var outer = CreateImage("Outer", badge, new Color(BadgeColor.r, BadgeColor.g, BadgeColor.b, 0.35f)).rectTransform;
        Place(outer, Vector2.zero, new Vector2(140f, 140f));
        outer.localRotation = Quaternion.Euler(0f, 0f, 45f);
        var inner = CreateImage("Inner", badge, new Color(0.08f, 0.08f, 0.04f, 0.85f)).rectTransform;
        Place(inner, Vector2.zero, new Vector2(124f, 124f));
        inner.localRotation = Quaternion.Euler(0f, 0f, 45f);

        var text = CreateText("Text", badge, allPrecise ? "AP" : "FC", 88, TextAnchor.MiddleCenter, FontStyle.Bold);
        text.color = BadgeColor;
        Place(text.rectTransform, Vector2.zero, new Vector2(200f, 120f));
        var glow = text.gameObject.AddComponent<Outline>();
        glow.effectColor = new Color(BadgeColor.r, BadgeColor.g, BadgeColor.b, 0.5f);
        glow.effectDistance = new Vector2(3f, -3f);
    }

    // ===== UI部品 =====
    //帯の中で左端からleftの位置に、幅widthの斜めの帯を置く
    SkewedRawImage CreateSkewed(string name, RectTransform parent, Color color, float left, float width, float height = RowHeight)
    {
        var obj = new GameObject(name, typeof(RectTransform));
        obj.transform.SetParent(parent, false);
        var image = obj.AddComponent<SkewedRawImage>();
        image.color = color;
        image.raycastTarget = false;
        //帯の斜めの辺は高さに比例してずらすので、高さが違う帯でも傾きをそろえる
        image.Skew = Skew * height / RowHeight;
        Place(image.rectTransform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(left, 0f), new Vector2(width, height));
        return image;
    }

    RawImage CreateRaw(string name, RectTransform parent, Color color)
    {
        var obj = new GameObject(name, typeof(RectTransform));
        obj.transform.SetParent(parent, false);
        var image = obj.AddComponent<RawImage>();
        image.color = color;
        image.raycastTarget = false;
        return image;
    }

    //正方形などのテクスチャから、縦横比aspect（横/縦）の帯を真ん中で切り抜くuv
    static Rect CropToBand(Texture texture, float aspect)
    {
        float textureAspect = texture.width / (float)texture.height;
        if (textureAspect < aspect)
        {
            float h = textureAspect / aspect;
            return new Rect(0f, 0.5f - h / 2f, 1f, h);
        }
        float w = aspect / textureAspect;
        return new Rect(0.5f - w / 2f, 0f, w, 1f);
    }

    static void ShrinkToFit(Text text)
    {
        text.horizontalOverflow = HorizontalWrapMode.Wrap;
        text.resizeTextForBestFit = true;
        text.resizeTextMinSize = 8;
        text.resizeTextMaxSize = text.fontSize;
    }

    static void AddShadow(Text text)
    {
        var shadow = text.gameObject.AddComponent<Shadow>();
        shadow.effectColor = new Color(0f, 0f, 0f, 0.6f);
        shadow.effectDistance = new Vector2(2f, -2f);
    }

    Text CreateText(string name, Transform parent, string text, int size, TextAnchor alignment, FontStyle style = FontStyle.Normal)
    {
        return UIUtil.CreateText(name, parent, font, Color.white, text, size, alignment, style);
    }
}
