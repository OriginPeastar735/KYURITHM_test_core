using System.Collections;
using System.Collections.Generic;
using UnityEngine;

//レーンのライトアップ。InputManagerの入力状態に合わせて透明度を変える
public class railfade : MonoBehaviour
{
    [SerializeField] private float Speed = 3;
    [SerializeField] private float pressedAlfa = 0.3f;
    public int laneIndex = 0;//担当するレーン番号（LaneFieldBuilderが設定）
    private Renderer rend;
    private float alfa = 0;

    // Start is called before the first frame update
    void Start()
    {
        rend = GetComponent<Renderer>();
        SetAlfa(0);
    }

    // Update is called once per frame
    void Update()
    {
        bool isPressed = InputManager.instance != null && InputManager.instance.laneHeld[laneIndex];

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
    }
}
