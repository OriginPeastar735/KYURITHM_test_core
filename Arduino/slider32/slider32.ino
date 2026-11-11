// =====================================================================
//  32キー静電容量タッチスライダー  (Raspberry Pi Pico + MPR121 x4)
//  Unity InputManager.cs 互換:  0xAA + 4バイト(32レーン) を 1ms ごとに送信
//  環境: arduino-pico v6.3.0 / Adafruit_MPR121 v1.1.3
//
//  シリアルモニタからのコマンド (1文字送信):
//    d   : 診断モード (テキストで 生値/基準値/変化量 を表示)
//    e   : イベント表示 (押された/離れた瞬間だけ1行ずつ表示。ゆっくりすべらせたときの確認用)
//    b   : バイナリモード (Unity用。起動時はこちら)
//    r   : 全センサーを再初期化 (基準値の取り直し・未接続ユニットの再検出)
//    s   : 接続状態を表示
//    0〜3 : そのユニットだけ再初期化 (Unity の F5 から送られる)
//    + - : 基準値が指を追いかける遅さを1段階 上げる/下げる (調整用。全センサーを再初期化する。電源を入れ直すと FALL_LEVEL_DEFAULT に戻る)
// =====================================================================
#include <Wire.h>
#include <Adafruit_MPR121.h>

// ----- 配線設定 -----
constexpr uint8_t  PIN_SDA = 4;          // GP4 (物理ピン6)
constexpr uint8_t  PIN_SCL = 5;          // GP5 (物理ピン7)
constexpr uint32_t I2C_HZ  = 400000;     // MPR121の上限 400kHz

// ----- ユニット設定 -----
constexpr uint8_t NUM_UNITS = 4;
// 0: lane 0-7 (左斜面)  1: lane 8-15 (底面左)  2: lane 16-23 (底面右)  3: lane 24-31 (右斜面)
constexpr uint8_t ADDRS[NUM_UNITS] = { 0x5A, 0x5B, 0x5C, 0x5D };

// 各ユニットで「左から k 番目のキー」が MPR121 のどの電極(ELE)につながっているか
constexpr uint8_t ELE_MAP[NUM_UNITS][8] = {
  { 0, 1, 2, 3, 4, 5, 6, 7 },
  { 0, 1, 2, 3, 4, 5, 6, 7 },
  { 0, 1, 2, 3, 4, 8, 6, 9 },
  { 0, 1, 2, 3, 4, 5, 6, 7 },
};

// ----- 感度設定 -----
constexpr uint8_t TOUCH_TH   = 10;   // 変化量がこれを超えたら押下
constexpr uint8_t RELEASE_TH = 5;    // 変化量がこれを下回ったら解放

// ----- 基準値が指を追いかける遅さ (ゆっくりすべらせても反応させる) -----
// MPR121 は基準値を自動で追従させる。ライブラリの初期設定は「指が近づく方向」の追いかけがとても速く、
// 隣のキーからゆっくり入ってきた指を環境の変化とみなして消してしまう。そこで近づく方向だけ遅くする。
// 離れる方向の追いかけと、押されている間は基準値を止める動きは変えない。
// 段階が大きいほど遅い = ゆっくりの指を拾いやすいが、手をかざしたときなどのずれが消えるのにも時間がかかる。
struct FallFilter { uint8_t mhd, nhd, ncl, fdl; };   // MPR121 の MHDF / NHDF / NCLF / FDLF に書く値
constexpr FallFilter FALL_LEVELS[] = {
  { 0x01, 0x05, 0x01, 0x00 },   // 0: ライブラリの初期設定 (今までの動き。比べる基準)
  { 0x01, 0x01, 0x10, 0x00 },   // 1: 少し遅い
  { 0x01, 0x01, 0xFF, 0x02 },   // 2: メーカー推奨の値
  { 0x01, 0x01, 0xFF, 0x10 },   // 3: さらに遅い
  { 0x01, 0x01, 0xFF, 0xFF },   // 4: 一番遅い
};
constexpr uint8_t FALL_LEVEL_COUNT   = sizeof(FALL_LEVELS) / sizeof(FALL_LEVELS[0]);
constexpr uint8_t FALL_LEVEL_DEFAULT = 2;   // 起動時の段階。+ - で試して決まった段階をここに書く

