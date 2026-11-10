// シーンの切り替えを持つstaticクラス
using UnityEngine;
using UnityEngine.SceneManagement;

public static class SceneLoader
{
    public static GameScene CurrentScene { get; private set; }//今のシーン（書き換えはこのクラスの中だけで行う）

    //裏で読み込んでいるシーンがあり、まだ切り替わっていない間true（この間に別のシーンへ切り替えると読み込みがぶつかる）
    public static bool IsSwitching { get; private set; }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void Init()
    {
        IsSwitching = false;
        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        IsSwitching = false;
    }

    //運営の全スキップ（F12）。どの画面からでも、今のプレイを打ち切ってオープニングへ戻る
    //遊び終えた曲のハイスコアは残るが、遊んでいる途中の曲は記録しない。設定・曲数は次のプレイヤー向けに戻す
    public static void ReturnToTitle()
    {
        if (DisplayFit.IsSetupOpen) return;//画面合わせ・タイミング合わせの途中では効かない
        if (IsSwitching)
        {
            Notice.Show("画面の切り替え中です。切り替わってからもう一度押してください");
            return;
        }
        //楽曲紹介の途中なら、紹介画面（シーンが変わっても残る）も消す
        IntroScreen intro = Object.FindAnyObjectByType<IntroScreen>();
        if (intro != null) Object.Destroy(intro.gameObject);

        PlaySession.EndSession();
        Notice.Show("スキップしてタイトルに戻りました");
        ChangeScene(GameScene.Opening);
    }

    public static void ChangeScene(GameScene next)
    {
        CurrentScene = next;

        switch (next)
        {
            case GameScene.Opening:
                SceneManager.LoadScene("Opening");
                break;

            case GameScene.MusicSelect:
                SceneManager.LoadScene("MusicSelect");
                break;

            case GameScene.Play:
                SceneManager.LoadScene("Play");
                break;

            case GameScene.Result:
                SceneManager.LoadScene("Result");
                break;

            case GameScene.Ending:
                SceneManager.LoadScene("Ending");
                break;
        }
    }

    // 次のシーンを裏で読み込んでおく（演出を出している間に読み込みを済ませる用）
    // 読み込みが終わってもすぐには切り替わらない。切り替えるときは戻り値の allowSceneActivation を true にする
    public static AsyncOperation PreloadScene(GameScene next)
    {
        IsSwitching = true;
        CurrentScene = next;
        AsyncOperation operation = SceneManager.LoadSceneAsync(next.ToString());//シーン名はGameSceneの名前と同じ
        operation.allowSceneActivation = false;
        return operation;
    }
}
