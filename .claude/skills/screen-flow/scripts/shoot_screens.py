#!/usr/bin/env python3
"""画面（モックの HTML や開発サーバーの URL）を撮って、図に埋め込むサムネイルを作る。

使い方:
  python shoot_screens.py OUTDIR SPEC [SPEC ...] [--scale 0.5] [--wait 4000]
         [--pc 1280x800] [--sp 390x696]

SPEC:
  名前=パスまたはURL        PC の画面として撮る（1280×800 → 図の frame 160×100）
  名前=パスまたはURL@sp     スマホの画面として撮る（390×696 → 図の frame 56×100）

例:
  python shoot_screens.py shots menus=mocks/menus.html check=mocks/allergen-check.html@sp \
         list=http://localhost:5173/menus

OUTDIR/<名前>.png ができる。図の中では %%ICON_<名前>%% で参照する（build.py が埋め込む）。
--scale 0.5 で 640×400 程度（1枚 50KB 前後）。図に入れる枚数が多いときは 0.25 に下げる。

注意：実データが映る画面は撮らない。モックやダミーデータの環境だけを撮る。
"""
import pathlib
import sys

sys.path.insert(0, str(pathlib.Path(__file__).resolve().parent))
from _browser import shoot, to_url  # noqa: E402


def size(s):
    w, h = s.lower().split("x")
    return int(w), int(h)


def main():
    args = sys.argv[1:]
    opts = {"--scale": "0.5", "--wait": "4000", "--pc": "1280x800", "--sp": "390x696"}
    for k in list(opts):
        if k in args:
            i = args.index(k)
            opts[k] = args[i + 1]
            del args[i:i + 2]
    if len(args) < 2:
        print(__doc__)
        sys.exit(1)
    outdir = pathlib.Path(args[0])
    scale, wait = float(opts["--scale"]), int(opts["--wait"])
    for spec in args[1:]:
        if "=" not in spec:
            sys.exit("SPEC は 名前=パス の形で: {}".format(spec))
        name, target = spec.split("=", 1)
        device = "pc"
        if target.endswith("@sp"):
            target, device = target[:-3], "sp"
        w, h = size(opts["--" + device])
        out = outdir / (name + ".png")
        shoot(to_url(target), out, w, h, scale, wait)
        print("shot {} ({} {}x{})".format(out, device, w, h))


if __name__ == "__main__":
    main()
