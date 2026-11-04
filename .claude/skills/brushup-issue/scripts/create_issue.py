"""ブラッシュアップの issue を起票し、implementation-tasks.md の「13. ブラッシュアップ（issue）」に1行足す。

使い方（リポジトリのどこからでも実行できる）:
    python .claude/skills/brushup-issue/scripts/create_issue.py \
        --title "献立一覧で商品名から週を探せるようにする" --body-file issue-body.md \
        --kind 改善 --priority あると良い --risk 中 [--safety] [--refs "FR-LIST-02, SC-11"] [--dry-run]

起票の前に機械的なチェックを行い、1つでも当たったら起票しない（公開リポジトリのため、止める側に倒す）:
- API キー・IP アドレス・画像や添付ファイルのリンクがない
- 本文に「終わりの条件」の見出しがある
- 安全レビューを付けるなら、リスクは「高」（REVIEW.md の決まり）
商品名や子どもの情報は機械では見分けられないので、起票の前に目で確かめる（SKILL.md）。
"""
import argparse
import io
import os
import re
import subprocess
import sys

sys.path.insert(0, os.path.dirname(__file__))
from ghcli import KINDS, PRIORITIES, RISKS, ROOT, SAFETY_LABEL, run_gh  # noqa: E402

if hasattr(sys.stdout, "reconfigure"):
    sys.stdout.reconfigure(encoding="utf-8")

TASKS_MD = os.path.join(ROOT, "docs", "implementation-tasks", "implementation-tasks.md")
SECTION_HEADING = "## 13. ブラッシュアップ（issue）"
BUILD_TASKS_HTML = os.path.join(ROOT, "tools", "build-tasks-html", "build.py")

# .claude/hooks/check-commit.py と同じパターン（コミットで止めるものは issue でも止める）
API_KEY_PATTERN = re.compile(r"sk-ant-[A-Za-z0-9_\-]{10,}")
IPV4_PATTERN = re.compile(r"(?<![\d.])(\d{1,3})\.(\d{1,3})\.(\d{1,3})\.(\d{1,3})(?![\d.])")
ALLOWED_IPS = {"127.0.0.1", "0.0.0.0", "255.255.255.255"}
# 画像・添付（実データのチラシや画面が映りうる）。md の画像、<img>、GitHub への添付の URL
ATTACHMENT_PATTERN = re.compile(
    r"!\[[^\]]*\]\(|<img\b|user-images\.githubusercontent\.com|github\.com/user-attachments/"
    r"|\.(?:png|jpe?g|gif|webp|heic|pdf)\b",
    re.IGNORECASE,
)
SECRET_WORDS = re.compile(r"(?:password|パスワード)\s*[:：=]\s*\S+", re.IGNORECASE)


def check_text(title, body, args):
    problems = []
    for where, text in (("タイトル", title), ("本文", body)):
        if API_KEY_PATTERN.search(text):
            problems.append(f"{where}: API キーらしい文字列（sk-ant-...）があります")
        for m in IPV4_PATTERN.finditer(text):
            parts = [int(p) for p in m.groups()]
            if m.group(0) in ALLOWED_IPS or parts[0] == 0 or any(p > 255 for p in parts):
                continue
            problems.append(f"{where}: IP アドレスらしい文字列（{m.group(0)}）があります")
        if ATTACHMENT_PATTERN.search(text):
            problems.append(f"{where}: 画像・添付ファイルへの参照があります（実データが映りうるので、言葉で書いてください）")
        if SECRET_WORDS.search(text):
            problems.append(f"{where}: パスワードらしい記述があります")
        if "docs/context/" in text:
            problems.append(f"{where}: docs/context/（公開しない資料）に触れています。中身を書いていないか確かめ、パスの記述も外してください")
    if not re.search(r"^##\s*終わりの条件", body, re.MULTILINE):
        problems.append("本文に「## 終わりの条件」の見出しがありません（閉じるときの判断の基準になるため必須）")
    if args.safety and args.risk != "高":
        problems.append("安全レビューを付けるなら、リスクは「高」にしてください（REVIEW.md の決まり）")
    if not title.strip():
        problems.append("タイトルが空です")
    return problems


