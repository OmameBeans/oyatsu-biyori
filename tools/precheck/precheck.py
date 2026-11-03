# -*- coding: utf-8 -*-
"""コミット前の確認を、REVIEW.md の手順どおりに機械的に走らせる。

使い方（リポジトリのルートで）：
    python tools/precheck/precheck.py            差分から区分を決めて、必要な確認を走らせる
    python tools/precheck/precheck.py --e2e      画面を変えていなくても E2E を走らせる
    python tools/precheck/precheck.py --risk high 区分を上げて走らせる（下げることはできない）
    python tools/precheck/precheck.py --dry-run   区分と走らせる確認を表示するだけ

- 区分（高・中・低）は、変えたファイルの場所で決める（REVIEW.md の 1）。迷うものは高い方に倒す
- 中・高：ビルド → Biz のテスト → フロントの lint → フロントのビルド → （画面を変えたら）E2E
- 低：何も走らせない（CI に任せる）
- すべて通ったら、作業中の差分の指紋を .claude/precheck-state.json に記録する。
  コミットのフック（.claude/hooks/check-commit.py）がこの指紋を照らし合わせ、
  確認のあとに変えた差分や、確認していない差分のコミットを止める
- /code-review と allergen-safety-reviewer は、このスクリプトでは走らせない（REVIEW.md の 2。別に行う）
"""
import argparse
import hashlib
import json
import os
import re
import shutil
import subprocess
import sys
import time

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
STATE_PATH = os.path.join(ROOT, ".claude", "precheck-state.json")
CLIENT_DIR = os.path.join(ROOT, "src", "oyatsubiyori.client")
TEST_PROJECT = os.path.join("src", "OyatsuBiyori.Biz.Tests")

RISK_ORDER = {"low": 0, "medium": 1, "high": 2}
RISK_LABEL = {"low": "低", "medium": "中", "high": "高"}

# REVIEW.md の 1「高」に当たる場所。REVIEW.md を直したら、ここも合わせる
HIGH_PATTERNS = [
    # アレルゲン・チラシ・確定は、どのプロジェクト（Biz・テスト・Data・Server・画面）でも高にする。
    # 安全のテストを消したり緩めたりする変更、確認の記録を保存する処理、確定の API・ダイアログを見落とさないため
    (r"^src/.*(allergen|flyer|confirm)", "アレルゲン・チラシ・確定（どの層でも）"),
    # Biz.Tests の同じ名前のテストも高にする
    (r"^src/OyatsuBiyori\.Biz(\.Tests)?/(.*/)?Menu(Validator|ConfirmationChecker|CandidateFilter|ProposalBuilder|ImpactAnalyzer)[^/]*\.cs$", "献立の検証・確定の条件（Biz とテスト）"),
    (r"^src/OyatsuBiyori\.Biz(\.Tests)?/(.*/)?(ProductMatcher|TextNormalizer|InterpretedRequestApplier)[^/]*\.cs$", "照合・正規化・要望の反映（Biz とテスト）"),
    (r"^src/OyatsuBiyori\.Biz/MasterData/", "マスタ（Biz）"),
    # DbContext とマイグレーションは、確認の記録（allergen_checks）の保存のしかたを変えうる
    (r"^src/OyatsuBiyori\.Data/DbContexts/", "DbContext（テーブルの定義）"),
    (r"^src/OyatsuBiyori\.Migrations/", "マイグレーション"),
    (r"^src/OyatsuBiyori\.Claude/", "Claude の呼び出し・プロンプト・スキーマ"),
    (r"^src/OyatsuBiyori\.Server/Security/", "ログイン・認可"),
    (r"^src/OyatsuBiyori\.Server/Program\.cs$", "Program.cs（認証の設定・サービスの登録）"),
    (r"^\.claude/hooks/", "フック"),
    (r"^\.claude/settings\.json$", "フックの設定"),
    (r"^\.claude/agents/allergen-safety-reviewer\.md$", "安全レビューのエージェント"),
    (r"^tools/precheck/", "コミット前の確認のスクリプト"),
    (r"^\.gitignore$", ".gitignore"),
    (r"^\.github/workflows/", "CI"),
]
# 「中」：上に当たらないソースコード・依存関係の変更
MEDIUM_PATTERNS = [
    (r"^src/", "ソースコード"),
    (r"^OyatsuBiyori\.slnx$", "ソリューション"),
]
# E2E を走らせるのは画面を変えたとき（D-87）
CLIENT_PATTERN = re.compile(r"^src/oyatsubiyori\.client/", re.I)


