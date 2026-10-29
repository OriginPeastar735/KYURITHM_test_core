using System.Collections;
using System.Collections.Generic;
using UnityEngine;

//レーンのライトアップ。InputManagerの入力状態に合わせて透明度を変える
public class KeyBeamFade : MonoBehaviour
{
    [SerializeField] private float Speed = 3;//離したあとに薄くなる速さ（1秒あたりのアルファの減り）
    [SerializeField] private float pressedAlfa = 0.3f;//押している間のアルファ
    public int laneIndex = 0;//担当するレーン番号（LaneFieldBuilderが設定）
    private Renderer rend;
    private float alfa = 0;

    void Start()
    {
        rend = GetComponent<Renderer>();
        SetAlfa(0);
    }

    void Update()
    {
        //リザルト表示中は押しても光らせない（光っていたものはそのまま消えていく）
        bool isPressed = !ResultScreen.IsShowing && InputManager.instance != null && InputManager.instance.laneHeld[laneIndex];

        if (isPressed)
        {
            alfa = pressedAlfa;
        }
        else if (alfa > 0)
        {
            alfa = Mathf.Max(0, alfa - Speed * Time.deltaTime);
        }
        else
        {
            return;//透明のままなら更新しない
        }
        SetAlfa(alfa);
    }

    void SetAlfa(float a)
    {
        Color c = rend.material.color;
        rend.material.color = new Color(c.r, c.g, c.b, a);
        //透明にしても光の反射は残って薄く見えるので、透明のときは描画しない
        rend.enabled = a > 0;
    }
}
