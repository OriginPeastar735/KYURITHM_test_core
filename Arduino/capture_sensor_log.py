"""センサーの変化量を記録する（スライドで反応が途切れる原因を調べる用）

使い方:
  1. Arduino IDE のシリアルモニタと KYURITHM（Unity）を閉じる（シリアルポートを取り合わないように）
  2. ログモード(l)のあるプログラム（KYURITHM_sensor_log か KYURITHM_sensor_neighbor）を書き込んでおく
  3. python Arduino\\capture_sensor_log.py COM3
     （ポート名を省略すると COM3。ファイル名の後ろに付ける名前も指定できる: python ... COM3 neighbor8）

記録中のキー操作（このウィンドウで押す）:
  1〜9 : いま試している動かし方の番号（ログの label 列に入る）。試し方を変えるたびに押す
  0    : 番号なし（準備中・休憩中）
  Space: 途切れたと感じた瞬間に押す（ログに印を残す）
  q    : 記録を終える（Arduino を Unity 用のバイナリモードに戻す）

記録は Arduino/logs/ に CSV で保存する。列: pc_time, label, mark, t_ms, mask, soft, d0..d31
  mask: 送信している押下状態（32bitの16進、bit i = lane i）  soft: 隣のレーンとしての押下（neighbor版だけ）
  d0..d31: 各レーンの変化量（基準値 - 計測値）。TOUCH_TH を超えると押下、RELEASE_TH を下回ると解放
"""
import msvcrt
import os
import sys
import time
from datetime import datetime

import serial

LANES = 32


def lane_bar(mask: int) -> str:
    """押下状態を 左斜面|底面|右斜面 の形で表示する"""
    s = "".join("#" if mask >> i & 1 else "." for i in range(LANES))
    return f"{s[0:8]}|{s[8:24]}|{s[24:32]}"


def main():
    port = sys.argv[1] if len(sys.argv) > 1 else "COM3"
    tag = f"_{sys.argv[2]}" if len(sys.argv) > 2 else ""
    log_dir = os.path.join(os.path.dirname(os.path.abspath(__file__)), "logs")
    os.makedirs(log_dir, exist_ok=True)
    path = os.path.join(log_dir, f"sensor_{datetime.now():%Y%m%d_%H%M%S}{tag}.csv")

    ser = serial.Serial(port, 115200, timeout=0.05)
    time.sleep(0.2)
    ser.reset_input_buffer()
    ser.write(b"l")

    label = 0
    rows = 0
    marks = 0
    last_status = 0.0
    buffer = b""
    print(f"記録中: {path}")
    print("1〜9: 動かし方の番号 / 0: 番号なし / Space: 途切れた印 / q: 終了\n")

    with open(path, "w", encoding="utf-8", newline="") as f:
        f.write("pc_time,label,mark,t_ms,mask,soft," + ",".join(f"d{i}" for i in range(LANES)) + "\n")
        mask = 0
        try:
            while True:
                mark = 0
                while msvcrt.kbhit():
                    key = msvcrt.getwch()
                    if key == "q":
                        raise KeyboardInterrupt
                    if key.isdigit():
                        label = int(key)
                        print(f"\n--- 動かし方 {label} ---")
                    elif key == " ":
                        mark = 1
                        marks += 1

                buffer += ser.read(4096)
                *lines, buffer = buffer.split(b"\n")
                now = time.time()
                for raw in lines:
                    line = raw.decode("ascii", "replace").strip()
                    if line.startswith("#"):
                        f.write(line + "\n")  # しきい値などの設定
                        continue
                    if not line.startswith("L,"):
                        continue
                    fields = line.split(",")[1:]
                    if len(fields) != 3 + LANES:
                        continue
                    f.write(f"{now:.3f},{label},{mark}," + ",".join(fields) + "\n")
                    mark = 0
                    rows += 1
                    mask = int(fields[1], 16)

                if now - last_status >= 0.1:
                    last_status = now
                    print(f"\r[{label}] {lane_bar(mask)}  行 {rows}  印 {marks}   ", end="", flush=True)
        except KeyboardInterrupt:
            pass
        finally:
            ser.write(b"b")
            ser.close()

    print(f"\n保存しました: {path}（{rows}行、印 {marks}個）")


if __name__ == "__main__":
    main()
