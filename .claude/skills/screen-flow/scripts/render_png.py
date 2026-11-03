#!/usr/bin/env python3
"""ビルドした画面遷移図の HTML を PNG にする（キャンバスだけ）。

使い方:
  python render_png.py IN.html OUT.png [--scale 2] [--single]

既定ではライト（OUT.png）とダーク（OUT-dark.png）の両方を出す。--single はライトだけ。
キャンバスの大きさは svg.edges の viewBox から読む。テンプレートの #shot モード
（キャンバスだけを左上に出す）を使うので、Playwright がなくても Edge / Chrome で撮れる。
"""
import pathlib
import re
import sys

sys.path.insert(0, str(pathlib.Path(__file__).resolve().parent))
from _browser import shoot, to_url  # noqa: E402


def main():
    args = sys.argv[1:]
    scale, single = 2.0, False
    if "--scale" in args:
        i = args.index("--scale")
        scale = float(args[i + 1])
        del args[i:i + 2]
    if "--single" in args:
        args.remove("--single")
        single = True
    if len(args) != 2:
        print(__doc__)
        sys.exit(1)
    src, out = pathlib.Path(args[0]), pathlib.Path(args[1])
    html = src.read_text(encoding="utf-8")
    m = re.search(r'<svg class="edges"[^>]*viewBox="0 0 (\d+) (\d+)"', html)
    if not m:
        sys.exit('svg.edges の viewBox="0 0 W H" が見つかりません')
    w, h = int(m.group(1)), int(m.group(2))
    if "#shot" not in html and "/shot/" not in html:
        sys.exit("テンプレートの #shot 用スクリプトがありません（assets/template.html から作ってください）")
    url = to_url(str(src))
    shoot(url + "#shot", out, w, h, scale, 3000)
    print("rendered {}".format(out))
    if not single:
        dark = out.with_name(out.stem + "-dark" + out.suffix)
        shoot(url + "#shot-dark", dark, w, h, scale, 3000)
        print("rendered {}".format(dark))


if __name__ == "__main__":
    main()
