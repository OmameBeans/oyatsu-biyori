# -*- coding: utf-8 -*-
"""Biz のコードを編集したら xUnit のテストを走らせるフック（HK-CC-03）。

PostToolUse（Edit / Write）から呼ばれる。テストが失敗したら exit 2 で結果を Claude に返す。
テストプロジェクト（src/OyatsuBiyori.Biz.Tests/）がまだない間は何もしない。
"""
import json
import os
import re
import subprocess
import sys

TEST_PROJECT = os.path.join("src", "OyatsuBiyori.Biz.Tests")


def main():
    payload = json.loads(sys.stdin.buffer.read().decode("utf-8"))
    path = payload.get("tool_input", {}).get("file_path", "").replace("\\", "/")
    if not re.search(r"src/OyatsuBiyori\.Biz(\.Tests)?/.*\.cs$", path, re.I):
        return 0
    root = os.environ.get("CLAUDE_PROJECT_DIR") or payload.get("cwd") or "."
    project = os.path.join(root, TEST_PROJECT)
    if not os.path.isdir(project):
        return 0
    r = subprocess.run(
        ["dotnet", "test", project, "--nologo", "-v", "q"],
        capture_output=True,
        cwd=root,
    )
    if r.returncode == 0:
        return 0
    out = (r.stdout + r.stderr).decode("utf-8", "replace").splitlines()
    tail = "\n".join(out[-60:])
    msg = "Biz のテストが失敗しています（src/OyatsuBiyori.Biz.Tests）。直してから先に進んでください。\n\n" + tail
    sys.stderr.buffer.write(msg.encode("utf-8"))
    return 2


if __name__ == "__main__":
    sys.exit(main())
