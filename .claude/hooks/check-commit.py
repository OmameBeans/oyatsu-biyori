# -*- coding: utf-8 -*-
"""git commit の前に、公開してはいけないものが入っていないかを確かめるフック（HK-CC-02）。

PreToolUse（Bash / PowerShell）から呼ばれる。git commit を含むコマンドのときだけ確かめ、
問題があれば exit 2 でコミットを止め、理由を stderr に出す。
"""
import json
import os
import re
import subprocess
import sys

# 画像・PDF を置いてよい場所（ダミーデータと、フロントの素材）
ALLOWED_MEDIA_DIRS = (
    "samples/",
    "src/oyatsubiyori.client/public/",
    "src/oyatsubiyori.client/src/assets/",
    # スキルで作った構成図・画面遷移図（実データを含まない）
    "docs/architecture/",
    "docs/screen-flow/",
)
MEDIA_EXTENSIONS = (".jpg", ".jpeg", ".png", ".heic", ".heif", ".webp", ".gif", ".bmp", ".tif", ".tiff", ".pdf")
DB_EXTENSIONS = (".db", ".sqlite", ".sqlite3", ".db-shm", ".db-wal")

# 中身を調べる対象から外す、大きな生成物
SKIP_CONTENT_SUFFIXES = ("package-lock.json", "docs/requirements/index.html")

API_KEY_PATTERN = re.compile(r"sk-ant-[A-Za-z0-9_\-]{10,}")
IPV4_PATTERN = re.compile(r"(?<![\d.])(\d{1,3})\.(\d{1,3})\.(\d{1,3})\.(\d{1,3})(?![\d.])")
ALLOWED_IPS = {"127.0.0.1", "0.0.0.0", "255.255.255.255"}


# 「git -C dir commit」「git -c key=value commit」「git --no-pager commit」「git.exe commit」
# 「& "C:\...\git.exe" commit」なども拾う
GIT_PREFIX = r"\bgit(?:\.exe)?[\"']?(?:\s+(?:-[Cc]\s+\S+|--[\w-]+(?:=\S+)?))*\s+"
COMMIT_PATTERN = re.compile(GIT_PREFIX + r"commit\b")
ADD_PATTERN = re.compile(GIT_PREFIX + r"add\b")
# -a・-am・-av・-qam などの短いオプションの組み合わせと --all
COMMIT_ALL_PATTERN = re.compile(GIT_PREFIX + r"commit\b[^;&|]*\s(-[A-Za-z]*a[A-Za-z]*\b|--all\b)")


def git_lines(*args):
    """git の出力の行。git が失敗したら例外にする（空の一覧として扱うと、確認を素通りするため）。"""
    # 日本語のファイル名を "\346..." のように書き換えさせない（中身を調べ損ねないため）
    r = subprocess.run(["git", "-c", "core.quotepath=false"] + list(args), capture_output=True)
    if r.returncode != 0:
        raise RuntimeError("git {} が失敗しました：{}".format(" ".join(args), r.stderr.decode("utf-8", "replace").strip()))
    return [l.replace("\\", "/") for l in r.stdout.decode("utf-8", "replace").splitlines() if l]


def target_files(command, diff_filter="ACMR"):
    """コミットされるファイル。diff_filter を空にすると、削除したファイルも含める。
    名前の変更は、元のパスと新しいパスの両方を含める（--no-renames）。"""
    opt = ["--no-renames"] + (["--diff-filter=" + diff_filter] if diff_filter else [])
    files = set(git_lines("diff", "--cached", "--name-only", *opt))
    # 「git add ... && git commit」「git commit -a」は、まだステージされていない変更も対象にする
    if ADD_PATTERN.search(command) or COMMIT_ALL_PATTERN.search(command):
        files.update(git_lines("diff", "--name-only", *opt))
        files.update(git_lines("ls-files", "--others", "--exclude-standard"))
    return sorted(files)


def is_not_ip(parts):
    # バージョン番号などの誤検知を減らすため、0 始まりや範囲外は IP とみなさない
    return parts[0] == 0 or any(p > 255 for p in parts)


def check(files):
    problems = []
    for f in files:
        lower = f.lower()
        if lower.startswith("docs/context/"):
            problems.append("{}: docs/context/ は公開しない資料です".format(f))
            continue
        if lower.endswith(DB_EXTENSIONS):
            problems.append("{}: データベースのファイルは入れません（実データの可能性）".format(f))
            continue
        if lower.endswith(MEDIA_EXTENSIONS) and not lower.startswith(ALLOWED_MEDIA_DIRS):
            problems.append("{}: 画像・PDF は samples/ かフロントの素材フォルダにだけ置けます（実物のチラシ・パッケージの可能性）".format(f))
            continue
        if lower.endswith(MEDIA_EXTENSIONS) or lower.endswith(SKIP_CONTENT_SUFFIXES):
            continue
        try:
            with open(f, "rb") as fp:
                data = fp.read(2 * 1024 * 1024)
        except OSError:
            continue
        if b"\0" in data:
            continue
        text = data.decode("utf-8", "replace")
        if API_KEY_PATTERN.search(text):
            problems.append("{}: API キーらしい文字列（sk-ant-...）があります".format(f))
        for m in IPV4_PATTERN.finditer(text):
            ip = m.group(0)
            parts = [int(p) for p in m.groups()]
            if ip in ALLOWED_IPS or is_not_ip(parts):
                continue
            line_no = text.count("\n", 0, m.start()) + 1
            problems.append("{}:{}: IP アドレスらしい文字列（{}）があります".format(f, line_no, ip))
            break
    return problems


