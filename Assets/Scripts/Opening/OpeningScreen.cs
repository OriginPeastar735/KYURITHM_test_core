using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using static UIUtil;

//オープニング画面。UIはすべてこのスクリプトがコードで生成する（シーンにはこのコンポーネントだけあればよい）
//
//  タイトル → 学校・学科のマーク → クレジット → 最初に戻る…を繰り返し、どこかのセンサーが押されたらチュートリアルが必要か聞く
//  右斜面（必要）ならチュートリアル（Playシーン）へ、左斜面（不要）なら選曲画面へ移る。制限時間内に答えが無ければオープニングに戻る
//  次のシーンの裏読み込みは、白く覆っている間に済ませる。覆いはシーンが変わっても残し、次のシーンの上で薄くして外す
//
//マークの画像は Assets/Resources/Opening/ に次の名前で置く（無ければ仮の枠を出す）。Texture Type は Sprite (2D and UI)
//  SchoolSymbol … 石川高専のシンボルマーク
//  DeptLogo     … 電子情報工学科のロゴマーク
public class OpeningScreen : MonoBehaviour
{
    static readonly Color TextColor = new Color(0.12f, 0.13f, 0.16f);
    //チュートリアルを聞くパネルは、オープニングが透けないよう不透明な暗い背景に白い文字（選曲画面と同じ背景色）
    static readonly Color QuestionBackground = new Color(0.05f, 0.06f, 0.12f);
    static readonly Color QuestionTextColor = Color.white;

    [SerializeField] private float slideSeconds = 3.5f;//1枚を見せている時間（フェードの時間は含まない）
    [SerializeField] private float fadeSeconds = 0.4f;//1枚のフェードイン・フェードアウトそれぞれの時間
    [SerializeField] private float inputLockSeconds = 1f;//開始直後は入力を受け付けない（リザルトから戻ったときの押しっぱなし対策）
    [SerializeField] private float exitSeconds = 0.9f;//白く覆うまでの時間
    [SerializeField] private float revealSeconds = 0.6f;//次のシーンの上で覆いを外す時間
    [SerializeField] private float questionTimeoutSeconds = 20f;//チュートリアルが必要か聞く制限時間。答えが無ければオープニングに戻る

    //斜面のレーン（左斜面0〜7・右斜面24〜31）と、コントローラー無しでのテスト用キー（プレイ画面のキー配置と位置を合わせている）
    const int SlopeLaneCount = 8;
    static readonly KeyCode[] leftSlopeKeys = { KeyCode.A, KeyCode.S, KeyCode.LeftArrow };
    static readonly KeyCode[] rightSlopeKeys = { KeyCode.L, KeyCode.Semicolon, KeyCode.RightArrow };

    struct Credit
    {
        public string department, year, name;
        public Credit(string department, string year, string name)
        {
            this.department = department;
            this.year = year;
            this.name = name;
        }
    }

    //クレジット。左の列の上から、右の列の上へと並ぶ（5人ずつ）。差し替えるのはここだけ
    static readonly Credit[] Credits =
    {
        new Credit("電子情報工学科", "3年", "宮﨑 九十九"),
        new Credit("電気工学科", "1年", "細川 大耀"),
        new Credit("機械工学科", "3年", "撰 亮伸"),
        new Credit("電子情報工学科", "3年", "野村 ひとね"),
        new Credit("電子情報工学科", "3年", "井上 釉互"),
        new Credit("電子情報工学科", "4年", "濵田 大稀"),
        new Credit("電子情報工学科", "3年", "寺田 睦生"),
        new Credit("電子情報工学科", "1年", "嵐 奏人"),
        new Credit("電子情報工学科", "1年", "松原 快知"),
        new Credit("電子情報工学科", "3年", "北 聖也"),
    };

    //シーンが変わっても残る側で演出を回すための入れ物（このコンポーネントはシーンと一緒に消えるため）
    class Host : MonoBehaviour { }