def git_lines(*args):
    r = subprocess.run(["git"] + list(args), capture_output=True, cwd=ROOT)
    if r.returncode != 0:
        return []
    return [l.replace("\\", "/") for l in r.stdout.decode("utf-8", "replace").splitlines() if l]


def changed_files():
    """コミットしていない変更（ステージ済み・未ステージ・未追跡）のファイル。削除も含む。"""
    # 名前の変更は、元のパスと新しいパスの両方を含める（元のパスで区分を決め損ねないため）
    files = set(git_lines("-c", "core.quotepath=false", "diff", "HEAD", "--name-only", "--no-renames"))
    files.update(git_lines("-c", "core.quotepath=false", "ls-files", "--others", "--exclude-standard"))
    return sorted(files)


def classify(files):
    """区分と、その理由（ファイルと当たった場所）を返す。"""
    risk = "low"
    reasons = []
    for f in files:
        for pattern, label in HIGH_PATTERNS:
            if re.search(pattern, f, re.I):
                risk = "high"
                reasons.append(("high", f, label))
                break
        else:
            for pattern, label in MEDIUM_PATTERNS:
                if re.search(pattern, f, re.I):
                    if risk == "low":
                        risk = "medium"
                    reasons.append(("medium", f, label))
                    break
    return risk, reasons


def fingerprint(files):
    """HEAD と、変えたファイルの中身から指紋を作る。確認のあとに1文字でも変えれば変わる。"""
    head = git_lines("rev-parse", "HEAD")
    h = hashlib.sha256()
    h.update((head[0] if head else "").encode())
    for f in files:
        h.update(b"\0" + f.encode("utf-8") + b"\0")
        path = os.path.join(ROOT, f)
        if os.path.isfile(path):
            with open(path, "rb") as fp:
                h.update(hashlib.sha256(fp.read()).digest())
        else:
            h.update(b"<deleted>")
    return h.hexdigest()


def load_state():
    try:
        with open(STATE_PATH, encoding="utf-8") as fp:
            return json.load(fp)
    except (OSError, ValueError):
        return None


def has_npm_script(name):
    try:
        with open(os.path.join(CLIENT_DIR, "package.json"), encoding="utf-8") as fp:
            return name in json.load(fp).get("scripts", {})
    except (OSError, ValueError):
        return False


def build_steps(risk, files, force_e2e):
    """走らせる確認の一覧。(名前, コマンド, 作業フォルダ, 前に通っている必要がある確認の組) と、飛ばした理由。"""
    if risk == "low":
        return [], []
    npm = shutil.which("npm") or "npm"
    steps = [
        ("ビルド", ["dotnet", "build", "OyatsuBiyori.slnx", "--nologo", "-v", "q"], ROOT, ()),
    ]
    skipped = []
    if os.path.isdir(os.path.join(ROOT, TEST_PROJECT)):
        steps.append(("Biz のテスト", ["dotnet", "test", TEST_PROJECT, "--no-build", "--nologo", "-v", "q"], ROOT, ("ビルド",)))
    else:
        skipped.append(("Biz のテスト", "テストのプロジェクトがまだありません"))
    steps.append(("フロントの lint", [npm, "run", "lint"], CLIENT_DIR, ()))
    steps.append(("フロントのビルド", [npm, "run", "build"], CLIENT_DIR, ()))
    if force_e2e or any(CLIENT_PATTERN.search(f) for f in files):
        if has_npm_script("e2e"):
            steps.append(("E2E", [npm, "run", "e2e"], CLIENT_DIR, ("ビルド", "フロントのビルド")))
        else:
            skipped.append(("E2E", "package.json に e2e がまだありません（T0-15 で作る）"))
    else:
        skipped.append(("E2E", "画面（src/oyatsubiyori.client/）を変えていません"))
    return steps, skipped


def run_step(name, cmd, cwd):
    print("▶ {} を走らせています…".format(name), flush=True)
    start = time.time()
    r = subprocess.run(cmd, cwd=cwd, capture_output=True)
    out = (r.stdout + r.stderr).decode("utf-8", "replace")
    return r.returncode == 0, time.time() - start, out


