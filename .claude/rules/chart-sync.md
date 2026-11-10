---
paths:
  - "Assets/Scripts/GamePlay/**"
  - "Assets/Scripts/Core/SongLibrary.cs"
  - "Assets/Resources/**/*.json"
---
# 譜面エディタと合わせておくもの

譜面エディタ（別リポジトリ `C:\Users\tsuku\WebSite\KYURITHM-ChartEditor`、エディタ本体は `templates/index.html`）の出力 JSON をゲームがそのまま読む。JSON の形式（`meta` と `notes`、1小節 = 1920tick、1拍 = 480tick、`lane` 0〜31 と `width`）はエディタの出力が正。

次をゲームとエディタでそろえる。**どちらかを変えたら、もう一方と、エディタの `SPEC` コマンドの説明（`NOTE_SPECS`）も直す。**

- 総ノーツ数の数え方（エディタの `getNoteCounts` → `meta.totalNotes`。ゲームは `NoteManager` で自分の数え方と比べて警告を出す）
- スライドの判定間隔（その位置の BPM で決まる）・曲がり方（直線・内曲がり・外曲がり）・分岐
- スライドの判定点の位置（エディタの `JUDGE` コマンド = `formatSlideJudgeReport`）
- BPM 変更の扱い（ノーツが来る秒だけが変わり、流れる速さは一定）

エディタを直すときは、そのリポジトリの `CLAUDE.md` に従う（push すると公開中のサイトが変わる）。このリポジトリにエディタのコピーを持ち込まない。