// ----- 基準値の固定 (もう1つのやり方。true にすると上の段階は効かない) -----
// MPR121 は基準値を自動で追従させる。指がゆっくり入ってくると (隣のキーから横にすべらせたときなど)
// 「環境が変わった」とみなして基準値のほうが下がり、変化量がしきい値を超えないまま触れていないことになる。
// true にすると基準値の追従を止め、覚えた基準値からの変化量だけで判定する。
// 気温などで基準値がずれていく分は、そのユニットに誰も触れていない時間が RECAL_IDLE_MS 続くたびに覚え直して合わせる。
// false にすると今までどおり (MPR121 の自動追従)。
constexpr bool     FREEZE_BASELINE = false;
constexpr uint32_t RECAL_IDLE_MS   = 5000;   // 誰も触れていない時間がこれだけ続いたら基準値を覚え直す
constexpr uint32_t RECAL_SETTLE_MS = 30;     // 覚え直しにかける時間 (この間だけ自動追従に戻す。送信は止めない)

// ----- 送信周期 -----
constexpr uint32_t SEND_PERIOD_US = 1000;   // 1ms (1000回/秒)

// ----- 押しっぱなし自動復帰 (0で無効) -----
constexpr uint32_t STUCK_MS = 0;

// =====================================================================
Adafruit_MPR121 cap[NUM_UNITS];
bool     present[NUM_UNITS];
uint8_t  laneBytes[NUM_UNITS];
uint32_t heldSince[NUM_UNITS * 8];

enum Mode { MODE_BINARY, MODE_DIAG, MODE_EVENT };
Mode mode = MODE_BINARY;

uint8_t fallLevel = FALL_LEVEL_DEFAULT;   // 今の段階 (FALL_LEVELS の何番目か)

// 基準値の固定 (FREEZE_BASELINE) の状態
uint32_t lastTouchMs[NUM_UNITS];   // そのユニットに最後に触れていた時刻
uint32_t recalStartMs[NUM_UNITS];  // 基準値の覚え直しを始めた時刻
bool     recalibrating[NUM_UNITS]; // 覚え直しの途中 (自動追従に戻している間)

// そのユニットで使う電極の数 (ELE_MAP の一番大きい番号 + 1)
uint8_t electrodeCount(uint8_t u) {
  uint8_t n = 0;
  for (uint8_t k = 0; k < 8; k++) n = max<uint8_t>(n, ELE_MAP[u][k] + 1);
  return n;
}

// ECR (計測の設定) を書き換える。上位2ビットが基準値の扱い
//   0xC0: 自動追従あり。計測を始めた最初の値を基準値として読み込む
//   0x80: 自動追従あり。最初の値の上位5ビットだけを読み込む (今までの設定)
//   0x40: 自動追従なし。基準値は今入っている値のまま
void setEcr(uint8_t u, uint8_t mode) {
  cap[u].writeRegister(MPR121_ECR, 0x00);   // いったん停止してから切り替える
  cap[u].writeRegister(MPR121_ECR, mode | electrodeCount(u));
}

// 基準値の覚え直しを始める。今の値を基準値として読み込み、RECAL_SETTLE_MS 後に updateBaseline() が追従を止める
void startRecalibration(uint8_t u, uint32_t now) {
  setEcr(u, 0xC0);
  recalibrating[u] = true;
  recalStartMs[u]  = now;
  lastTouchMs[u]   = now;
}

// scan() から毎回呼ぶ。覚え直しを終えて追従を止める / 誰も触れていない時間が続いたら覚え直しを始める
void updateBaseline(uint8_t u, uint8_t touchedBits, uint32_t now) {
  if (!FREEZE_BASELINE) return;
  if (touchedBits) lastTouchMs[u] = now;

  if (recalibrating[u]) {
    if (now - recalStartMs[u] >= RECAL_SETTLE_MS) {
      setEcr(u, 0x40);
      recalibrating[u] = false;
    }
    return;
  }
  if (now - lastTouchMs[u] >= RECAL_IDLE_MS) startRecalibration(u, now);
}

