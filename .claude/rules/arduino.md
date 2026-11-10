---
paths:
  - "Arduino/**"
  - "Assets/Scripts/Core/InputManager.cs"
---
# 専用コントローラー（`Arduino/slider32/`）

- Raspberry Pi Pico ＋ MPR121 ×4。環境は arduino-pico v6.3.0 / Adafruit_MPR121 v1.1.3
- 1ms ごとに `0xAA` ＋ 4バイト（lane 0〜7 / 8〜15 / 16〜23 / 24〜31、各バイトの bit0 が一番小さいレーン）を送る。115200bps
- ポートは既定 COM3（パソコンによって変わることがある）。`InputManager` の `portName` で合わせ、自分の環境の値をシーンごとコミットしない
- Unity から 1文字のコマンドを受け取る（`0`〜`3`: そのユニットだけ再初期化 = F5、`r`: 全体の再初期化 など）
- **通信の形を変えるときは `slider32.ino` と `InputManager.cs` を必ず両方直す**
- Claude はマイコンに書き込めない。書き込みと動作確認は Arduino IDE でユーザーが行うので、その手順を伝える
