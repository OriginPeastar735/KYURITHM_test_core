// シーンの切り替えを持つstaticクラス
using UnityEngine;
using UnityEngine.SceneManagement;

public static class SceneLoader
{
    public static GameScene CurrentScene { get; private set; }//今のシーン（書き換えはこのクラスの中だけで行う）

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
        CurrentScene = next;
        AsyncOperation operation = SceneManager.LoadSceneAsync(next.ToString());//シーン名はGameSceneの名前と同じ
        operation.allowSceneActivation = false;
        return operation;
    }
}
