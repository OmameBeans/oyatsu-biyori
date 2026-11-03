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
)
MEDIA_EXTENSIONS = (".jpg", ".jpeg", ".png", ".heic", ".heif", ".webp", ".gif", ".bmp", ".tif", ".tiff", ".pdf")
DB_EXTENSIONS = (".db", ".sqlite", ".sqlite3", ".db-shm", ".db-wal")

# 中身を調べる対象から外す、大きな生成物
SKIP_CONTENT_SUFFIXES = ("package-lock.json", "docs/requirements/index.html")

API_KEY_PATTERN = re.compile(r"sk-ant-[A-Za-z0-9_\-]{10,}")
IPV4_PATTERN = re.compile(r"(?<![\d.])(\d{1,3})\.(\d{1,3})\.(\d{1,3})\.(\d{1,3})(?![\d.])")
ALLOWED_IPS = {"127.0.0.1", "0.0.0.0", "255.255.255.255"}


def git_lines(*args):
    r = subprocess.run(["git"] + list(args), capture_output=True)
    if r.returncode != 0:
        return []
    return [l for l in r.stdout.decode("utf-8", "replace").splitlines() if l]


def target_files(command):
    files = set(git_lines("diff", "--cached", "--name-only", "--diff-filter=ACMR"))
    # 「git add ... && git commit」「git commit -a」は、まだステージされていない変更も対象にする
    if re.search(r"\bgit\s+add\b", command) or re.search(r"\bgit\s+commit\b[^;&|]*\s(-a\b|-am\b|--all\b)", command):
        files.update(git_lines("diff", "--name-only", "--diff-filter=ACMR"))
        files.update(git_lines("ls-files", "--others", "--exclude-standard"))
    return sorted(f.replace("\\", "/") for f in files)


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
    payload = json.loads(sys.stdin.buffer.read().decode("utf-8"))
    command = payload.get("tool_input", {}).get("command", "")
    if not re.search(r"\bgit\s+commit\b", command):
        return 0
    cwd = payload.get("cwd") or os.environ.get("CLAUDE_PROJECT_DIR")
    if cwd:
        os.chdir(cwd)
    problems = check(target_files(command))
    if not problems:
        return 0
    msg = "公開リポジトリに入れてはいけないものがあるため、コミットを止めました。\n" + "\n".join("- " + p for p in problems)
    msg += "\n\n対象から外す（git restore --staged など）か、誤検知ならユーザーに確認してください。"
    sys.stderr.buffer.write(msg.encode("utf-8"))
    return 2


if __name__ == "__main__":
    sys.exit(main())
