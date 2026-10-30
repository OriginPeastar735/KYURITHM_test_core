using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using static UIUtil;

//チュートリアルの進行。オープニングで「必要」を選ぶとPlaySession.IsTutorialでPlayシーンが開き、GameManagerがこのコンポーネントを付ける
//曲は流さず、プレイ画面のレーン・判定をそのまま使って練習用のノーツを流す
//
//  1つの練習: 画面中央に説明を出す（TextSeconds秒見せたら隠す） → ノーツを数個流す → 全部判定されたら（ミスしても）次の練習へ
//  タップ → タップ同時押し → ホールド → スライド → 斜面 の順に練習し、最後にまとめを出して選曲画面へ移る（曲数は使わない）
//  設定はオープニングで初心者向けにしてあり、終わったら（スキップでも）始める前の設定に戻す。スコア・コンボは通常のプレイと同じく出す
//  左斜面・底面・右斜面の3面を同時にSkipSeconds秒触り続けると、途中でも選曲画面へ移る（右上のゲージで進み具合を見せる。離すと0に戻る）
public class Tutorial : MonoBehaviour
{
    const float Bpm = 120f;//練習の譜面のBPM（1拍0.5秒）
    const float TicksPerBeat = 480f;//1小節1920tickの4分の1
    const float TextSeconds = 5f;//ノーツを流す前に説明だけを見せる時間
    const float LeadSeconds = 0.5f;//ノーツが壁から見え始めるまでの少しの間
    const float StepGapSeconds = 1f;//最後のノーツの判定から次の説明までの間
    const float SkipSeconds = 3f;//3面をこの秒数触り続けるとスキップ

    //スキップに使う3面（左斜面・底面・右斜面）のレーン範囲と名前
    static readonly int[] surfaceStartLane = { 0, 8, 24 };
    static readonly int[] surfaceLaneCount = { 8, 16, 8 };
    static readonly string[] surfaceNames = { "左斜面", "底面", "右斜面" };
    static readonly Color SkipColor = new Color(0.4f, 0.85f, 1f);
    static readonly Color OffColor = new Color(1f, 1f, 1f, 0.3f);

    //練習1つ分。notesのtickは拍数×TicksPerBeat
    class Step
    {
        public string heading;
        public string body;
        public List<ChartNoteData> notes;
        public bool useSlopes;//斜面にもノーツが来る。falseの練習の間は斜面を黒くして隠す
    }

    //ノーツの置き場所（幅はすべてNoteWidth）。レーン番号: 左斜面0〜7・底面8〜23・右斜面24〜31
    const int NoteWidth = 6;
    const int Center = 13;//底面の真ん中（13〜18）
    const int LeftSide = 8;//底面の左寄り（8〜13）
    const int RightSide = 18;//底面の右寄り（18〜23）
    const int PairLeft = 9;//同時押しの左側（9〜14）
    const int PairRight = 17;//同時押しの右側（17〜22）
    const int LeftSlope = 1;//左斜面（1〜6）
    const int RightSlope = 25;//右斜面（25〜30）

    //ノーツの間隔（拍）。基本は等間隔で流す
    const float Interval = 2f;//1つずつ来るノーツの間隔
    const float SlideGap = 4f;//スライドの終点から次のスライドの始点までの間隔
    const float ChainInterval = 0.125f;//連続して来るホールドの間隔（32分）
    const int ChainCount = 10;//連続して来るホールドの数
    const float SlideBeats = 4f;//スライド1区間の長さ

