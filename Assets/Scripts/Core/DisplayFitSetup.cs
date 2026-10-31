using UnityEngine;
using UnityEngine.UI;
using static UIUtil;

//初回起動時の画面合わせ。設定ファイルが無いときにDisplayFitが付ける（Windowsのビルドのみ）
//
//  ウィンドウにゲーム画面の外枠とセンサーの位置（ピンクの線）を描くので、PCのキーボードでウィンドウの大きさと位置を動かして、
//  ピンクの線が筐体のセンサーに重なるように合わせる。センサーをタッチ（またはEnter）で確定して設定ファイルに保存する
//  大きさを変えるときは、センサーのある下辺の中央を動かさない
public class DisplayFitSetup : MonoBehaviour
{
    const float MoveSpeed = 0.05f;//1秒あたりに動く量（画面に対する割合）
    const float ScaleSpeed = 0.05f;//1秒あたりに変わる大きさ
    const float FastFactor = 5f;//Shiftを押している間の速さの倍率
    const float ConfirmLockSeconds = 1f;//開いた直後のタッチでは確定しない
    const float LineThickness = 12f;

    static readonly Color BackgroundColor = new Color(0.05f, 0.06f, 0.12f);
    static readonly Color FrameColor = new Color(0.3f, 0.8f, 1f);
    static readonly Color SensorColor = new Color(1f, 0.4f, 0.75f);

    private DisplayFit.Settings settings;
    private RectTransform canvas;
    private RectTransform bottomSensor, leftSensor, rightSensor;
    private Text valueText;
    private float openTime;

    void Start()
    {
        settings = DisplayFit.Current.Clone();
        openTime = Time.unscaledTime;
        BuildCanvas();
    }

    void BuildCanvas()
    {
        canvas = CreateCanvas("DisplayFitSetup", 300, 0f);//シーンの切り替えの覆い(200)よりも手前に描く
        DontDestroyOnLoad(canvas.gameObject);
        Font font = CreateFont();

        Stretch(CreateImage("Background", canvas, BackgroundColor).rectTransform);

        //ゲーム画面の外枠
        Place(CreateImage("FrameBottom", canvas, FrameColor).rectTransform, Vector2.zero, new Vector2(1f, 0f), new Vector2(0.5f, 0f), Vector2.zero, new Vector2(0f, LineThickness / 2f));
        Place(CreateImage("FrameTop", canvas, FrameColor).rectTransform, Vector2.up, Vector2.one, new Vector2(0.5f, 1f), Vector2.zero, new Vector2(0f, LineThickness / 2f));
        Place(CreateImage("FrameLeft", canvas, FrameColor).rectTransform, Vector2.zero, Vector2.up, new Vector2(0f, 0.5f), Vector2.zero, new Vector2(LineThickness / 2f, 0f));
        Place(CreateImage("FrameRight", canvas, FrameColor).rectTransform, Vector2.right, Vector2.one, new Vector2(1f, 0.5f), Vector2.zero, new Vector2(LineThickness / 2f, 0f));

        //センサーの位置（場所は画面の縦横比で変わるのでLayoutSensorsで決める）
        bottomSensor = CreateImage("BottomSensor", canvas, SensorColor).rectTransform;
        leftSensor = CreateImage("LeftSensor", canvas, SensorColor).rectTransform;
        rightSensor = CreateImage("RightSensor", canvas, SensorColor).rectTransform;

        //縮小すると文字も小さくなるので大きめにする
        Place(CreateText("Title", canvas, font, Color.white, "画面の大きさ合わせ（初回のみ）", 64, TextAnchor.MiddleCenter, FontStyle.Bold).rectTransform, new Vector2(0f, 260f), new Vector2(1600f, 90f));
        Place(CreateText("Guide", canvas, font, Color.white, "ピンクの線が筐体のセンサーに重なるように、PCのキーボードで合わせてください", 40, TextAnchor.MiddleCenter).rectTransform, new Vector2(0f, 170f), new Vector2(1600f, 60f));
        Place(CreateText("Keys", canvas, font, new Color(1f, 1f, 1f, 0.75f), "← → ↑ ↓ : 位置　　Z / X : 小さく / 大きく　　Shift : 速く　　R : 元に戻す", 34, TextAnchor.MiddleCenter).rectTransform, new Vector2(0f, 90f), new Vector2(1800f, 50f));
        valueText = CreateText("Value", canvas, font, FrameColor, "", 44, TextAnchor.MiddleCenter);
        Place(valueText.rectTransform, new Vector2(0f, -10f), new Vector2(1600f, 60f));
        Place(CreateText("Confirm", canvas, font, SensorColor, "センサーをタッチ（または Enter）で確定", 56, TextAnchor.MiddleCenter, FontStyle.Bold).rectTransform, new Vector2(0f, -120f), new Vector2(1600f, 80f));
    }