    private Font font;
    private RectTransform canvasRect;
    private Host host;
    private readonly List<CanvasGroup> slides = new List<CanvasGroup>();
    private Text startText;
    private Image background;//オープニングの白い背景
    private Image flash;
    //答える斜面の光（左斜面: いいえ、右斜面: はい）。色は選曲画面の斜面と同じ
    static readonly Color NoColor = new Color(0.3f, 0.8f, 1f);
    static readonly Color YesColor = new Color(1f, 0.4f, 0.75f);
    class SlopeGlow
    {
        public Image image;
        public Color color;
        public float level;
    }
    private SlopeGlow noGlow, yesGlow;

    private CanvasGroup question;//チュートリアルが必要か聞くパネル
    private Text timerText;//パネル右上の制限時間
    private float startTime;
    private float questionTime;//パネルを出した時刻
    private bool asking;
    private bool leaving;

    void Start()
    {
        //オープニングにはInputManagerが置かれていないので、無ければ作る（斜面センサーで答えるため）
        //キーボードはこのクラスで専用に読むので、InputManager側はコントローラーのみにする
        if (InputManager.instance == null)
        {
            var input = new GameObject("InputManager").AddComponent<InputManager>();
            input.SetSource(InputSource.Controller);
        }

        startTime = Time.time;
        font = CreateFont();
        BuildCanvas();
        host.StartCoroutine(RunSlideshow());
    }

    void Update()
    {
        if (leaving) return;

        if (asking)
        {
            float remaining = questionTimeoutSeconds - (Time.time - questionTime);
            SetTimer(Mathf.Max(0, Mathf.CeilToInt(remaining)));
            UpdateGlow(noGlow, SlopeHeld(0, leftSlopeKeys));
            UpdateGlow(yesGlow, SlopeHeld(InputManager.LaneCount - SlopeLaneCount, rightSlopeKeys));
            if (LeftSlopePressed()) Leave(GameScene.MusicSelect);
            else if (RightSlopePressed()) StartTutorial();
            else if (remaining <= 0f) CancelAsk();
            return;
        }

        //「TOUCH TO START」をゆっくり点滅させる
        Color c = startText.color;
        c.a = 0.55f + 0.45f * Mathf.Sin(Time.time * 3f);
        startText.color = c;

        //初回起動の画面合わせ中は受け付けない。確定したタッチで質問が出ないよう、終わってから受け付けるまでの待ち時間を数え直す
        if (DisplayFit.IsSetupOpen)
        {
            startTime = Time.time;
            return;
        }
        //F9で画面合わせ・タイミング合わせをやり直す（今の設定から直す）
        if (Input.GetKeyDown(KeyCode.F9))
        {
            DisplayFit.OpenSetup();
            return;
        }
        if (Time.time - startTime >= inputLockSeconds && AnyPressed()) Ask();
    }

    bool AnyPressed()
    {
        if (InputManager.instance != null && InputManager.instance.AnyDown(0, InputManager.LaneCount)) return true;
        return Input.anyKeyDown;
    }

    bool LeftSlopePressed()
    {
        return SlopePressed(0, leftSlopeKeys);
    }

    bool RightSlopePressed()
    {
        return SlopePressed(InputManager.LaneCount - SlopeLaneCount, rightSlopeKeys);
    }

    bool SlopePressed(int firstLane, KeyCode[] keys)
    {
        if (InputManager.instance != null && InputManager.instance.AnyDown(firstLane, SlopeLaneCount)) return true;
        foreach (KeyCode key in keys)
        {
            if (Input.GetKeyDown(key)) return true;
        }
        return false;
    }

    // ===== チュートリアルが必要か聞く =====
    //押したフレームでは答えを読まないので、TOUCH TO STARTで斜面を押してもそのまま答えにはならない
    void Ask()
    {
        asking = true;
        questionTime = Time.time;
        startText.gameObject.SetActive(false);
        SetTimer(Mathf.CeilToInt(questionTimeoutSeconds));
        host.StartCoroutine(ShowQuestion(true));
    }

    //制限時間内に答えが無かったときは、パネルを消してTOUCH TO STARTの状態に戻る（スライドショーはそのまま続いている）
    void CancelAsk()
    {
        asking = false;
        startTime = Time.time;//戻った直後の押しっぱなしで、すぐにまた聞かないようにする
        startText.gameObject.SetActive(true);
        host.StartCoroutine(ShowQuestion(false));
    }