    static List<Step> CreateSteps()
    {
        return new List<Step>
        {
            new Step
            {
                heading = "タップ",
                body = "ノーツが判定ラインに来たら叩こう",
                notes = new List<ChartNoteData>
                {
                    Tap(Center, 0), Tap(Center, Interval), Tap(LeftSide, Interval * 2), Tap(RightSide, Interval * 3),
                },
            },
            new Step
            {
                heading = "タップ",
                body = "並んで来たら同時に叩こう",
                notes = new List<ChartNoteData>
                {
                    Tap(PairLeft, 0), Tap(PairRight, 0),
                    Tap(PairLeft, Interval), Tap(PairRight, Interval),
                },
            },
            new Step
            {
                heading = "ホールド",
                body = "判定ラインで触れていればOK\n押しっぱなしでも取れる",
                //中央に2つ → 中央に10連続 → 2つ同時押しの10連続（連続の後もInterval拍あける）
                notes = Concat(
                    new List<ChartNoteData> { Hold(Center, 0), Hold(Center, Interval) },
                    Chain(Center, HoldChainAt(0)),
                    Chain(PairLeft, HoldChainAt(1)),
                    Chain(PairRight, HoldChainAt(1))),
            },
            new Step
            {
                heading = "スライド",
                body = "叩いたら、指を離さず帯をなぞろう\n離すタイミングは自由",
                notes = new List<ChartNoteData>
                {
                    Slide(SlideAt(0), Center, Center),
                    Slide(SlideAt(1), LeftSide, LeftSide), Slide(SlideAt(1), RightSide, RightSide),
                    Slide(SlideAt(2), LeftSide, RightSide),
                    Slide(SlideAt(3), RightSide, LeftSide),
                    //くの字（中間1つ）。1区間は他のスライドの半分の長さ
                    SlideWith(SlideAt(4), SlideBeats / 2f, LeftSide, RightSide, LeftSide),
                },
            },
            new Step
            {
                heading = "斜面",
                body = "斜面にもノーツが来る\n斜面のセンサーも叩こう",
                useSlopes = true,
                notes = new List<ChartNoteData>
                {
                    Tap(LeftSlope, 0), Tap(LeftSlope, Interval),
                    Hold(RightSlope, Interval * 2), Hold(RightSlope, Interval * 3),
                    Slide(Interval * 4, Center, Center),
                },
            },
        };
    }

    //スライドは1本ずつ、終わってからSlideGap拍あけて次を流す
    static float SlideAt(int index)
    {
        return index * (SlideBeats + SlideGap);
    }

    //ホールドの練習で、index番目の10連続が始まる拍（前のノーツからInterval拍あける）
    static float HoldChainAt(int index)
    {
        return Interval * 2 + index * (ChainInterval * (ChainCount - 1) + Interval);
    }

    //同じ場所に連続して来るホールドChainCount個
    static List<ChartNoteData> Chain(int lane, float beat)
    {
        var chain = new List<ChartNoteData>();
        for (int i = 0; i < ChainCount; i++) chain.Add(Hold(lane, beat + i * ChainInterval));
        return chain;
    }

    static List<ChartNoteData> Concat(params List<ChartNoteData>[] lists)
    {
        var all = new List<ChartNoteData>();
        foreach (var list in lists) all.AddRange(list);
        return all;
    }

    private Font font;
    private RectTransform canvas;
    private CanvasGroup panel;
    private Text headingText, bodyText;
    private string lastHeading;//前に出した説明の見出し（同じなら2回目は見出しを出さない）
    const float PanelHeight = 560f;//説明の裏の黒いエリアの高さ（上下のグラデーションを含む）
    const float PanelY = 200f;//説明の位置（画面の真ん中から上へ）。真ん中だとコンボ表示に重なるので少し上げる
    const float HeadingY = 75f;//見出しの位置（画面の真ん中から）
    const float BodyY = -65f;//見出しがあるときの本文の位置
    private RectTransform skipFill;//スキップのゲージの中身（右端の位置で進み具合を表す）
    private readonly Text[] surfaceTexts = new Text[3];//3面それぞれ、触れているときに光る文字
    private float skipProgress;//0〜1
    private bool finished;

    private List<Step> steps;

    void Start()
    {
        font = CreateFont();
        BuildCanvas();

        //スコアは全練習のノーツ数の合計を基準にする（全部PRECISE+で101万点）
        steps = CreateSteps();
        int total = 0;
        foreach (Step step in steps) total += NoteManager.instance.CountNotes(Chart(step.notes, 0f));
        GetComponent<GameManager>().totalCombo = total;

        StartCoroutine(Run());
    }

    void Update()
    {
        if (finished || InputManager.instance == null) return;

        bool allHeld = true;
        for (int i = 0; i < surfaceTexts.Length; i++)
        {
            bool held = InputManager.instance.AnyHeld(surfaceStartLane[i], surfaceLaneCount[i]);
            surfaceTexts[i].color = held ? SkipColor : OffColor;
            allHeld &= held;
        }

        //触り続けている間だけたまり、どこかを離したら最初からやり直し
        skipProgress = allHeld ? skipProgress + Time.deltaTime / SkipSeconds : 0f;
        skipFill.anchorMax = new Vector2(Mathf.Clamp01(skipProgress), 1f);
        if (skipProgress >= 1f) Finish();
    }

    //チュートリアルを終えて選曲画面へ移る（最後まで練習した場合も、スキップした場合も）
    void Finish()
    {
        if (finished) return;
        finished = true;
        StopAllCoroutines();
        PlaySession.IsTutorial = false;
        //チュートリアル用の初心者向け設定から、始める前の設定に戻す
        if (PlaySession.SettingsBeforeTutorial != null) PlaySession.SettingsBeforeTutorial.Apply();
        PlaySession.SettingsBeforeTutorial = null;
        SceneFader.FadeTo(GameScene.MusicSelect, Color.black);
    }

