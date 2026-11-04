"""brushup-issue のスクリプトで共通に使うもの（gh の場所・ラベルの定義・リポジトリのルート）。"""
import os
import shutil
import subprocess
import sys

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", "..", "..", ".."))  # リポジトリのルート

# ラベル：（名前, 色, 説明）。軸ごとに1つずつ付ける（安全レビューは当てはまるときだけ）
KINDS = {
    "不具合": ("d73a4a", "要件どおりに動かない"),
    "改善": ("a2eeef", "動くが使いにくい・足りない"),
    "見た目・文言": ("c5def5", "動作を変えない表示・言葉"),
    "ドキュメント": ("0075ca", "README・docs・提出物"),
}
PRIORITIES = {
    "必須": ("b60205", "締切までに直さないと試用・デモ・提出に困る"),
    "あると良い": ("fbca04", "締切までに余裕があれば直す"),
    "余裕があれば": ("e4e669", "ハッカソンのあとでもよい"),
}
RISKS = {
    "高": ("5319e7", "REVIEW.md の区分：高（見込み）"),
    "中": ("8a63d2", "REVIEW.md の区分：中（見込み）"),
    "低": ("d4c5f9", "REVIEW.md の区分：低（見込み）"),
}
SAFETY_LABEL = ("安全レビュー", "e99695", "コミット前に allergen-safety-reviewer を使う")


def all_labels():
    out = [(f"種類: {k}", c, d) for k, (c, d) in KINDS.items()]
    out += [(f"優先度: {k}", c, d) for k, (c, d) in PRIORITIES.items()]
    out += [(f"リスク: {k}", c, d) for k, (c, d) in RISKS.items()]
    out.append(SAFETY_LABEL)
    return out


def find_gh():
    gh = shutil.which("gh")
    if gh:
        return gh
    fallback = r"C:\Program Files\GitHub CLI\gh.exe"
    if os.path.exists(fallback):
        return fallback
    sys.exit("gh が見つかりません。winget install --id GitHub.cli で入れ、gh auth login でログインしてください。")


def run_gh(args):
    """gh を実行し、標準出力を返す。失敗したら止める（素通りさせない）。"""
    r = subprocess.run([find_gh(), *args], cwd=ROOT, capture_output=True, text=True, encoding="utf-8", errors="replace")
    if r.returncode != 0:
        sys.exit(f"gh {' '.join(args[:2])} に失敗しました:\n{r.stderr.strip()}")
    return r.stdout