    //パネルを出し入れする。暗い背景が広がるようにフェードしながら、少し大きいところから元の大きさに縮める
    //（出し入れの間に答えても、表示が途中のまま次の画面へ移るだけで問題ない）
    IEnumerator ShowQuestion(bool show)
    {
        const float seconds = 0.5f;
        const float startScale = 1.08f;
        Transform panel = question.transform;
        yield return Animate(seconds, t =>
        {
            float e = EaseOut(t);
            float shown = show ? e : 1f - e;
            question.alpha = shown;
            panel.localScale = Vector3.one * Mathf.Lerp(startScale, 1f, shown);
        });
    }

    //残り3秒から赤くする
    void SetTimer(int seconds)
    {
        timerText.text = seconds.ToString();
        timerText.color = seconds <= 3 ? new Color(1f, 0.35f, 0.3f) : QuestionTextColor;
    }

    //曲を流さずにPlaySceneを開き、Tutorialが練習用のノーツを流す
    void StartTutorial()
    {
        PlaySession.IsTutorial = true;
        //チュートリアルは初心者向けの設定で遊ばせる。終わったらTutorialが元の設定に戻す
        //（設定はプレイ画面の開始時に読むので、シーンを読み込む前に変える）
        PlaySession.SettingsBeforeTutorial = PlayPreset.Capture();
        PlayPreset.Beginner.Apply();
        PlaySession.ChartName = "";
        PlaySession.AudioPath = "";
        PlaySession.WaitingForIntro = false;
        Leave(GameScene.Play);
    }

    // ===== スライドショー =====
    //スライドを順に、フェードイン→表示→フェードアウトで見せてループする
    IEnumerator RunSlideshow()
    {
        int index = 0;
        while (true)
        {
            CanvasGroup slide = slides[index];
            float total = fadeSeconds * 2f + slideSeconds;
            yield return Animate(total, t =>
            {
                float time = t * total;
                float fadeIn = Mathf.Clamp01(time / fadeSeconds);
                float fadeOut = Mathf.Clamp01((total - time) / fadeSeconds);
                slide.alpha = Mathf.Min(fadeIn, fadeOut);
            });
            slide.alpha = 0f;
            index = (index + 1) % slides.Count;
        }
    }

    // ===== 次のシーンへの移動 =====
    void Leave(GameScene next)
    {
        if (leaving) return;
        leaving = true;
        Destroy(startText.gameObject);
        startText = null;
        DontDestroyOnLoad(canvasRect.gameObject);
        host.StopAllCoroutines();
        host.StartCoroutine(RunLeave(next));
    }

    //白く覆いながらスライドを拡大 → 覆っている間に次のシーンへ切り替え → 白を薄くして外す
    IEnumerator RunLeave(GameScene next)
    {
        AsyncOperation loading = SceneLoader.PreloadScene(next);

        Transform[] shown = new Transform[slides.Count];
        float[] startScale = new float[slides.Count];
        for (int i = 0; i < slides.Count; i++)
        {
            shown[i] = slides[i].transform;
            startScale[i] = shown[i].localScale.x;
        }
        yield return Animate(exitSeconds, t =>
        {
            float e = t * t;//だんだん加速させる
            flash.color = new Color(1f, 1f, 1f, e);
            for (int i = 0; i < shown.Length; i++) shown[i].localScale = Vector3.one * Mathf.Lerp(startScale[i], startScale[i] * 1.25f, e);
        });

        while (loading.progress < 0.9f) yield return null;
        loading.allowSceneActivation = true;
        while (!loading.isDone) yield return null;

        //切り替わった後は、白い覆いだけを残して薄くする（背景も消さないと、覆いを外しても白いままで、最後にぱっと切り替わってしまう）
        foreach (CanvasGroup slide in slides) slide.alpha = 0f;
        question.alpha = 0f;
        background.enabled = false;
        yield return Animate(revealSeconds, t => flash.color = new Color(1f, 1f, 1f, 1f - EaseOut(t)));
        Destroy(canvasRect.gameObject);
    }