def main():
    for stream in (sys.stdout, sys.stderr):
        try:
            stream.reconfigure(encoding="utf-8")
        except AttributeError:
            pass
    parser = argparse.ArgumentParser(description="コミット前の確認（REVIEW.md）")
    parser.add_argument("--risk", choices=["medium", "high"], help="区分を上げる（下げることはできない）")
    parser.add_argument("--e2e", action="store_true", help="画面を変えていなくても E2E を走らせる")
    parser.add_argument("--dry-run", action="store_true", help="区分と走らせる確認を表示するだけ")
    args = parser.parse_args()

    files = changed_files()
    if not files:
        print("コミットしていない変更はありません。")
        return 0
    risk, reasons = classify(files)
    if args.risk and RISK_ORDER[args.risk] > RISK_ORDER[risk]:
        risk = args.risk

    print("リスクの区分：{}（変えたファイル {} 件）".format(RISK_LABEL[risk], len(files)))
    for level, f, label in reasons:
        if level == "high" or risk != "high":
            print("  - [{}] {}（{}）".format(RISK_LABEL[level], f, label))

    steps, skipped = build_steps(risk, files, args.e2e)
    if args.dry_run:
        print("\n走らせる確認：" + ("、".join(s[0] for s in steps) if steps else "なし（CI に任せる）"))
        for name, why in skipped:
            print("飛ばす確認：{}（{}）".format(name, why))
        return 0

    # 前回の記録は消してから走らせる（途中で止めても、古い記録でコミットできないように）
    try:
        os.remove(STATE_PATH)
    except OSError:
        pass
    # 走らせる前の指紋。確認の途中でファイルを変えたら、確認していない中身を記録しないように比べる
    before = fingerprint(files)

    results = []
    passed = set()
    for name, cmd, cwd, needs in steps:
        missing = [n for n in needs if n not in passed]
        if missing:
            results.append((name, None, 0, "{} が通っていないため飛ばしました".format("・".join(missing))))
            continue
        ok, sec, out = run_step(name, cmd, cwd)
        if ok:
            passed.add(name)
        results.append((name, ok, sec, out))

    print("\n=== 結果 ===")
    for name, ok, sec, _ in results:
        mark = "✅ 通った" if ok else ("⏭ 飛ばした" if ok is None else "❌ 失敗")
        print("{}  {}（{:.0f}秒）".format(mark, name, sec))
    for name, why in skipped:
        print("⏭ 飛ばした  {}（{}）".format(name, why))
    if not steps:
        print("区分が低なので、確認は CI に任せます（REVIEW.md の 2）。")

    failed = [(name, out) for name, ok, _, out in results if ok is False]
    for name, out in failed:
        tail = "\n".join(out.strip().splitlines()[-60:])
        print("\n--- {} の出力（最後の60行） ---\n{}".format(name, tail))
    if failed or any(ok is None for _, ok, _, _ in results):
        print("\n確認が通っていないため、記録しませんでした。直してから、もう一度走らせてください。")
        return 1

    files_after = changed_files()
    after = fingerprint(files_after)
    if files_after != files or after != before:
        print("\n確認を走らせている間に、ファイルが変わりました。変わった中身は確認していないため、記録しませんでした。"
              "もう一度走らせてください。")
        return 1
    state = {
        "fingerprint": after,
        "risk": risk,
        "checkedAt": time.strftime("%Y-%m-%d %H:%M:%S"),
        "steps": [name for name, ok, _, _ in results if ok],
        "skipped": [name for name, _ in skipped],
    }
    with open(STATE_PATH, "w", encoding="utf-8") as fp:
        json.dump(state, fp, ensure_ascii=False, indent=2)

    print("\nすべて通りました。この差分のままならコミットできます。")
    if risk in ("medium", "high"):
        print("次に：/code-review で差分をレビューする（REVIEW.md の 2）")
    if risk == "high":
        print("　　　allergen-safety-reviewer でもレビューし（アレルゲン・献立・Claude の出力に触れる場合）、"
              "コミットメッセージに「確かめ方・リスク・戻し方」を書く")
    if any(CLIENT_PATTERN.search(f) for f in files) and "E2E" in state["steps"]:
        print("　　　E2E の画像（PC とスマホ）を目で見て、崩れがないか確かめる")
    return 0


if __name__ == "__main__":
    sys.exit(main())
