// =====================================================================
//  32キー静電容量タッチスライダー  (Raspberry Pi Pico + MPR121 x4)
//  Unity InputManager.cs 互換:  0xAA + 4バイト(32レーン) を 1ms ごとに送信
//  環境: arduino-pico v6.3.0 / Adafruit_MPR121 v1.1.3
//
//  シリアルモニタからのコマンド (1文字送信):
//    d : 診断モード (テキストで 変化量 と 基準値/計測値 を表示。* = 押下, + = 隣のレーンとして押下)
//    b : バイナリモード (Unity用。起動時はこちら)
//    l : ログモード (全レーンの変化量と押下状態を 1行ずつ、約200回/秒で送る。Arduino/capture_sensor_log.py で記録する)
//    r : 全センサーを再初期化 (基準値の取り直し・未接続ユニットの再検出)
//    s : 接続状態を表示
//    0〜3 : そのユニットだけ再初期化 (Unity の F5 から送られる)
//
//  隣のレーンの感度:
//    指を横にすべらせて隣のセンサーに入るときは、静電容量がゆっくり増えるので TOUCH_TH に届く前に
//    前のセンサーから離れてしまい、途切れることがある。そこで、押されているレーンのすぐ隣だけは
//    変化量が NEIGHBOR_TOUCH_TH を超えたら押下とみなす (MPR121の判定とは別に、このプログラムで判定する)。
//    押されているレーンが無いところのしきい値は変わらないので、離れた場所の誤反応は増えない
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
constexpr uint8_t TOUCH_TH   = 12;   // 変化量がこれを超えたら押下
constexpr uint8_t RELEASE_TH = 6;    // 変化量がこれを下回ったら解放
// 押されているレーンのすぐ隣だけ使う押下のしきい値 (0で無効)。TOUCH_TH より小さく、RELEASE_TH より大きくする
constexpr uint8_t NEIGHBOR_TOUCH_TH = 8;

// ----- 送信周期 -----
constexpr uint32_t SEND_PERIOD_US = 1000;   // 1ms (1000回/秒)
constexpr uint32_t LOG_PERIOD_US  = 5000;   // ログモードで1行送る間隔 (5ms = 200回/秒)

// ----- 押しっぱなし自動復帰 (0で無効) -----
constexpr uint32_t STUCK_MS = 0;

// ----- MPR121 のレジスタ -----
constexpr uint8_t REG_FILTERED = 0x04;   // 計測値 (電極ごとに2バイト、下位バイトが先、10bit)
constexpr uint8_t REG_BASELINE = 0x1E;   // 基準値 (電極ごとに1バイト、上位8bit)

// =====================================================================
Adafruit_MPR121 cap[NUM_UNITS];
bool     present[NUM_UNITS];
uint8_t  numEle[NUM_UNITS];      // 計測する電極の数 (ELE_MAP の最大 + 1)
uint8_t  laneBytes[NUM_UNITS];   // 送信する押下状態 (MPR121の判定 + 隣のレーンとしての押下)
uint8_t  hwBytes[NUM_UNITS];     // MPR121 が判定した押下
uint32_t softMask;               // 隣のレーンとして押下とみなしているレーン (bit i = lane i)
int16_t  delta[NUM_UNITS][8];    // 変化量 (基準値 - 計測値)。読んだユニットだけ更新する
uint32_t heldSince[NUM_UNITS * 8];

enum Mode { MODE_BINARY, MODE_DIAG, MODE_LOG };
Mode mode = MODE_BINARY;

bool initUnit(uint8_t u) {
  if (!cap[u].begin(ADDRS[u], &Wire, TOUCH_TH, RELEASE_TH)) return false;

  uint8_t n = 0;
  for (uint8_t k = 0; k < 8; k++) n = max<uint8_t>(n, ELE_MAP[u][k] + 1);
  numEle[u] = n;

  cap[u].writeRegister(MPR121_ECR, 0x00);        // 停止モード (設定変更のため)
  cap[u].writeRegister(MPR121_DEBOUNCE, 0x00);   // 内部デバウンスなし = 最小遅延
  cap[u].writeRegister(MPR121_ECR, 0x80 | n);    // 計測開始
  return true;
}