    // ===== 画面の組み立て =====
    void BuildCanvas()
    {
        //選曲画面に切り替わった後も、覆いが外れるまでは手前に描く
        //質問パネルの斜面の光をセンサーに合わせるため、選曲画面と同じく横幅基準で拡大縮小する
        canvasRect = CreateCanvas("OpeningCanvas", 50, 0f);
        host = canvasRect.gameObject.AddComponent<Host>();

        //マークの画像が白背景なので、全体を白にそろえる。文字は濃い色で書く
        background = CreateImage("Background", canvasRect, Color.white);
        Stretch(background.rectTransform);

        slides.Add(BuildTitleSlide());
        slides.Add(BuildLogoSlide());
        slides.Add(BuildCreditSlide());

        startText = CreateText("TouchToStart", canvasRect, "TOUCH TO START", 44, TextAnchor.MiddleCenter, FontStyle.Bold);
        Place(startText.rectTransform, new Vector2(0f, -400f), new Vector2(900f, 80f));

        question = BuildQuestion();

        flash = CreateImage("Flash", canvasRect, new Color(1f, 1f, 1f, 0f));
        Stretch(flash.rectTransform);
        flash.raycastTarget = false;
    }

    //タイトルのスライド。KYURITHMの文字を出す
    CanvasGroup BuildTitleSlide()
    {
        RectTransform slide = CreateSlide("TitleSlide");
        Text title = CreateText("Title", slide, "KYURITHM", 200, TextAnchor.MiddleCenter, FontStyle.Bold);
        Place(title.rectTransform, new Vector2(0f, 40f), new Vector2(1600f, 260f));
        return slide.GetComponent<CanvasGroup>();
    }

    //学校名・学科名は画像に入っているので文字は出さない
    CanvasGroup BuildLogoSlide()
    {
        RectTransform slide = CreateSlide("LogoSlide");
        BuildLogo(slide, "SchoolSymbol", "石川高専\nシンボルマーク\n(仮)", new Vector2(-470f, 40f));
        BuildLogo(slide, "DeptLogo", "電子情報工学科\nロゴマーク\n(仮)", new Vector2(470f, 40f));
        return slide.GetComponent<CanvasGroup>();
    }

    //画像があればそれを縦横比のまま表示し、無ければ仮の枠を出す
    //取り込み設定がSpriteでなくても使えるように、Textureとして読んでからSpriteにする
    void BuildLogo(RectTransform parent, string resourceName, string placeholder, Vector2 position)
    {
        Vector2 size = new Vector2(900f, 500f);
        Texture2D texture = Resources.Load<Texture2D>("Opening/" + resourceName);
        if (texture != null)
        {
            Sprite sprite = Sprite.Create(texture, new Rect(0f, 0f, texture.width, texture.height), new Vector2(0.5f, 0.5f));
            Image image = CreateImage(resourceName, parent, Color.white, sprite);
            image.preserveAspect = true;
            Place(image.rectTransform, position, size);
            return;
        }
        Image box = CreateImage(resourceName, parent, new Color(0f, 0f, 0f, 0.12f));
        Place(box.rectTransform, position, size);
        Text label = CreateText("Label", box.rectTransform, placeholder, 40, TextAnchor.MiddleCenter);
        Stretch(label.rectTransform);
    }

    CanvasGroup BuildCreditSlide()
    {
        const int rows = 5;
        RectTransform slide = CreateSlide("CreditSlide");
        Text heading = CreateText("Heading", slide, "CREDITS", 72, TextAnchor.MiddleCenter, FontStyle.Bold);
        Place(heading.rectTransform, new Vector2(0f, 380f), new Vector2(1000f, 100f));

        for (int i = 0; i < Credits.Length; i++)
        {
            Credit credit = Credits[i];
            int column = i / rows;
            int row = i % rows;
            Vector2 position = new Vector2(column == 0 ? -340f : 340f, 200f - row * 120f);//斜面センサーに隠れないよう、端には寄せない

            Text name = CreateText("Name" + i, slide, credit.name, 52, TextAnchor.MiddleCenter, FontStyle.Bold);
            Place(name.rectTransform, position + new Vector2(0f, 16f), new Vector2(640f, 64f));
            Text affiliation = CreateText("Affiliation" + i, slide, credit.department + "　" + credit.year, 30, TextAnchor.MiddleCenter);
            affiliation.color = Color.black;
            Place(affiliation.rectTransform, position + new Vector2(0f, -34f), new Vector2(640f, 40f));
        }
        return slide.GetComponent<CanvasGroup>();
    }