bool initUnit(uint8_t u) {
  if (!cap[u].begin(ADDRS[u], &Wire, TOUCH_TH, RELEASE_TH)) return false;

  cap[u].writeRegister(MPR121_ECR, 0x00);        // 停止モード (設定変更のため)
  cap[u].writeRegister(MPR121_DEBOUNCE, 0x00);   // 内部デバウンスなし = 最小遅延
  const FallFilter &ff = FALL_LEVELS[fallLevel];  // 指が近づく方向の追いかけを今の段階の遅さにする
  cap[u].writeRegister(MPR121_MHDF, ff.mhd);
  cap[u].writeRegister(MPR121_NHDF, ff.nhd);
  cap[u].writeRegister(MPR121_NCLF, ff.ncl);
  cap[u].writeRegister(MPR121_FDLF, ff.fdl);
  recalibrating[u] = false;
  if (FREEZE_BASELINE) startRecalibration(u, millis());                  // 今の値を基準値として覚えてから追従を止める
  else cap[u].writeRegister(MPR121_ECR, 0x80 | electrodeCount(u));       // 計測開始 (自動追従)
  return true;
}

// 1ユニットだけ再初期化 (基準値の取り直し)。Unity の F5 から '0'〜'3' で呼ばれる
void reinitUnit(uint8_t u) {
  present[u]   = initUnit(u);
  laneBytes[u] = 0;
  for (uint8_t k = 0; k < 8; k++) heldSince[u * 8 + k] = 0;
  Wire.setClock(I2C_HZ);   // begin() で I2C のクロックが戻る場合に備えて設定し直す
}

void initAll() {
  for (uint8_t u = 0; u < NUM_UNITS; u++) {
    present[u]   = initUnit(u);
    laneBytes[u] = 0;
  }
  for (auto &h : heldSince) h = 0;
  Wire.setClock(I2C_HZ);
}

void printStatus() {
  Serial.println(F("--- status ---"));
  for (uint8_t u = 0; u < NUM_UNITS; u++) {
    Serial.printf("unit%u  0x%02X  lane %2u-%2u  %s\n",
                  u, ADDRS[u], u * 8, u * 8 + 7, present[u] ? "OK" : "NOT FOUND");
  }
  Serial.printf("touch=%u release=%u stuck=%lums freeze_baseline=%s\n", TOUCH_TH, RELEASE_TH, (unsigned long)STUCK_MS,
                FREEZE_BASELINE ? "on" : "off");
  Serial.printf("fall_level=%u/%u (+ - で変更)\n", fallLevel, FALL_LEVEL_COUNT - 1);
}

// 追いかけの遅さを1段階変えて、全センサーを初期化し直す (調整用)
void changeFallLevel(int8_t delta) {
  int8_t next = (int8_t)fallLevel + delta;
  if (next < 0 || next >= FALL_LEVEL_COUNT) return;   // 端ではそのまま
  fallLevel = next;
  initAll();
}

void scan() {
  uint32_t now = millis();
  for (uint8_t u = 0; u < NUM_UNITS; u++) {
    if (!present[u]) { laneBytes[u] = 0; continue; }

    uint16_t t = cap[u].touched();
    uint8_t  b = 0;
    for (uint8_t k = 0; k < 8; k++) {
      if ((t >> ELE_MAP[u][k]) & 1) b |= (1 << k);   // bit0 = 一番小さいレーン番号
    }
    laneBytes[u] = b;
    updateBaseline(u, b, now);

    if (STUCK_MS == 0) continue;
    bool reinit = false;
    for (uint8_t k = 0; k < 8; k++) {
      uint32_t &h = heldSince[u * 8 + k];
      if (b & (1 << k)) {
        if (h == 0) h = now ? now : 1;
        else if (now - h > STUCK_MS) reinit = true;
      } else {
        h = 0;
      }
    }
    if (reinit) {
      reinitUnit(u);
    }
  }
}

