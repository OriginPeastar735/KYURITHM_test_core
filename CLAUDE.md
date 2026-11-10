# KYURITHM — Claude 向けメモ

専用タッチセンサー（左斜面8・底面16・右斜面8 = 32レーン）で遊ぶ音楽ゲーム。Unity 6000.6.5f1（URP）＋ C#。個人・サークルでの制作。
ゲームの仕様（ノーツ・判定・スコア・操作・曲データの置き方）は [README.md](README.md) を読む。
コントローラー（Arduino）と譜面エディタの決まりは `.claude/rules/` にあり、関係するファイルを読んだときに読み込まれる。

## このプロジェクトの決まり

- コードのコメントは日本語。今のコードと同じく、行末の `//` で「何のためか」を短く書く
- コミットメッセージは日本語で、版名から始めて何が変わったかを並べる（例: `γ版: 〇〇の追加、△△の修正`）
- 確認用のテストファイル（テスト譜面、テスト用スクリプトなど）は作ってよい
- 曲データ（`Assets/StreamingAssets/Songs/`）は Google Drive で管理していて git には入れない。譜面の作成・修正は頼まれない限りしない
- Claude は Unity を動かせない。変更後の確認手順は「どのシーンを開いて再生し、何を押すと何が起きれば成功か」で書く

## Unity で気をつけること

- **シーン（`.unity`）・プレハブ（`.prefab`）・`.asset`・`.mat` をテキストで直接書き換えない。** Inspector での操作が必要なときは、手順（どのオブジェクトの、どのコンポーネントの、どの欄を何にする）を伝える。共同開発でシーンが衝突しやすいので、なるべくコード側で済ませる
- 新しいスクリプトを作ったら「Unity を開いて読み込ませ、Unity が作る `.meta` も一緒にコミットする」と伝える
- アセットの移動・名前変更は Unity 上で行ってもらう（`.meta` の対応が崩れるため）
- `Library/` `Temp/` `Logs/` `UserSettings/`、`*.csproj` `*.slnx`（Unity が自動で作る）、`output_*`（ビルド）は触らない
- 入力は **旧 Input Manager**（`Input.GetKey` など）。新しい Input System のコードは書かない
- コンパイルエラーは、VS Code の診断（getDiagnostics）が使えればそれで確かめる

## コードの勘所（`Assets/Scripts/`）

### シーンとデータ

```
Opening → MusicSelect → Play（曲が終わると同じシーン内でリザルト）
            ↑__________________|  を3曲（PlaySession.MaxTracks）くり返す
          → Result（3曲分のまとめ） → Ending → Opening
Opening からチュートリアルを選ぶと Play（PlaySession.IsTutorial）→ MusicSelect
```

- シーン切り替えは必ず `SceneLoader.ChangeScene` / `PreloadScene` を使う。`GameScene` の名前は Unity のシーン名と同じにする
- シーンをまたぐデータはすべて static の `PlaySession` に置く
- ハイスコア（`HighScoreStore`）はメモリにだけ持つ。ゲームを終了すると消えるのは仕様（展示で次の人に引き継がないため）
- 筐体ごとの調整（画面の大きさ・位置、タイミング）は `DisplayFit` が `display_fit.json` に保存する。プレイヤーの「楽曲タイミング」（`PlaySession.SongOffsetMs`）はそれに足して使う
- 設定項目を増やしたら `PlayPreset` の `Capture` / `Apply` と各プリセットにも足す

### 入力と判定

- 入力は `InputManager` だけを通して読む（`laneDown` / `laneHeld` / `laneUp`）。判定やメニューから `Input` やシリアルを直接読まない
- 判定は、入力を受け取った時刻（`Stopwatch.GetTimestamp()`）を `MusicManager.ToPlayTime` で曲の時刻に直して行う。**判定にフレームの時刻（`Time.time` など）を使わない**
- 実行順 `MusicManager`（-110）→ `InputManager`（-100）→ `JudgeManager` を崩さない
- 「何か押したら進む」画面は `InputManager.AnyPressed()` を使う（ファンクションキーでは進まない）

### 運営用キー

F1: 入力元の切り替え ／ F5: センサーリセット（どのシーンでも効く） ／ F9: オープニングで画面合わせ・タイミング合わせ ／ F12: プレイスキップ

運営用の操作を増やすときはファンクションキーに割り当て、実行したら `Notice.Show(...)` で画面に知らせる。

### UI

UI はシーンに置かず、`UIUtil`（`CreateCanvas` / `CreateText` / `CreateImage` / `Place` など）でコードから作る（例: `MusicSelectView`、`Notice`）。新しい UI も同じやり方で作る。
