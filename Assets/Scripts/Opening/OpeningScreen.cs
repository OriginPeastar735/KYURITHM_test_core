using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using static UIUtil;

//オープニング画面。UIはすべてこのスクリプトがコードで生成する（シーンにはこのコンポーネントだけあればよい）
//
//  タイトル → 学校・学科のマーク → クレジット → 最初に戻る…を繰り返し、何かキーが押されたら選曲画面へ移る
//  選曲画面の裏読み込みは、白く覆っている間に済ませる。覆いはシーンが変わっても残し、選曲画面の上で薄くして外す
//
//マークの画像は Assets/Resources/Opening/ に次の名前で置く（無ければ仮の枠を出す）。Texture Type は Sprite (2D and UI)
//  SchoolSymbol … 石川高専のシンボルマーク
//  DeptLogo     … 電子情報工学科のロゴマーク
public class OpeningScreen : MonoBehaviour
{
    static readonly Color TextColor = new Color(0.12f, 0.13f, 0.16f);
    static readonly Color SubTextColor = new Color(0.12f, 0.13f, 0.16f, 0.6f);

    [SerializeField] private float slideSeconds = 4f;//1枚を見せている時間（フェードの時間は含まない）
    [SerializeField] private float fadeSeconds = 0.4f;//1枚のフェードイン・フェードアウトそれぞれの時間
    [SerializeField] private float inputLockSeconds = 0.5f;//開始直後は入力を受け付けない（リザルトから戻ったときの押しっぱなし対策）
    [SerializeField] private float exitSeconds = 0.9f;//白く覆うまでの時間
    [SerializeField] private float revealSeconds = 0.6f;//選曲画面の上で覆いを外す時間

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
        new Credit("電子情報工学科", "3年", "名前（仮）"),
        new Credit("電子情報工学科", "4年", "名前（仮）"),
        new Credit("電気工学科", "1年", "名前（仮）"),
        new Credit("電子情報工学科", "年", "名前（仮）"),
        new Credit("電子情報工学科", "年", "名前（仮）"),
        new Credit("電子情報工学科", "年", "名前（仮）"),
        new Credit("電子情報工学科", "○年", "名前（仮）"),
        new Credit("電子情報工学科", "○年", "名前（仮）"),
        new Credit("電子情報工学科", "○年", "名前（仮）"),
        new Credit("電子情報工学科", "○年", "名前（仮）"),
    };

    //シーンが変わっても残る側で演出を回すための入れ物（このコンポーネントはシーンと一緒に消えるため）
    class Host : MonoBehaviour { }

    private Font font;
    private RectTransform canvasRect;
    private Host host;
    private readonly List<CanvasGroup> slides = new List<CanvasGroup>();
    private Text pressKeyText;
    private Image flash;
    private float startTime;
    private bool leaving;

    void Start()
    {
        startTime = Time.time;
        font = CreateFont();
        BuildCanvas();
        host.StartCoroutine(RunSlideshow());
    }

    void Update()
    {
        if (leaving || pressKeyText == null) return;

        //「PRESS ANY KEY」をゆっくり点滅させる
        Color c = pressKeyText.color;
        c.a = 0.55f + 0.45f * Mathf.Sin(Time.time * 3f);
        pressKeyText.color = c;

        if (Time.time - startTime >= inputLockSeconds && AnyPressed()) Leave();
    }

    bool AnyPressed()
    {
        if (InputManager.instance != null && InputManager.instance.AnyDown(0, InputManager.LaneCount)) return true;
        return Input.anyKeyDown;
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

    // ===== 選曲画面への移動 =====
    void Leave()
    {
        if (leaving) return;
        leaving = true;
        Destroy(pressKeyText.gameObject);
        pressKeyText = null;
        DontDestroyOnLoad(canvasRect.gameObject);
        host.StopAllCoroutines();
        host.StartCoroutine(RunLeave());
    }

    //白く覆いながらスライドを拡大 → 覆っている間に選曲画面へ切り替え → 白を薄くして外す
    IEnumerator RunLeave()
    {
        AsyncOperation loading = SceneLoader.PreloadScene(GameScene.MusicSelect);

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

        //切り替わった後は、白だけを残して薄くする
        foreach (CanvasGroup slide in slides) slide.alpha = 0f;
        yield return Animate(revealSeconds, t => flash.color = new Color(1f, 1f, 1f, 1f - EaseOut(t)));
        Destroy(canvasRect.gameObject);
    }

    // ===== 画面の組み立て =====
    void BuildCanvas()
    {
        canvasRect = CreateCanvas("OpeningCanvas", 50, 0.5f);//選曲画面に切り替わった後も、覆いが外れるまでは手前に描く
        host = canvasRect.gameObject.AddComponent<Host>();

        //マークの画像が白背景なので、全体を白にそろえる。文字は濃い色で書く
        Stretch(CreateImage("Background", canvasRect, Color.white).rectTransform);

        slides.Add(BuildTitleSlide());
        slides.Add(BuildLogoSlide());
        slides.Add(BuildCreditSlide());

        pressKeyText = CreateText("PressAnyKey", canvasRect, "PRESS ANY KEY", 44, TextAnchor.MiddleCenter, FontStyle.Bold);
        Place(pressKeyText.rectTransform, new Vector2(0f, -400f), new Vector2(900f, 80f));

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
            affiliation.color = SubTextColor;
            Place(affiliation.rectTransform, position + new Vector2(0f, -34f), new Vector2(640f, 40f));
        }
        return slide.GetComponent<CanvasGroup>();
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