def md_cell(text):
    return text.replace("|", "\\|").replace("\n", " ").strip()


def build_row(number, url, args):
    return (
        f"| #{number} | {args.priority} | [{md_cell(args.title)}]({url}) | {args.kind} | {args.risk} "
        f"| {md_cell(args.refs)} | {'○' if args.safety else ''} |  |"
    )


def insert_row(md, row):
    """13. の節の表の最後に1行足す。節や表が見つからなければ止める（黙って足さない）。"""
    lines = md.split("\n")
    try:
        start = lines.index(SECTION_HEADING)
    except ValueError:
        sys.exit(f"implementation-tasks.md に「{SECTION_HEADING}」の節がありません。issue は作られています。表には手で足してください。")
    i = start + 1
    while i < len(lines) and not lines[i].startswith("| ID |"):
        if lines[i].startswith("## "):
            sys.exit("13. の節に表（| ID | ...）がありません。issue は作られています。表には手で足してください。")
        i += 1
    if i >= len(lines):
        sys.exit("13. の節に表（| ID | ...）がありません。issue は作られています。表には手で足してください。")
    i += 2  # 見出しの行と区切りの行
    while i < len(lines) and lines[i].startswith("|"):
        i += 1
    lines.insert(i, row)
    return "\n".join(lines)


def main():
    p = argparse.ArgumentParser(description="ブラッシュアップの issue を起票する")
    p.add_argument("--title", required=True)
    p.add_argument("--body-file", required=True)
    p.add_argument("--kind", required=True, choices=list(KINDS))
    p.add_argument("--priority", required=True, choices=list(PRIORITIES))
    p.add_argument("--risk", required=True, choices=list(RISKS))
    p.add_argument("--safety", action="store_true", help="安全レビュー（allergen-safety-reviewer）が要る")
    p.add_argument("--refs", default="", help="関係する要件・タスクの ID（例：FR-LIST-02, SC-11, T9-03）")
    p.add_argument("--dry-run", action="store_true", help="チェックだけ行い、起票しない")
    args = p.parse_args()

    body = io.open(args.body_file, encoding="utf-8-sig").read()
    problems = check_text(args.title, body, args)
    if problems:
        print("起票しませんでした。次を直してください:")
        for x in problems:
            print(f"- {x}")
        sys.exit(1)

    labels = [f"種類: {args.kind}", f"優先度: {args.priority}", f"リスク: {args.risk}"]
    if args.safety:
        labels.append(SAFETY_LABEL[0])
    gh_args = ["issue", "create", "--title", args.title, "--body-file", os.path.abspath(args.body_file)]
    for label in labels:
        gh_args += ["--label", label]

    if args.dry_run:
        print("チェックを通りました（--dry-run のため起票していません）")
        print("ラベル: " + "、".join(labels))
        print("足す行: " + build_row("<番号>", "<URL>", args))
        return

    out = run_gh(gh_args).strip()
    m = re.search(r"https://github\.com/\S+/issues/(\d+)", out)
    if not m:
        sys.exit(f"issue の URL が読み取れませんでした（起票はされたかもしれません。gh issue list で確かめてください）:\n{out}")
    url, number = m.group(0), m.group(1)
    print(f"起票しました: #{number} {url}")

    md = io.open(TASKS_MD, encoding="utf-8-sig").read()
    io.open(TASKS_MD, "w", encoding="utf-8", newline="\n").write(insert_row(md, build_row(number, url, args)))
    print("implementation-tasks.md の 13. に1行足しました")
    env = dict(os.environ, PYTHONIOENCODING="utf-8")  # 子の print を UTF-8 で受け取るため
    r = subprocess.run(
        [sys.executable, BUILD_TASKS_HTML], cwd=ROOT, env=env,
        capture_output=True, text=True, encoding="utf-8", errors="replace",
    )
    if r.returncode != 0:
        sys.exit(f"タスクの HTML の作り直しに失敗しました:\n{r.stdout}{r.stderr}")
    print(r.stdout.strip())


if __name__ == "__main__":
    main()
