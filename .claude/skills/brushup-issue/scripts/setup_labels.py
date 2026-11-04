"""ブラッシュアップの issue のラベル（種類・優先度・リスク・安全レビュー）を GitHub に作る。

使い方（リポジトリのどこからでも実行できる。何度実行してもよい）:
    python .claude/skills/brushup-issue/scripts/setup_labels.py

既にあるラベルは、色と説明をここの定義に合わせ直す（--force）。
"""
import os
import sys

sys.path.insert(0, os.path.dirname(__file__))
from ghcli import all_labels, run_gh  # noqa: E402

if hasattr(sys.stdout, "reconfigure"):
    sys.stdout.reconfigure(encoding="utf-8")

for name, color, desc in all_labels():
    run_gh(["label", "create", name, "--color", color, "--description", desc, "--force"])
    print(f"ラベルを用意しました: {name}")
