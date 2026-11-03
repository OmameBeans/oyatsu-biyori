"""ヘッドレスブラウザで「ビューポートそのまま」を PNG に撮る共通部品。

Playwright（Python）が入っていればそれを使い、なければ手元の Edge / Chrome / Chromium を
コマンドラインのヘッドレスモードで起動する（追加のインストール不要。Python 3.7 でも動く）。
ブラウザの場所は環境変数 SCREEN_FLOW_BROWSER で指定することもできる。
"""
import os
import pathlib
import shutil
import subprocess
import sys
import tempfile

CANDIDATES = [
    r"C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe",
    r"C:\Program Files\Microsoft\Edge\Application\msedge.exe",
    r"C:\Program Files\Google\Chrome\Application\chrome.exe",
    r"C:\Program Files (x86)\Google\Chrome\Application\chrome.exe",
    "/Applications/Google Chrome.app/Contents/MacOS/Google Chrome",
    "/Applications/Microsoft Edge.app/Contents/MacOS/Microsoft Edge",
    "/Applications/Chromium.app/Contents/MacOS/Chromium",
]
NAMES = ["msedge", "google-chrome", "google-chrome-stable", "chromium", "chromium-browser", "chrome"]


def to_url(target):
    """ファイルのパスなら file:// の URL に、http(s) ならそのまま返す。"""
    if "://" in target:
        return target
    p = pathlib.Path(target).resolve()
    if not p.exists():
        sys.exit("見つかりません: {}".format(target))
    return p.as_uri()


def find_browser():
    env = os.environ.get("SCREEN_FLOW_BROWSER")
    if env:
        return env
    for c in CANDIDATES:
        if os.path.exists(c):
            return c
    for n in NAMES:
        w = shutil.which(n)
        if w:
            return w
    return None


def have_playwright():
    try:
        import playwright.sync_api  # noqa: F401
        return True
    except ImportError:
        return False


def shoot(url, out, width, height, scale, wait_ms=4000):
    """url をビューポート width×height（CSS px）で開き、scale 倍で out に保存する。"""
    out = pathlib.Path(out)
    out.parent.mkdir(parents=True, exist_ok=True)
    if have_playwright():
        from playwright.sync_api import sync_playwright
        with sync_playwright() as p:
            b = p.chromium.launch(args=["--no-sandbox", "--font-render-hinting=none"])
            page = b.new_page(viewport={"width": width, "height": height}, device_scale_factor=scale)
            page.goto(url)
            page.wait_for_timeout(min(wait_ms, 1500))
            page.screenshot(path=str(out))
            b.close()
        return
    exe = find_browser()
    if not exe:
        sys.exit("Playwright も Edge / Chrome も見つかりません。\n"
                 "  pip install playwright && python -m playwright install chromium\n"
                 "  またはブラウザの場所を環境変数 SCREEN_FLOW_BROWSER で指定してください。")
    profile = tempfile.mkdtemp(prefix="screen-flow-")
    try:
        cmd = [exe, "--headless=new", "--disable-gpu", "--hide-scrollbars", "--no-first-run",
               "--no-default-browser-check", "--user-data-dir=" + profile,
               "--window-size={},{}".format(width, height),
               "--force-device-scale-factor={}".format(scale),
               "--virtual-time-budget={}".format(wait_ms),
               "--screenshot=" + str(out.resolve()), url]
        r = subprocess.run(cmd, stdout=subprocess.PIPE, stderr=subprocess.PIPE, timeout=120)
        if not out.exists():
            sys.exit("撮影に失敗しました（{}）:\n{}".format(
                exe, r.stderr.decode("utf-8", "replace")[-2000:]))
    finally:
        shutil.rmtree(profile, ignore_errors=True)
