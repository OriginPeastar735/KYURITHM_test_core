# slider32 をコンパイルして Pico に書き込む（VS Code のタスクから呼ぶ）
#   ・普段の Pico（COMポートあり）  → arduino-cli でそのまま書き込む
#   ・BOOTSEL モード（RPI-RP2 ドライブ）→ できた .uf2 をドライブにコピーする
param([switch]$CompileOnly)   # 付けるとコンパイルだけ

$ErrorActionPreference = 'Stop'
$cli    = Join-Path $env:LOCALAPPDATA 'Programs\Arduino IDE\resources\app\lib\backend\resources\arduino-cli.exe'
$fqbn   = 'rp2040:rp2040:rpipico'
$sketch = Join-Path $PSScriptRoot '..\Arduino\slider32'
$outDir = Join-Path $env:TEMP 'kyurithm-slider32-build'   # ビルド結果はリポジトリの外に置く

if (-not (Test-Path $cli)) { Write-Host "arduino-cli が見つかりません。Arduino IDE をインストールしてください: $cli"; exit 1 }

& $cli compile --fqbn $fqbn --output-dir $outDir $sketch
if ($LASTEXITCODE -ne 0) { Write-Host 'コンパイルに失敗しました'; exit 1 }
if ($CompileOnly) { exit 0 }

# 普段の Pico を探す（COMポート）
$json = & $cli board list --format json | ConvertFrom-Json
$port = $json.detected_ports | Where-Object { $_.matching_boards.fqbn -match 'rp2040' } | Select-Object -First 1 -ExpandProperty port
if ($port) {
    Write-Host "書き込み先: $($port.address)"
    & $cli upload --fqbn $fqbn -p $port.address --input-dir $outDir $sketch
    exit $LASTEXITCODE
}

# BOOTSEL モードの Pico を探す（INFO_UF2.TXT があるドライブ）
$drive = Get-PSDrive -PSProvider FileSystem | Where-Object { Test-Path (Join-Path $_.Root 'INFO_UF2.TXT') } | Select-Object -First 1
if ($drive) {
    $uf2 = Get-ChildItem $outDir -Filter '*.uf2' | Select-Object -First 1
    Write-Host "書き込み先: $($drive.Root)（BOOTSEL モード）"
    Copy-Item $uf2.FullName $drive.Root   # コピーが終わると Pico が自動で再起動する
    Write-Host '書き込みました。Pico が再起動します'
    exit 0
}

Write-Host 'Pico が見つかりません。USB をつなぎ直してください（Unity の再生や Arduino IDE は閉じておく）'
exit 1