    void Update()
    {
        float speed = Time.unscaledDeltaTime * (Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift) ? FastFactor : 1f);

        if (Input.GetKey(KeyCode.LeftArrow)) settings.left -= MoveSpeed * speed;
        if (Input.GetKey(KeyCode.RightArrow)) settings.left += MoveSpeed * speed;
        if (Input.GetKey(KeyCode.DownArrow)) settings.bottom -= MoveSpeed * speed;
        if (Input.GetKey(KeyCode.UpArrow)) settings.bottom += MoveSpeed * speed;

        float grow = 0f;
        if (Input.GetKey(KeyCode.Z) || Input.GetKey(KeyCode.KeypadMinus)) grow -= ScaleSpeed * speed;
        if (Input.GetKey(KeyCode.X) || Input.GetKey(KeyCode.KeypadPlus)) grow += ScaleSpeed * speed;
        if (grow != 0f)
        {
            float centerX = settings.left + settings.scale / 2f;
            settings.scale = Mathf.Clamp(settings.scale + grow, DisplayFit.MinScale, 1f);
            settings.left = centerX - settings.scale / 2f;
        }

        if (Input.GetKeyDown(KeyCode.R)) settings = new DisplayFit.Settings();

        DisplayFit.Clamp(settings);
        DisplayFit.Preview(settings);
        valueText.text = $"大きさ {settings.scale * 100f:0.0}%　　左 {settings.left * 100f:0.0}%　　下 {settings.bottom * 100f:0.0}%";
        LayoutSensors();

        if (Time.unscaledTime - openTime >= ConfirmLockSeconds && ConfirmPressed()) Close();
    }

    //オープニングでは、InputManagerはコントローラーだけを読む（キーボードはEnterで確定する）
    bool ConfirmPressed()
    {
        if (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter)) return true;
        return InputManager.instance != null && InputManager.instance.AnyDown(0, InputManager.LaneCount);
    }

    //センサーの線を、筐体の画面下部と同じ比率で置く（SensorLayoutの図を参照）
    //斜面センサーは45°なので、画面の下の角から、横幅・高さとも 長さ/√2 の位置まで伸びる
    void LayoutSensors()
    {
        float width = canvas.rect.width;
        float slopeLength = width * SensorLayout.SlopeWidthRatio;
        float slopeSpan = slopeLength / Mathf.Sqrt(2f);
        float inset = LineThickness / 2f / Mathf.Sqrt(2f);//線が画面の外にはみ出さないよう、太さの半分だけ内側に寄せる

        PlaceLine(bottomSensor, new Vector2(width / 2f, LineThickness / 2f), width * SensorLayout.BottomWidthRatio, 0f);
        PlaceLine(leftSensor, new Vector2(slopeSpan / 2f + inset, slopeSpan / 2f + inset), slopeLength, -45f);
        PlaceLine(rightSensor, new Vector2(width - slopeSpan / 2f - inset, slopeSpan / 2f + inset), slopeLength, 45f);
    }

    static void PlaceLine(RectTransform line, Vector2 center, float length, float angle)
    {
        Place(line, Vector2.zero, Vector2.zero, new Vector2(0.5f, 0.5f), center, new Vector2(length, LineThickness));
        line.localEulerAngles = new Vector3(0f, 0f, angle);
    }

    void Close()
    {
        DisplayFit.Confirm(settings);
        Destroy(canvas.gameObject);
        Destroy(this);
    }
}
