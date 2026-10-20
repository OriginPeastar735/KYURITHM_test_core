// ChangeSceneだけを持つstaticクラス
using UnityEngine.SceneManagement;

public static class SceneLoader
{
    public static GameScene CurrentScene { get; private set; }//外部から参照はできるが，書き込みはできない

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
        }
    }
}
