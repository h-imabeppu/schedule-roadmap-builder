---
description: 新しいチャットへの引き継ぎ文を作る
mode: agent
---

# 引き継ぎ（安価モデル・Agent）

1. `docs/progress.md` を読む。
2. 直近の作業について、次の内容を含む引き継ぎ文を日本語で出力する。
   - 現在の状況（直近の実施内容と結果、テスト件数）
   - 未完了の作業と、次にやること
   - 使用中の指示.md（`docs/instructions/` のパス）
   - 停止した理由や「TODO(要確認)」があればその一覧
3. `docs/progress.md` が最新でなければ、不足分を追記してよい。それ以外のファイルは変更しない。
4. 新しいチャットで最初に読ませるファイルとして、`docs/progress.md` と `.github/copilot-instructions.md` を示す。
