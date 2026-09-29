# AWSIMミニカー環境 — 開発状況と残作業

更新日: 2026-09-29。VisionPilot連携の再開用チェックポイント。

## 現在の到達点

収集・レビュー・RL操作環境は実装済みで、既存devの用途別コミットに保存されている。
監査開始時のHEADは `c1eda6912bd5c66e86f81626e030166a8b87a6eb`。
`main` (`4d96a75f900e9258aef9112ed0d63de2a4e0fb56`) から99コミットあり、
今回その履歴を書き換えずに残す。

| 項目 | 実装・確認状況 |
|---|---|
| 起動・操作 | GUI、設定ファイル、CLIを統合。固定/無限コース、手動操作、記録開始/停止 |
| 車両 | assisted/dynamicを区別した安定動作の基盤。取付位置・姿勢は固定設定として保持 |
| 記録 | schema 3のmanifest、frames/events、RGB、metric depth＋mask、BEV、3D注釈、コース幾何 |
| レビュー | 記録の構造・センサー・注釈整合性を検査し、終端情報をHTML/JSONで表示 |
| RL | 共通設定を用いた開始・停止・checkpoint保存・再開の操作経路 |
| マニュアル | 手動運転から収録、レビュー、RLまでの手順と受け入れ記録 |

実装の主な既存コミットは `18ecf064`（操作設定・複数センサー収録・レビュー・RL管理）、
`c1eda691`（利用手順）。詳細は [operator-workflows](docs/physical-ai-operator-workflows.md) と
[user-guide](docs/physical-ai-user-guide.md) を参照。

ここでの完成範囲は操作・収録の基盤。実車に合ったタイヤ校正、学習済み方策の走行性能、
VisionPilotの3モデルを通した追加学習サービスの完成は含まない。
現行スコープは [physical-ai-goals](docs/physical-ai-goals.md) 冒頭の改訂が優先する。

## 連携で残る実装・検証

### 1. VisionPilotの3モデルへ接続する

- [ ] AutoSpeed・AutoDrive向けの教師生成と入力適合性検査を下流側に実装する。
  AutoSteerは終了済みschema 3記録の取り込み・変換・短い学習まで接続済み。
- [ ] 原本、センサー契約、車両モデル、操作由来、seed/map、カメラ校正を変換後も追跡する。
  固定holdoutとtraining-evaluationの両seed群を学習から除外する。
- [ ] 手動`Command`は計画教師として直接流用せず、`DirectActuation`等の操作記録と
  下流で導出する計画教師を区別する。BEVはシミュレータ真値で、カメラ可視性とは別。
- [ ] 学習に必要な解像度・フレーム間隔・連続性・シナリオ量を定め、収録設定と照合する。
  160×90・5 Hzの軽量動作確認設定だけで学習品質を認定しない。

完了条件: 3モデルそれぞれで記録→教師→追加学習→固定データ評価→書き出しを検証し、
モデル/データ/設定の対応を保存する。主な下流実装の管理先は
[VisionPilot GOAL.md](https://github.com/toki-1441/vision_pilot/blob/dev/GOAL.md)。
記録契約の追加が必要になった場合は、AWSIMのschemaと下流双方に検証を加える。

### 2. 宣言した環境と検証環境を一致させる

- [ ] `scripts/requirements-vision-rl.txt` の固定環境を別環境で再構築して試験する。
  現在のvenvは宣言されたtorch 2.5.1 CPU版と一致していないため、今回の成功を
  固定環境再現の達成とは扱わない。
- [ ] Unity EditMode/PlayMode結果、起動設定、ビルド/実行情報を永続する成果物置場へ保存する。
  過去の `/tmp` 内XMLだけに依存しない。Unity版は `ProjectSettings/ProjectVersion.txt` を使う。
- [ ] クリーンな導入先で、設定読込→手動運転→収録→レビュー→RL保存/再開を確認する。

### 3. 既知制約に対応する

- [ ] 長時間記録のreviewは全件メモリ読込のため、大容量での動作を測り、必要に応じ逐次処理化する。
- [ ] 対応する起動設定の組合せを追加監査し、非対応設定が明確に拒否されることを維持する。
- [ ] 深度・BEVを学習方策へ使用する場合は入力契約と学習経路を明示して実装する。
  記録できることだけで、方策がそのセンサーを使うとは扱わない。

### 長期目標として残す範囲

- [ ] 人による継続走行の受け入れ、実車計測に基づく校正。
- [ ] 人間データ由来の学習方策、未知コース・blind-corner・対戦性能の評価。
- [ ] 外部ROS2制御の接続と、その運用条件の検証。

これらは現行の収集環境完成の前提から外した長期目標。
既存の詳細は [readiness](docs/physical-ai-readiness.md) に残す。

## 2026-09-29の作業・検証記録

今回実施したのは状態監査、既存Pythonテスト、残作業の記録。新しいシミュレータ機能は追加していない。

```bash
PYTHONDONTWRITEBYTECODE=1 ../.physical-ai-venv/bin/python \
  -m unittest discover -s scripts -p 'test_*.py' -v
```

結果: **39 tests passed**、3.576秒、exit 0。
評価レポート整合性11、記録レビュー10、RL保存/再開/通信7、手動走行検証11。
実行環境: Python 3.10.12 / torch 2.13.0+cu130 / NumPy 2.2.6 / Pillow 12.3.0。
これは上記venvでの実測であり、requirementsに指定したCPU環境での再検証ではない。

既存の実データ証跡も確認した。

- endlessのreview: 9,248 records、RGB/depth/BEV各463 samples、`valid=true`。
- dynamicの2記録: 1,755 / 78 records、各センサー87 / 4 samples、`valid=true`。
- 最終RL記録: episode 5/6、seed 11/12、20/21 steps、終了理由timeout。
  checkpointは81,785 bytes。学習の保存・再開経路の証跡であり、性能達成の証拠ではない。

過去の受入文書にはEditMode 172、PlayMode 23、Python 39の成功が記録されている。
参照しているUnity結果XML 2件は今回の `/tmp` には存在せず、Unityも再実行していない。
今回再検証した39件と過去のUnity結果を分けて報告する。
生データやビルド成果物はローカルで保持し、この文書コミットには含めない。

## Gitの保存方針

今回のユーザー指示に従い、`toki-1441/AWSIM-TEST` の `dev` を使う。
既存devの99コミットを保持し、この残作業の記録を追加コミットする。
`origin` は取得・送信ともこのforkであることを確認済み。upstream・mainは変更しない。
