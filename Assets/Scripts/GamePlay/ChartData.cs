using System;
using System.Collections.Generic;

//譜面のノーツ1つ分（type: tap / hold / slide / bpm）
//tickはエディタのMOVE/QUANTIZEで小数になることがあるのでfloatで読む
[Serializable]
public class ChartNoteData
{
    public string type;
    public int lane;
    public int width;
    public float tick;
    public float bpm;//BPM変更線の値
    public List<SlideSection> sections;//slideのセクション（slideのみ）

    //slideの分岐。親slideの中間ノーツ(parentMidId)につながっている。
    //branchTypeが"end"なら始点が、"start"なら終点が親の中間ノーツと同じ位置になる
    //idはエディタ上で数値にも文字列にもなるのでobjectで受け取る
    public object parentId;
    public object parentMidId;
    public string branchType;
}

//slideの1セクション（from→to）と曲がり方
[Serializable]
public class SlideSection
{
    public SlidePointer from;
    public SlidePointer to;
    public string curve;
}

//slideのセクションの始点・終点
[Serializable]
public class SlidePointer
{
    public int lane;
    public int width;
    public float tick;
}

//譜面の"meta"部分
[Serializable]
public class ChartMetaData
{
    public string title;
    public string artist;
    public float bpm;
    public float offset;//ms。正の値だとノーツが曲に対して遅れて来る
    public int? totalNotes;//エディタで数えた総ノーツ数（読み込み時の確認に使う）
}

//譜面json全体（譜面エディタの出力そのまま）。NoteManager.LoadJsonで読み込む
[Serializable]
public class ChartData
{
    public ChartMetaData meta;
    public List<ChartNoteData> notes;
}
