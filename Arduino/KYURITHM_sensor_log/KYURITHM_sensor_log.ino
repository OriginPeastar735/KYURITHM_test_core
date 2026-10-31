// =====================================================================
//  32キー静電容量タッチスライダー  (Raspberry Pi Pico + MPR121 x4)
//  Unity InputManager.cs 互換:  0xAA + 4バイト(32レーン) を 1ms ごとに送信
//  環境: arduino-pico v6.3.0 / Adafruit_MPR121 v1.1.3
//
//  ※ 今使っているプログラムに、確認用のログモード(l)だけを足したもの。押下の判定は今と同じ
//
//  シリアルモニタからのコマンド (1文字送信):
//    d : 診断モード (テキストで 生値/基準値/変化量 を表示)
//    b : バイナリモード (Unity用。起動時はこちら)
//    l : ログモード (全レーンの変化量と押下状態を 1行ずつ、約200回/秒で送る。Arduino/capture_sensor_log.py で記録する)
//    r : 全センサーを再初期化 (基準値の取り直し・未接続ユニットの再検出)
//    s : 接続状態を表示
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

// ----- 送信周期 -----
constexpr uint32_t SEND_PERIOD_US = 1000;   // 1ms (1000回/秒)
constexpr uint32_t LOG_PERIOD_US  = 5000;   // ログモードで1行送る間隔 (5ms = 200回/秒)

// ----- 押しっぱなし自動復帰 (0で無効) -----
constexpr uint32_t STUCK_MS = 0;

// ----- MPR121 のレジスタ (ログモードで変化量をまとめて読む) -----
constexpr uint8_t REG_FILTERED = 0x04;   // 計測値 (電極ごとに2バイト、下位バイトが先、10bit)
constexpr uint8_t REG_BASELINE = 0x1E;   // 基準値 (電極ごとに1バイト、上位8bit)

// =====================================================================
Adafruit_MPR121 cap[NUM_UNITS];
bool     present[NUM_UNITS];
uint8_t  numEle[NUM_UNITS];
uint8_t  laneBytes[NUM_UNITS];
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
  Serial.printf("touch=%u release=%u stuck=%lums\n", TOUCH_TH, RELEASE_TH, (unsigned long)STUCK_MS);
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
      present[u]   = initUnit(u);
      laneBytes[u] = 0;
      for (uint8_t k = 0; k < 8; k++) heldSince[u * 8 + k] = 0;
    }
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

// ===== ログモード =====
// 連続したレジスタをまとめて読む
bool readBlock(uint8_t addr, uint8_t reg, uint8_t *buf, uint8_t len) {
  Wire.beginTransmission(addr);
  Wire.write(reg);
  if (Wire.endTransmission(false) != 0) return false;
  if (Wire.requestFrom(addr, len) != len) return false;
  for (uint8_t i = 0; i < len; i++) buf[i] = Wire.read();
  return true;
}

// 1行: L,時刻(ms),押下(32bitの16進),隣の押下(このプログラムでは常に0),変化量0,...,変化量31
void printLogLine() {
  char line[256];
  uint32_t mask = 0;
  for (uint8_t u = 0; u < NUM_UNITS; u++) mask |= (uint32_t)laneBytes[u] << (u * 8);
  int len = snprintf(line, sizeof(line), "L,%lu,%08lX,00000000", (unsigned long)millis(), (unsigned long)mask);

  for (uint8_t u = 0; u < NUM_UNITS; u++) {
    uint8_t filtered[24], baseline[12];
    uint8_t n = numEle[u];
    bool ok = present[u] && readBlock(ADDRS[u], REG_FILTERED, filtered, n * 2)
                         && readBlock(ADDRS[u], REG_BASELINE, baseline, n);
    for (uint8_t k = 0; k < 8; k++) {
      int d = 0;
      if (ok) {
        uint8_t e = ELE_MAP[u][k];
        int f = (filtered[e * 2] | (filtered[e * 2 + 1] << 8)) & 0x3FF;
        d = (baseline[e] << 2) - f;
      }
      len += snprintf(line + len, sizeof(line) - len, ",%d", d);
    }
  }
  Serial.println(line);
}

void handleCommands() {
  while (Serial.available()) {
    char c = Serial.read();
    if      (c == 'd') { mode = MODE_DIAG;   printStatus(); }
    else if (c == 'b') { mode = MODE_BINARY; }
    else if (c == 'l') { mode = MODE_LOG; printStatus(); Serial.printf("#touch=%u release=%u neighbor=0\n", TOUCH_TH, RELEASE_TH); }
    else if (c == 'r') { initAll(); if (mode == MODE_DIAG) printStatus(); }
    else if (c == 's') { printStatus(); }
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