    IEnumerator Run()
    {
        yield return ShowText("KYURITHMを遊んでくれてありがとう！", "ノーツの種類とたたき方を練習しよう！");
        yield return HideText();

        for (int i = 0; i < steps.Count; i++)
        {
            Step step = steps[i];
            //底面だけを使う練習の間は、斜面の床を黒くして線も消す（斜面の練習で元に戻る）
            if (LaneFieldBuilder.instance != null) LaneFieldBuilder.instance.SetSlopesHidden(!step.useSlopes);
            yield return ShowText(step.heading, step.body);
            yield return HideText();

            //ノーツは今から少し後に壁から見え始めるように、譜面のオフセットを今の時刻に合わせる
            //前の練習のノーツはすべて判定済みなので、オフセットを置き換えてよい
            float firstNoteTime = MusicManager.instance.CurrentPlayTime + NoteManager.instance.TravelSeconds + LeadSeconds;
            NoteManager.instance.LoadChart(Chart(step.notes, firstNoteTime), "tutorial_" + (i + 1));
            while (!NoteManager.instance.AllNotesCleared) yield return null;
            yield return new WaitForSeconds(StepGapSeconds);
        }

        yield return ShowText("これでチュートリアルは終了です", "選曲画面で好きな曲を遊ぼう！");
        Finish();
    }

    //説明を出してTextSeconds秒見せる（隠すのはHideText）
    //見出しが前と同じ（タップ→タップなど）ときは、続きの説明なので見出しは出さず本文だけにする
    IEnumerator ShowText(string heading, string body)
    {
        bool repeated = heading == lastHeading;
        lastHeading = heading;
        headingText.text = repeated ? "" : heading;
        bodyText.text = body;
        //見出しが無いときは本文を真ん中に置く
        bodyText.rectTransform.anchoredPosition = new Vector2(0f, repeated ? 0f : BodyY);
        yield return Animate(0.25f, t => panel.alpha = EaseOut(t));
        yield return new WaitForSeconds(TextSeconds);
    }

    //説明を隠してノーツを見やすくする。次の説明が必要になったらShowTextでまた出す
    IEnumerator HideText()
    {
        yield return Animate(0.3f, t => panel.alpha = 1f - t);
    }

    // ===== 練習の譜面 =====
    static ChartData Chart(List<ChartNoteData> notes, float firstNoteTime)
    {
        return new ChartData
        {
            meta = new ChartMetaData { title = "チュートリアル", artist = "", bpm = Bpm, offset = firstNoteTime * 1000f },
            notes = notes,
        };
    }

    static ChartNoteData Tap(int lane, float beat)
    {
        return new ChartNoteData { type = "tap", lane = lane, width = NoteWidth, tick = beat * TicksPerBeat };
    }

    static ChartNoteData Hold(int lane, float beat)
    {
        return new ChartNoteData { type = "hold", lane = lane, width = NoteWidth, tick = beat * TicksPerBeat };
    }

    //beat拍目にlanes[0]から始まり、SlideBeats拍ごとに次のレーンへ真っすぐ動くslide（3つ以上なら途中が中間）
    static ChartNoteData Slide(float beat, params int[] lanes)
    {
        return SlideWith(beat, SlideBeats, lanes);
    }

    //1区間の長さをsectionBeats拍にしたslide
    static ChartNoteData SlideWith(float beat, float sectionBeats, params int[] lanes)
    {
        var sections = new List<SlideSection>();
        for (int i = 0; i + 1 < lanes.Length; i++)
        {
            sections.Add(new SlideSection
            {
                from = new SlidePointer { lane = lanes[i], width = NoteWidth, tick = (beat + sectionBeats * i) * TicksPerBeat },
                to = new SlidePointer { lane = lanes[i + 1], width = NoteWidth, tick = (beat + sectionBeats * (i + 1)) * TicksPerBeat },
            });
        }
        return new ChartNoteData { type = "slide", lane = lanes[0], width = NoteWidth, tick = beat * TicksPerBeat, sections = sections };
    }

