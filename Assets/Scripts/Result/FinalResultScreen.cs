using UnityEngine;

//Resultシーン。規定の曲数を遊び終えた後、エンディングの前に通る（1曲ごとのリザルトはPlayシーンのResultScreenが出す）
//何も表示せず、すぐエンディングへ移る。全曲分の結果などを出すときはここに追加する
public class FinalResultScreen : MonoBehaviour
{
    void Start()
    {
        SceneLoader.ChangeScene(GameScene.Ending);
    }
}
