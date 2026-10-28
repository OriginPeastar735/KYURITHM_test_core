using UnityEngine;

//筐体のセンサーの画面上の位置。選曲画面のボタン表示とプレイ画面の判定ラインはこれに合わせる
//
//筐体の画面下部（正面から見た図）。底面センサーの長さ2に対して斜面センサーは長さ1で45°傾いている
//  ┌──────────────────────────────┐
//  │＼                          ／│
//  │隠＼                      ／隠│  ← 斜面センサーで隠れて見えない三角形
//  └───┴──────────────────┴───┘
//  1/√2          2           1/√2   ← 画面下辺の比
public static class SensorLayout
{
    public const float SlopeAngle = 45f;//斜面センサーの傾き（度）

    //画面の横幅に対する長さの割合
    public static readonly float SlopeWidthRatio = 1f / (2f + Mathf.Sqrt(2f));//斜面センサー1本分の長さ
    public static readonly float BottomWidthRatio = SlopeWidthRatio * 2f;//底面センサーの長さ
}