    // ===== 画面の組み立て =====
    void BuildCanvas()
    {
        canvas = CreateCanvas("TutorialCanvas", 20, 0f);
        Sprite gradient = SensorGlow.CreateGradientSprite();

        //説明は画面の真ん中より少し上に出す（出している間はノーツを流さないので、レーンを隠してもよい）
        RectTransform band = CreateRect("Panel", canvas);
        Place(band, new Vector2(0f, 0.5f), new Vector2(1f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, PanelY), new Vector2(0f, PanelHeight));
        panel = band.gameObject.AddComponent<CanvasGroup>();
        panel.alpha = 0f;

        //文字の裏の黒いエリア。文字のある真ん中(ShadeCoreHeight)は一様に濃くし、その上下はグラデーションで透明にしてレーンに溶け込ませる
        //グラデーションは下が濃いので、上側はそのまま、下側は上下を反転して使う
        const float ShadeCoreHeight = 300f;
        float fadeHeight = (PanelHeight - ShadeCoreHeight) / 2f;
        Color shadeColor = new Color(0f, 0f, 0f, 0.95f);
        RectTransform core = CreateImage("ShadeCore", band, shadeColor).rectTransform;
        Place(core, new Vector2(0f, 0.5f), new Vector2(1f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(0f, ShadeCoreHeight));
        RectTransform upper = CreateImage("ShadeUpper", band, shadeColor, gradient).rectTransform;
        Place(upper, new Vector2(0f, 0.5f), new Vector2(1f, 0.5f), new Vector2(0.5f, 0f), new Vector2(0f, ShadeCoreHeight / 2f), new Vector2(0f, fadeHeight));
        RectTransform lower = CreateImage("ShadeLower", band, shadeColor, gradient).rectTransform;
        Place(lower, new Vector2(0f, 0.5f), new Vector2(1f, 0.5f), new Vector2(0.5f, 1f), Vector2.zero, new Vector2(0f, fadeHeight));
        lower.localScale = new Vector3(1f, -1f, 1f);
        //反転するとpivotの反対側に伸びるので、pivotを下端に置く（濃い側が真ん中の濃いエリアに接する）
        lower.anchoredPosition = new Vector2(0f, -ShadeCoreHeight / 2f - fadeHeight);

        //収まらない長さなら文字を小さくする
        const float TextMargin = 120f;
        headingText = CreateText("Heading", band, "", 92, TextAnchor.MiddleCenter, FontStyle.Bold);
        Place(headingText.rectTransform, new Vector2(0f, 0.5f), new Vector2(1f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, HeadingY), new Vector2(-TextMargin * 2f, 120f));
        ShrinkToFit(headingText);
        bodyText = CreateText("Body", band, "", 54, TextAnchor.MiddleCenter);
        Place(bodyText.rectTransform, new Vector2(0f, 0.5f), new Vector2(1f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, BodyY), new Vector2(-TextMargin * 2f, 150f));
        ShrinkToFit(bodyText);

        BuildSkip();
    }

    //右上のスキップ案内。説明とは別に常に出しておく
    void BuildSkip()
    {
        const float width = 360f;
        var skip = CreateRect("Skip", canvas);
        Place(skip, Vector2.one, Vector2.one, Vector2.one, new Vector2(-50f, -60f), new Vector2(width, 170f));

        var guide = CreateText("Guide", skip, $"3面を同時に{SkipSeconds:0}秒\n触り続けるとスキップ", 28, TextAnchor.UpperRight);
        Place(guide.rectTransform, new Vector2(0f, 1f), Vector2.one, new Vector2(0.5f, 1f), Vector2.zero, new Vector2(0f, 76f));

        for (int i = 0; i < surfaceTexts.Length; i++)
        {
            surfaceTexts[i] = CreateText(surfaceNames[i], skip, surfaceNames[i], 26, TextAnchor.MiddleCenter, FontStyle.Bold);
            surfaceTexts[i].color = OffColor;
            Place(surfaceTexts[i].rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f),
                  new Vector2(width / 3f * i, -86f), new Vector2(width / 3f, 36f));
        }

        var gauge = CreateImage("Gauge", skip, new Color(1f, 1f, 1f, 0.15f)).rectTransform;
        Place(gauge, new Vector2(0f, 1f), Vector2.one, new Vector2(0.5f, 1f), new Vector2(0f, -132f), new Vector2(0f, 18f));
        skipFill = CreateImage("Fill", gauge, SkipColor).rectTransform;
        skipFill.anchorMin = Vector2.zero;
        skipFill.anchorMax = new Vector2(0f, 1f);
        skipFill.offsetMin = Vector2.zero;
        skipFill.offsetMax = Vector2.zero;
    }

    static void ShrinkToFit(Text text)
    {
        text.resizeTextForBestFit = true;
        text.resizeTextMinSize = 8;
        text.resizeTextMaxSize = text.fontSize;
    }

    Text CreateText(string name, Transform parent, string text, int size, TextAnchor alignment, FontStyle style = FontStyle.Normal)
    {
        return UIUtil.CreateText(name, parent, font, Color.white, text, size, alignment, style);
    }
}