// 1ユニットだけ再初期化 (基準値の取り直し)。Unity の F5 から '0'〜'3' で呼ばれる
void reinitUnit(uint8_t u) {
  present[u]   = initUnit(u);
  laneBytes[u] = 0;
  hwBytes[u]   = 0;
  softMask &= ~(0xFFul << (u * 8));
  for (uint8_t k = 0; k < 8; k++) heldSince[u * 8 + k] = 0;
  Wire.setClock(I2C_HZ);   // begin() で I2C のクロックが戻る場合に備えて設定し直す
}

void initAll() {
  for (uint8_t u = 0; u < NUM_UNITS; u++) {
    present[u]   = initUnit(u);
    laneBytes[u] = 0;
    hwBytes[u]   = 0;
  }
  softMask = 0;
  for (auto &h : heldSince) h = 0;
  Wire.setClock(I2C_HZ);
}

void printStatus() {
  Serial.println(F("--- status ---"));
  for (uint8_t u = 0; u < NUM_UNITS; u++) {
    Serial.printf("unit%u  0x%02X  lane %2u-%2u  %s\n",
                  u, ADDRS[u], u * 8, u * 8 + 7, present[u] ? "OK" : "NOT FOUND");
  }
  Serial.printf("touch=%u release=%u neighbor=%u stuck=%lums\n",
                TOUCH_TH, RELEASE_TH, NEIGHBOR_TOUCH_TH, (unsigned long)STUCK_MS);
}

// 連続したレジスタをまとめて読む (1つずつ読むより I2C の時間がずっと短い)
bool readBlock(uint8_t addr, uint8_t reg, uint8_t *buf, uint8_t len) {
  Wire.beginTransmission(addr);
  Wire.write(reg);
  if (Wire.endTransmission(false) != 0) return false;
  if (Wire.requestFrom(addr, len) != len) return false;
  for (uint8_t i = 0; i < len; i++) buf[i] = Wire.read();
  return true;
}

// ユニット u の全キーの変化量を読む
bool readDeltas(uint8_t u) {
  uint8_t filtered[24];
  uint8_t baseline[12];
  uint8_t n = numEle[u];
  if (!readBlock(ADDRS[u], REG_FILTERED, filtered, n * 2)) return false;
  if (!readBlock(ADDRS[u], REG_BASELINE, baseline, n)) return false;
  for (uint8_t k = 0; k < 8; k++) {
    uint8_t e = ELE_MAP[u][k];
    int f  = (filtered[e * 2] | (filtered[e * 2 + 1] << 8)) & 0x3FF;
    int bl = baseline[e] << 2;
    delta[u][k] = bl - f;
  }
  return true;
}

uint32_t joinBytes(const uint8_t *bytes) {
  uint32_t m = 0;
  for (uint8_t u = 0; u < NUM_UNITS; u++) m |= (uint32_t)bytes[u] << (u * 8);
  return m;
}

void scan() {
  uint32_t now = millis();

  // 1. MPR121 の判定 (1ユニット2バイトなので速い)
  for (uint8_t u = 0; u < NUM_UNITS; u++) {
    if (!present[u]) { hwBytes[u] = 0; continue; }
    uint16_t t = cap[u].touched();
    uint8_t  b = 0;
    for (uint8_t k = 0; k < 8; k++) {
      if ((t >> ELE_MAP[u][k]) & 1) b |= (1 << k);   // bit0 = 一番小さいレーン番号
    }
    hwBytes[u] = b;
  }
  uint32_t hwMask = joinBytes(hwBytes);

  // 2. 押されているレーンの隣と、隣のレーンとして押下中のレーンだけ、変化量を自分で判定する
  //    変化量を読むのはそのレーンがあるユニットだけ (誰も触れていなければ I2C の読み出しは増えない)
  softMask &= ~hwMask;   // MPR121 も押下と判定したレーンは、MPR121 の判定に任せる
  if (NEIGHBOR_TOUCH_TH > 0) {
    uint32_t active = hwMask | softMask;
    uint32_t candidates = ((active << 1) | (active >> 1) | softMask) & ~hwMask;
    bool loaded[NUM_UNITS] = {};
    for (uint8_t u = 0; u < NUM_UNITS; u++) {
      if (present[u] && ((candidates >> (u * 8)) & 0xFF)) loaded[u] = readDeltas(u);
    }
    for (uint8_t lane = 0; lane < NUM_UNITS * 8; lane++) {
      uint32_t bit = 1ul << lane;
      if (!(candidates & bit)) continue;
      uint8_t u = lane / 8, k = lane % 8;
      if (!loaded[u]) { softMask &= ~bit; continue; }
      int d = delta[u][k];
      if (softMask & bit) {
        if (d < RELEASE_TH) softMask &= ~bit;            // 離れた
      } else if (d > NEIGHBOR_TOUCH_TH) {
        softMask |= bit;                                 // 隣から入ってきた
      }
    }
  } else {
    softMask = 0;
  }

  // 3. 送信する押下状態にまとめる
  for (uint8_t u = 0; u < NUM_UNITS; u++) {
    uint8_t b = hwBytes[u] | (uint8_t)(softMask >> (u * 8));
    laneBytes[u] = b;

    if (STUCK_MS == 0 || !present[u]) continue;
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
    if (reinit) reinitUnit(u);
  }
}