    //チュートリアルが必要か聞くパネル。スライドの上に不透明な暗い背景で重ね、答えは答える斜面の側に書く
    CanvasGroup BuildQuestion()
    {
        RectTransform panel = CreateSlide("Question");
        panel.gameObject.AddComponent<Image>().color = QuestionBackground;

        Text heading = CreateText("Heading", panel, "チュートリアルをプレイしますか？", 80, TextAnchor.MiddleCenter, FontStyle.Bold);
        heading.color = QuestionTextColor;
        Place(heading.rectTransform, new Vector2(0f, 180f), new Vector2(1600f, 120f));

        BuildAnswer(panel, "いいえ", new Vector2(-480f, -160f));
        BuildAnswer(panel, "はい", new Vector2(480f, -160f));

        //制限時間（右上）。選曲画面と同じ位置・大きさ
        var timer = CreateRect("Timer", panel);
        Place(timer, Vector2.one, Vector2.one, Vector2.one, new Vector2(-60f, -60f), new Vector2(260f, 120f));
        var label = CreateText("Label", timer, "制限時間", 28, TextAnchor.UpperRight);
        label.color = QuestionTextColor;
        Stretch(label.rectTransform);
        timerText = CreateText("Seconds", timer, "", 80, TextAnchor.LowerRight, FontStyle.Bold);
        Stretch(timerText.rectTransform);

        //選曲画面と同じく、答える斜面のセンサーに沿って光らせる
        Sprite gradient = SensorGlow.CreateGradientSprite();
        noGlow = BuildSlopeGlow(panel, true, "いいえ", NoColor, gradient);
        yesGlow = BuildSlopeGlow(panel, false, "はい", YesColor, gradient);
        return panel.GetComponent<CanvasGroup>();
    }

    SlopeGlow BuildSlopeGlow(RectTransform parent, bool left, string label, Color color, Sprite gradient)
    {
        RectTransform slope = SensorGlow.SlopeRect(parent, left ? "NoSlope" : "YesSlope", left);
        Image image = SensorGlow.WholeSlope(slope, "Glow", color, gradient);
        Text text = CreateText("Label", slope, label, 38, TextAnchor.MiddleCenter, FontStyle.Bold);
        text.color = QuestionTextColor;
        Place(text.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
              new Vector2(0f, SensorGlow.SlopeHeight / 2f), new Vector2(SensorGlow.SlopeLength / 2f, SensorGlow.SlopeHeight));
        return new SlopeGlow { image = image, color = color };
    }

    //触れている間は明るく、触れていない間はゆっくり明滅させる
    void UpdateGlow(SlopeGlow glow, bool held)
    {
        float target = held ? 1f : SensorGlow.IdleLevel;
        glow.level = Mathf.Lerp(glow.level, target, 1f - Mathf.Exp(-20f * Time.deltaTime));
        Color c = glow.color;
        c.a = glow.level;
        glow.image.color = c;
    }

    bool SlopeHeld(int firstLane, KeyCode[] keys)
    {
        if (InputManager.instance != null && InputManager.instance.AnyHeld(firstLane, SlopeLaneCount)) return true;
        foreach (KeyCode key in keys)
        {
            if (Input.GetKey(key)) return true;
        }
        return false;
    }

    void BuildAnswer(RectTransform parent, string answer, Vector2 position)
    {
        Text answerText = CreateText("Answer", parent, answer, 96, TextAnchor.MiddleCenter, FontStyle.Bold);
        answerText.color = QuestionTextColor;
        Place(answerText.rectTransform, position, new Vector2(600f, 120f));
    }

    //1枚分の入れ物。最初は透明
    RectTransform CreateSlide(string name)
    {
        RectTransform slide = CreateRect(name, canvasRect);
        Stretch(slide);
        slide.gameObject.AddComponent<CanvasGroup>().alpha = 0f;
        return slide;
    }

    // ===== UI部品 =====
    //濃い色の文字（オープニングのフォント）
    Text CreateText(string name, Transform parent, string text, int size, TextAnchor alignment, FontStyle style = FontStyle.Normal)
    {
        return UIUtil.CreateText(name, parent, font, TextColor, text, size, alignment, style);
    }
}
