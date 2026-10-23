using System.Collections;
using TMPro;
using UnityEngine;

//曲が終わったら（全ノーツが消えたら）シーンを切り替えずに、Canvas上のパネル画像を割り込ませてリザルトを出す
//
//  1. 最後のノーツから少し待つ（delay秒）
//  2. パネルを画面外からスライドさせながら表示する（slideIn秒）
//  3. スコアと判定数を0から数え上げる（countUp秒）
//
//panelはCanvasの子に置いた画像(Image)。最初は非表示にしておき、GameManagerからShowで呼び出す
public class ResultScreen : MonoBehaviour
{
    [SerializeField] private RectTransform panel;//割り込ませるリザルト画像
    [SerializeField] private CanvasGroup group;//panelに付けたCanvasGroup（フェード用）

    [Header("表示するテキスト")]
    [SerializeField] private TextMeshProUGUI scoreText;
    [SerializeField] private TextMeshProUGUI maxComboText;
    [SerializeField] private TextMeshProUGUI precisePlusText;
    [SerializeField] private TextMeshProUGUI preciseText;
    [SerializeField] private TextMeshProUGUI approxText;
    [SerializeField] private TextMeshProUGUI missText;

    [Header("演出(秒)")]
    public float delay = 1.0f;
    public float slideIn = 0.4f;
    public float countUp = 1.0f;
    public Vector2 slideFrom = new Vector2(1920f, 0f);//表示位置からどれだけずらした所から入ってくるか

    //SetActive(false)で隠すと、このコンポーネントがpanelやその親に付いていた場合にコルーチンが動かなくなるので、透明にして隠す
    void Awake()
    {
        group.alpha = 0f;
        group.blocksRaycasts = false;
    }

    //result: GameManager.PlayResult (score, combo, maxCombo, precisePlus, precise, approx, miss)
    //hideOnShow: パネルが出始めると同時に非アクティブにするもの（なければnull）
    public void Show(int[] result, GameObject hideOnShow = null)
    {
        //エディタで非表示にしてあってもコルーチンを動かせるように、先に表示状態にする（初回はここでAwakeが呼ばれる）
        gameObject.SetActive(true);
        StartCoroutine(Run((int[])result.Clone(), hideOnShow));//表示中に値が変わらないようコピーしておく
    }

    IEnumerator Run(int[] result, GameObject hideOnShow)
    {
        yield return new WaitForSeconds(delay);

        if (hideOnShow != null) hideOnShow.SetActive(false);

        SetCounts(result, 0f);

        //2. 画面外から表示位置へスライドさせる
        Vector2 shownPos = panel.anchoredPosition;
        panel.gameObject.SetActive(true);
        group.blocksRaycasts = true;
        yield return MusicSelectView.Animate(slideIn, t =>
        {
            float e = MusicSelectView.EaseOut(t);
            panel.anchoredPosition = shownPos + slideFrom * (1f - e);
            group.alpha = e;
        });

        //3. スコアと判定数を同時に数え上げる
        yield return MusicSelectView.Animate(countUp, t => SetCounts(result, MusicSelectView.EaseOut(t)));
    }

    //最終値にrate(0〜1)を掛けた値を表示する
    void SetCounts(int[] result, float rate)
    {
        scoreText.text = Mathf.RoundToInt(result[0] * rate).ToString();
        maxComboText.text = Mathf.RoundToInt(result[2] * rate).ToString();
        precisePlusText.text = Mathf.RoundToInt(result[3] * rate).ToString();
        preciseText.text = Mathf.RoundToInt(result[4] * rate).ToString();
        approxText.text = Mathf.RoundToInt(result[5] * rate).ToString();
        missText.text = Mathf.RoundToInt(result[6] * rate).ToString();
    }
}