void sendPacket() {
  uint8_t pkt[5] = { 0xAA, laneBytes[0], laneBytes[1], laneBytes[2], laneBytes[3] };
  if (Serial.availableForWrite() >= (int)sizeof(pkt)) Serial.write(pkt, sizeof(pkt));
}

void printDiag() {
  Serial.println();
  for (uint8_t u = 0; u < NUM_UNITS; u++) {
    if (!present[u]) { Serial.printf("u%u ---- not found\n", u); continue; }
    readDeltas(u);
    Serial.printf("u%u d:", u);
    for (uint8_t k = 0; k < 8; k++) {
      uint8_t lane = u * 8 + k;
      char mark = (hwBytes[u] >> k) & 1 ? '*' : (softMask >> lane) & 1 ? '+' : ' ';
      Serial.printf(" %4d%c", delta[u][k], mark);
    }
    Serial.print("  F/B:");
    for (uint8_t k = 0; k < 8; k++) {
      uint8_t e = ELE_MAP[u][k];
      Serial.printf(" %u/%u", cap[u].filteredData(e), cap[u].baselineData(e));
    }
    Serial.println();
  }
}

// 1行: L,時刻(ms),押下(MPR121の判定+隣),隣のレーンとしての押下,変化量0,...,変化量31 (どれも32bitの16進は bit i = lane i)
void printLogLine() {
  char line[256];
  uint32_t mask = joinBytes(laneBytes);
  int len = snprintf(line, sizeof(line), "L,%lu,%08lX,%08lX", (unsigned long)millis(), (unsigned long)mask, (unsigned long)softMask);
  for (uint8_t u = 0; u < NUM_UNITS; u++) {
    bool ok = present[u] && readDeltas(u);
    for (uint8_t k = 0; k < 8; k++) len += snprintf(line + len, sizeof(line) - len, ",%d", ok ? delta[u][k] : 0);
  }
  Serial.println(line);
}

void handleCommands() {
  while (Serial.available()) {
    char c = Serial.read();
    if      (c == 'd') { mode = MODE_DIAG;   printStatus(); }
    else if (c == 'b') { mode = MODE_BINARY; }
    else if (c == 'l') { mode = MODE_LOG; printStatus(); Serial.printf("#touch=%u release=%u neighbor=%u\n", TOUCH_TH, RELEASE_TH, NEIGHBOR_TOUCH_TH); }
    else if (c == 'r') { initAll(); if (mode == MODE_DIAG) printStatus(); }
    else if (c == 's') { printStatus(); }
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
  static uint32_t lastLog  = micros();

  handleCommands();

  if ((int32_t)(micros() - next) < 0) return;
  next += SEND_PERIOD_US;
  if ((int32_t)(micros() - next) > (int32_t)(SEND_PERIOD_US * 5)) next = micros();

  scan();

  bool any = laneBytes[0] | laneBytes[1] | laneBytes[2] | laneBytes[3];
  digitalWrite(LED_BUILTIN, any);   // どれか押されていたらLED点灯

  if (mode == MODE_BINARY) {
    sendPacket();
  } else if (mode == MODE_LOG) {
    if (micros() - lastLog >= LOG_PERIOD_US) {
      lastLog = micros();
      printLogLine();
    }
  } else if (millis() - lastDiag >= 100) {
    lastDiag = millis();
    printDiag();
  }
}
