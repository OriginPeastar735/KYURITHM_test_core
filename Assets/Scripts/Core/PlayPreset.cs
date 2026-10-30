using UnityEngine;

//おすすめプリセット1つ分。設定を変えるのが面倒な人向けに、設定カードの値をまとめて変える（タイミング調整は人や筐体で違うので変えない）
//選曲画面のプリセットカードと、チュートリアル（初心者向けで遊ばせる）で使う
public class PlayPreset
{
    public string name;
    public string description;
    public Color color;
    public float hiSpeed;
    public int wall;
    public int laneOpacity;
    public int lineDivision;
    public int lineOpacity;
    public int backgroundBrightness;
    public int keyBeam;
    public int noteThickness;
    public bool showFastLate;

    public static readonly PlayPreset Beginner = new PlayPreset
    {
        name = "初心者向け", description = "ゆっくり流れて見やすい設定", color = new Color(0.3f, 0.6f, 0.42f),
        hiSpeed = 3f, wall = 0, laneOpacity = 90, lineDivision = 4, lineOpacity = 50,
        backgroundBrightness = 70, keyBeam = 80, noteThickness = 100, showFastLate = false,
    };

    public static readonly PlayPreset Advanced = new PlayPreset
    {
        name = "上級者向け", description = "速く流れてノーツの間隔が詰まりにくい設定", color = new Color(0.62f, 0.3f, 0.36f),
        hiSpeed = 7f, wall = 0, laneOpacity = 90, lineDivision = 2, lineOpacity = 20,
        backgroundBrightness = 70, keyBeam = 50, noteThickness = 100, showFastLate = true,
    };

    //選曲画面のプリセットカードに並べる順
    public static readonly PlayPreset[] All = { Beginner, Advanced };

    //今の設定を写し取る（チュートリアルの後に元の設定へ戻すのに使う）
    public static PlayPreset Capture()
    {
        return new PlayPreset
        {
            hiSpeed = PlaySession.HiSpeed,
            wall = PlaySession.WallPercent,
            laneOpacity = PlaySession.LaneOpacityPercent,
            lineDivision = PlaySession.LineDivision,
            lineOpacity = PlaySession.LineOpacityPercent,
            backgroundBrightness = PlaySession.BackgroundBrightnessPercent,
            keyBeam = PlaySession.KeyBeamPercent,
            noteThickness = PlaySession.NoteThicknessPercent,
            showFastLate = PlaySession.ShowFastLate,
        };
    }

    public void Apply()
    {
        PlaySession.HiSpeed = hiSpeed;
        PlaySession.WallPercent = wall;
        PlaySession.LaneOpacityPercent = laneOpacity;
        PlaySession.LineDivision = lineDivision;
        PlaySession.LineOpacityPercent = lineOpacity;
        PlaySession.BackgroundBrightnessPercent = backgroundBrightness;
        PlaySession.KeyBeamPercent = keyBeam;
        PlaySession.NoteThicknessPercent = noteThickness;
        PlaySession.ShowFastLate = showFastLate;
    }

    //今の設定がこのプリセットと同じか（カードに「適用中」と出す）
    public bool IsApplied =>
        Mathf.Approximately(PlaySession.HiSpeed, hiSpeed)
        && PlaySession.WallPercent == wall
        && PlaySession.LaneOpacityPercent == laneOpacity
        && PlaySession.LineDivision == lineDivision
        && PlaySession.LineOpacityPercent == lineOpacity
        && PlaySession.BackgroundBrightnessPercent == backgroundBrightness
        && PlaySession.KeyBeamPercent == keyBeam
        && PlaySession.NoteThicknessPercent == noteThickness
        && PlaySession.ShowFastLate == showFastLate;
}