void sendPacket() {
  uint8_t pkt[5] = { 0xAA, laneBytes[0], laneBytes[1], laneBytes[2], laneBytes[3] };
  if (Serial.availableForWrite() >= (int)sizeof(pkt)) Serial.write(pkt, sizeof(pkt));
}

void printDiag() {
  Serial.printf("\n[fall_level=%u]\n", fallLevel);
  for (uint8_t u = 0; u < NUM_UNITS; u++) {
    if (!present[u]) { Serial.printf("u%u ---- not found\n", u); continue; }
    Serial.printf("u%u d:", u);
    for (uint8_t k = 0; k < 8; k++) {
      uint8_t e = ELE_MAP[u][k];
      int f  = cap[u].filteredData(e);
      int bl = cap[u].baselineData(e);
      Serial.printf(" %4d%c", bl - f, (laneBytes[u] >> k) & 1 ? '*' : ' ');
    }
    Serial.print("  F/B:");
    for (uint8_t k = 0; k < 8; k++) {
      uint8_t e = ELE_MAP[u][k];
      Serial.printf(" %u/%u", cap[u].filteredData(e), cap[u].baselineData(e));
    }
    Serial.println();
  }
}

// イベント表示: 前回から変わったレーンだけ「ON/OFF レーン番号 変化量 時刻」を1行で出す
void printEvents() {
  static uint8_t prev[NUM_UNITS] = {0, 0, 0, 0};
  for (uint8_t u = 0; u < NUM_UNITS; u++) {
    uint8_t diff = laneBytes[u] ^ prev[u];
    for (uint8_t k = 0; k < 8 && diff; k++) {
      if (!((diff >> k) & 1)) continue;
      uint8_t e = ELE_MAP[u][k];
      bool on = (laneBytes[u] >> k) & 1;
      Serial.printf("%s lane %2u  d=%3d  t=%lums\n", on ? "ON " : "OFF", u * 8 + k,
                    (int)cap[u].baselineData(e) - (int)cap[u].filteredData(e), (unsigned long)millis());
    }
    prev[u] = laneBytes[u];
  }
}

void handleCommands() {
  while (Serial.available()) {
    char c = Serial.read();
    if      (c == 'd') { mode = MODE_DIAG;   printStatus(); }
    else if (c == 'e') { mode = MODE_EVENT;  printStatus(); }
    else if (c == 'b') { mode = MODE_BINARY; }
    else if (c == 'r') { initAll(); if (mode == MODE_DIAG) printStatus(); }
    else if (c == 's') { printStatus(); }
    else if (c == '+' || c == '-') { changeFallLevel(c == '+' ? 1 : -1); if (mode != MODE_BINARY) printStatus(); }
    else if (c >= '0' && c < '0' + NUM_UNITS) { reinitUnit(c - '0'); }
  }
}

void setup() {
  Serial.begin(115200);
  Wire.setSDA(PIN_SDA);
  Wire.setSCL(PIN_SCL);
  Wire.begin();
  Wire.setClock(I2C_HZ);

  pinMode(LED_BUILTIN, OUTPUT);
  initAll();
}

void loop() {
  static uint32_t next     = micros();
  static uint32_t lastDiag = 0;

  handleCommands();

  if ((int32_t)(micros() - next) < 0) return;
  next += SEND_PERIOD_US;
  if ((int32_t)(micros() - next) > (int32_t)(SEND_PERIOD_US * 5)) next = micros();

  scan();

  bool any = laneBytes[0] | laneBytes[1] | laneBytes[2] | laneBytes[3];
  digitalWrite(LED_BUILTIN, any);   // どれか押されていたらLED点灯

  if (mode == MODE_BINARY) {
    sendPacket();
  } else if (mode == MODE_EVENT) {
    printEvents();
  } else if (millis() - lastDiag >= 100) {
    lastDiag = millis();
    printDiag();
  }
}