def main():
    try:
        return run()
    except Exception as e:  # フック自体のエラーで素通りさせない（exit 1 は止めない扱いになるため、2 を返す）
        msg = ("コミット前の確認のフックでエラーが起きたため、コミットを止めました：{}: {}\n"
               "原因を直すか、確認を飛ばしてコミットするかどうかをユーザーに確認してください。").format(type(e).__name__, e)
        sys.stderr.buffer.write(msg.encode("utf-8"))
        return 2


def run():
    payload = json.loads(sys.stdin.buffer.read().decode("utf-8"))
    command = payload.get("tool_input", {}).get("command", "")
    if not COMMIT_PATTERN.search(command):
        return 0
    cwd = payload.get("cwd") or os.environ.get("CLAUDE_PROJECT_DIR")
    if cwd:
        os.chdir(cwd)
    command_dir = os.getcwd()
    # git の出すパスはリポジトリのルートからなので、ルートに移ってから調べる
    top = git_lines("rev-parse", "--show-toplevel")[0]
    os.chdir(top)
    problems = check(target_files(command))
    if problems:
        msg = "公開リポジトリに入れてはいけないものがあるため、コミットを止めました。\n" + "\n".join("- " + p for p in problems)
        msg += "\n\n対象から外す（git restore --staged など）か、誤検知ならユーザーに確認してください。"
        sys.stderr.buffer.write(msg.encode("utf-8"))
        return 2
    try:
        precheck = load_precheck(top)
    except Exception as e:  # 確認の仕組み自体が壊れていたら、通さずに止める
        msg = ("コミット前の確認（tools/precheck/precheck.py）を読み込めなかったため、コミットを止めました：{}: {}\n"
               "precheck.py を直してから、もう一度走らせてください。").format(type(e).__name__, e)
        sys.stderr.buffer.write(msg.encode("utf-8"))
        return 2
    problem = check_precheck(precheck, command, command_dir)
    if problem:
        sys.stderr.buffer.write(problem.encode("utf-8"))
        return 2
    return 0


def load_precheck(top):
    """コミットするリポジトリ（worktree ならその worktree）の tools/precheck/precheck.py を読み込む。
    記録と指紋も、そのリポジトリのものを使う。"""
    import importlib.util
    path = os.path.join(top, "tools", "precheck", "precheck.py")
    spec = importlib.util.spec_from_file_location("precheck", path)
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)  # 見つからない・構文の誤りは例外になり、main で止める
    return module


def commit_message(command, command_dir):
    """コミットメッセージを調べる文字列。-F / --file ならそのファイルの中身。"""
    m = re.search(r"\s(?:-F|--file)(?:\s+|=)(\"[^\"]+\"|'[^']+'|\S+)", command)
    if not m:
        return command
    path = m.group(1).strip("\"'")
    if path == "-":  # 標準入力（heredoc など）から渡すときは、メッセージがコマンドの中にある
        return command
    path = os.path.join(command_dir, path)
    try:
        with open(path, encoding="utf-8") as fp:
            return fp.read()
    except OSError:
        return None


def check_precheck(precheck, command, command_dir):
    """REVIEW.md の確認（tools/precheck/precheck.py）が、今の差分に対して通っているかを確かめる。"""
    # 区分はコミットするファイル（削除を含む）で決める
    risk, _ = precheck.classify(target_files(command, diff_filter=""))
    if risk == "low":
        return None
    label = precheck.RISK_LABEL[risk]
    # 確認は作業中の差分全体に対して走らせたので、指紋も全体で比べる
    state = precheck.load_state()
    if not state or state.get("fingerprint") != precheck.fingerprint(precheck.changed_files()):
        why = "まだ確認していない" if not state else "確認のあとに差分が変わった"
        return ("リスクの区分が{}の変更で、{}ため、コミットを止めました。\n"
                "python tools/precheck/precheck.py を走らせ、すべて通ってからコミットしてください（REVIEW.md の 2）。\n"
                "確認を飛ばしてコミットするかどうかは、ユーザーに確認してください。").format(label, why)
    # 確認したのは作業中の中身。ステージした中身がそれと違うファイルは、確認していない中身がコミットされる
    if not (ADD_PATTERN.search(command) or COMMIT_ALL_PATTERN.search(command)):
        staged = set(git_lines("diff", "--cached", "--name-only", "--no-renames"))
        unstaged = set(git_lines("diff", "--name-only", "--no-renames"))
        differ = sorted(staged & unstaged)
        if differ:
            return ("ステージした中身が作業中の中身と違うファイルがあるため、コミットを止めました（確認したのは作業中の中身です）。\n"
                    + "\n".join("- " + f for f in differ)
                    + "\ngit add でステージし直してからコミットしてください。")
    if risk == "high":
        message = commit_message(command, command_dir)
        if message is None:
            return ("リスクの区分が高の変更ですが、コミットメッセージのファイルを読めませんでした。"
                    "-m で渡すか、ファイルのパスを確かめてください（REVIEW.md の 2）。")
        missing = [w for w in ("確かめ方", "リスク", "戻し方") if w not in message]
        if missing:
            return ("リスクの区分が高の変更なので、コミットメッセージの本文に「確かめ方・リスク・戻し方」を書いてください"
                    "（足りないもの：{}。REVIEW.md の 2）。").format("・".join(missing))
    return None


if __name__ == "__main__":
    sys.exit(main())
