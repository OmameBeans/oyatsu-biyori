"""samples/ の元データ（架空のチラシ・パッケージ）から、画像・PDF と正解データを作る（TD-01）。

使い方（リポジトリのどこからでも実行できる）:
    python tools/build-samples/build.py

チラシの出力（samples/flyers/。flyer-01 の場合）:
    flyer-01.html                 チラシ（ブラウザで見る用）
    flyer-01.png                  チラシ全体の画像（ファイルのアップロード用）
    flyer-01.pdf                  チラシの PDF（1ページ）
    flyer-01-photo-top.png        撮影風の画像（上半分を近づけて、少し傾けて撮った想定）
    flyer-01-photo-bottom.png     撮影風の画像（下半分）
    flyer-01.expected.json        チラシの読み取りの正解データ（画像ごとに写っている商品つき）

パッケージの出力（samples/packages/。package-8104 の場合）:
    package-8104-front.png        表面（商品名と絵）
    package-8104-<面>.png         原材料表示のある面（back、または side-1・side-2 のように複数）
    package-8104.expected.json    アレルゲン抽出の正解データ（原材料名の全文つき）

- 正解データは元データから作るので、書き起こしの間違いが入らない。読み取りの評価はこれと比べる
- 税込価格は本体価格 × 1.08 の切り捨て、1個あたりの単価は本体価格 ÷ 個数の四捨五入で、ここで計算する
- 画像と PDF は Edge（なければ Chrome）のヘッドレスで撮る
"""
import html
import io
import json
import math
import os
import pathlib
import shutil
import subprocess
import sys
import tempfile

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))  # リポジトリのルート
SRC_DIR = os.path.join(ROOT, "samples", "flyers")
PACKAGE_DIR = os.path.join(ROOT, "samples", "packages")
TEMPLATE = os.path.join(os.path.dirname(__file__), "template.html")

COLUMNS = 5
CELL_H = 250
HEAD_H = 56
FOOT_H = 22
PAD = 12
SHEET_W = 1120 + PAD * 2

# 撮影風の画像：近づけて（拡大）、少し傾けて、半分ずつ撮った想定（FR-FLYER-01 の案内に合わせる）
PHOTO_W, PHOTO_H = 1600, 800
PHOTO_SCALE = 1.35
PHOTO_ROTATE = -0.8

# デモの山場（ヘーゼルナッツはパッケージで初めて見つかる。TD-01）のため、チラシに出してはいけない言葉
#（ヘーゼルナッツの探す言葉は MasterSeed.cs と合わせる）
FORBIDDEN_WORDS = ("ナッツ", "なっつ", "ヘーゼル", "へーぜる", "ヘイゼル", "はしばみ", "ジャンドゥ", "ノワゼット", "ノアゼット", "プラリネ")

# パッケージの画像：表面は縦長、原材料表示の面は横長。小さい文字が読めるように2倍で撮る
FRONT_W, FRONT_H = 620, 820
LABEL_W = 1000
LABEL_CHARS_PER_LINE = 42  # 原材料表示の表の右の列に、1行に入るおよその文字数（高さの見積もりに使う）
PACKAGE_SCALE = 2
CONTINUE_MARK = "→"  # 原材料名が別の面につづく印（ここから後ろは全文に入れない）

BROWSERS = (
    r"C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe",
    r"C:\Program Files\Microsoft\Edge\Application\msedge.exe",
    r"C:\Program Files\Google\Chrome\Application\chrome.exe",
)


def including_tax(price):
    return math.floor(price * 108 / 100)


def unit_price(item):
    count = item["piecesPerPackCandidates"][0]
    return math.floor(item["priceExcludingTax"] / count + 0.5)


def age_class(age):
    if "新生児" in age or "カ月" in age:
        return "baby"
    if age.startswith("1歳"):
        return "one"
    if age.startswith("2歳"):
        return "two"
    return "three"


def full_description(item):
    """説明文の末尾にアレルゲンの表記（あれば）を付ける。チラシと同じ「（乳・小麦）」の形。"""
    labels = item.get("allergenLabel")
    return item["description"] + ("（{}）".format("・".join(labels)) if labels else "")


def check(data):
    items = data["items"]
    codes = [x["code"] for x in items]
    if len(set(codes)) != len(codes):
        raise SystemExit("注文番号が重複しています")
    if len(items) % COLUMNS != 0:
        raise SystemExit("商品の数は {} の倍数にしてください（格子が欠けるため）".format(COLUMNS))
    for item in items:
        text = json.dumps({k: v for k, v in item.items() if not k.startswith("_")}, ensure_ascii=False)
        for word in FORBIDDEN_WORDS:
            if word in text:
                raise SystemExit("{}: チラシに「{}」を出してはいけません（TD-01）".format(item["code"], word))
        if item.get("unitPrice") and not item["piecesPerPackCandidates"]:
            raise SystemExit("{}: 1個あたりの単価を出すには入数の候補が要ります".format(item["code"]))


def cell_html(item):
    e = html.escape
    parts = ['    <div class="cell">']
    parts.append('<div class="age {}">{}</div>'.format(age_class(item["age"]), e(item["age"])))
    parts.append('<div class="origin">{}</div>'.format(e(item["origin"])))
    parts.append('<div class="meta"><div class="code">{}</div><div class="small">次回予定▶{}　賞{}<br>{}</div></div>'.format(
        e(item["code"]), e(item["next"]), e(item["shelf"]), e(item["maker"])))
    parts.append('<div class="name">{}</div>'.format(e(item["name"])))
    parts.append('<div class="volume">{}</div>'.format(e(item["volume"])))
    label = item["name"].split(" ")[0]
    parts.append('<div class="pkg" style="background:{}"><div class="icon">{}</div><div class="label">{}</div></div>'.format(
        e(item["package"]["color"]), item["package"]["icon"], e(label)))
    for sticker in item.get("stickers", []):
        parts.append('<div class="sticker">{}</div>'.format(e(sticker).replace("より", "より<br>")))
    if item.get("bulk"):
        parts.append('<div class="bulk">{}</div>'.format(e(item["bulk"]).replace("注文で", "注文で<br>")))
    elif item.get("chilled"):
        parts.append('<div class="chilled">冷蔵</div>')
    if item.get("unitPrice"):
        parts.append('<div class="unit">本体<b>{}</b>円<br>{}</div>'.format(unit_price(item), e(item["unitPrice"])))
    parts.append('<div class="price"><span class="honntai">本体</span><span class="num">{}</span><span class="yen">円</span>'
                 '<span class="tax">(税込<b>{:,}</b>円)</span></div>'.format(item["priceExcludingTax"], including_tax(item["priceExcludingTax"])))
    parts.append('<div class="desc">{}</div>'.format(e(full_description(item))))
    parts.append("</div>")
    return "".join(parts)


def render(template, data, sheet_h, body_class="", extra_css=""):
    values = {
        "title": "{} {}（架空のチラシ）".format(data["publisher"], data["issue"]),
        "sheet_w": str(SHEET_W),
        "sheet_h": str(sheet_h),
        "cell_h": str(CELL_H),
        "section": html.escape(data["section"]),
        "subsection": html.escape(data["subsection"]),
        "publisher": html.escape(data["publisher"]),
        "issue": html.escape(data["issue"]),
        "cells": "\n".join(cell_html(x) for x in data["items"]),
        "body_class": body_class,
        "extra_css": extra_css,
    }
    out = template
    for key, value in values.items():
        out = out.replace("{{" + key + "}}", value)
    return out


def photo_css(row_top):
    """撮影風：row_top（チラシ上の y）が画像の上端近くに来るように、拡大・回転・ぼかし・影を付ける。"""
    ty = 45 - row_top * PHOTO_SCALE
    return """
  body.photo {{ background: #5c4b3b; width: {w}px; height: {h}px; overflow: hidden; }}
  body.photo .sheet {{ transform-origin: 0 0; transform: translate(30px, {ty:.0f}px) rotate({r}deg) scale({s}); filter: blur(0.6px) contrast(0.9) brightness(0.96) saturate(0.88); box-shadow: 0 6px 18px rgba(0,0,0,0.5); }}
  body.photo::after {{ content: ""; position: fixed; inset: 0; pointer-events: none; background: radial-gradient(ellipse at 25% 15%, rgba(255,250,235,0.22), transparent 55%), linear-gradient(115deg, rgba(0,0,0,0) 55%, rgba(0,0,0,0.24)); }}
""".format(w=PHOTO_W, h=PHOTO_H, ty=ty, r=PHOTO_ROTATE, s=PHOTO_SCALE)


def find_browser():
    for path in BROWSERS:
        if os.path.exists(path):
            return path
    raise SystemExit("Edge か Chrome が見つかりません")


def run_browser(browser, profile, html_path, args):
    subprocess.run([browser, "--headless=new", "--disable-gpu", "--hide-scrollbars", "--no-first-run",
                    "--user-data-dir=" + profile] + args + [pathlib.Path(html_path).as_uri()],
                   check=True, capture_output=True, timeout=120)


def expected(data, images):
    items = []
    for i, item in enumerate(data["items"]):
        entry = {
            "code": item["code"],
            "row": i // COLUMNS + 1,
            "name": item["name"],
            "maker": item["maker"],
            "volume": item["volume"],
            "priceExcludingTax": item["priceExcludingTax"],
            "priceIncludingTax": including_tax(item["priceExcludingTax"]),
            "ageLabel": item["age"],
            "allergenLabel": item.get("allergenLabel") or [],
            "description": full_description(item),
            "bulk": item.get("bulk"),
            "unitPrice": {"unit": item["unitPrice"], "priceExcludingTax": unit_price(item)} if item.get("unitPrice") else None,
            "piecesPerPackCandidates": item["piecesPerPackCandidates"],
            "snackCategory": item["snackCategory"],
        }
        if item.get("_trap"):
            entry["_trap"] = item["_trap"]
        items.append(entry)
    return {
        "_note": "build.py が {}.json から作った正解データ。直接編集しない。snackCategory・piecesPerPackCandidates は Claude の候補の目安（確定は職員）".format(data["id"]),
        "source": data["id"] + ".json",
        "images": images,
        "items": items,
    }


def build(json_path, template, browser):
    with io.open(json_path, encoding="utf-8") as f:
        data = json.load(f)
    check(data)
    base = os.path.join(SRC_DIR, data["id"])
    rows = len(data["items"]) // COLUMNS
    sheet_h = PAD * 2 + HEAD_H + CELL_H * rows + FOOT_H
    codes_of = lambda first, last: [x["code"] for i, x in enumerate(data["items"]) if first <= i // COLUMNS + 1 <= last]
    half = (rows + 1) // 2

    with io.open(base + ".html", "w", encoding="utf-8", newline="\n") as f:
        f.write(render(template, data, sheet_h))

    work = tempfile.mkdtemp(prefix="sample-flyer-")
    try:
        profile = os.path.join(work, "profile")
        run_browser(browser, profile, base + ".html",
                    ["--window-size={},{}".format(SHEET_W, sheet_h), "--force-device-scale-factor=1.5", "--screenshot=" + base + ".png"])
        run_browser(browser, profile, base + ".html", ["--no-pdf-header-footer", "--print-to-pdf=" + base + ".pdf"])
        for suffix, first_row in (("top", 1), ("bottom", half + 1)):
            row_top = PAD + HEAD_H + CELL_H * (first_row - 1)
            photo_html = os.path.join(work, "photo-{}.html".format(suffix))
            with io.open(photo_html, "w", encoding="utf-8") as f:
                f.write(render(template, data, sheet_h, "photo", photo_css(row_top)))
            run_browser(browser, profile, photo_html,
                        ["--window-size={},{}".format(PHOTO_W, PHOTO_H), "--force-device-scale-factor=1", "--screenshot={}-photo-{}.png".format(base, suffix)])
    finally:
        shutil.rmtree(work, ignore_errors=True)

    name = data["id"]
    images = {
        name + ".png": codes_of(1, rows),
        name + ".pdf": codes_of(1, rows),
        name + "-photo-top.png": codes_of(1, half),
        name + "-photo-bottom.png": codes_of(half + 1, rows),
    }
    with io.open(base + ".expected.json", "w", encoding="utf-8", newline="\n") as f:
        json.dump(expected(data, images), f, ensure_ascii=False, indent=2)
        f.write("\n")
    print("作りました: {}（{}商品）".format(os.path.relpath(base, ROOT), len(data["items"])))
    return data


PACKAGE_CSS = """
  * { box-sizing: border-box; }
  html, body { margin: 0; }
  body { width: {w}px; height: {h}px; overflow: hidden; background: #6b5a48; font-family: "BIZ UDPGothic", "Yu Gothic", "Meiryo", sans-serif; color: #222; }
  .face { position: absolute; left: 36px; top: 36px; transform-origin: 0 0; transform: rotate(-0.7deg); filter: blur(0.3px) contrast(0.95); box-shadow: 0 6px 18px rgba(0,0,0,0.45); }
  body::after { content: ""; position: fixed; inset: 0; pointer-events: none; background: radial-gradient(ellipse at 30% 20%, rgba(255,250,235,0.18), transparent 55%), linear-gradient(115deg, rgba(0,0,0,0) 55%, rgba(0,0,0,0.2)); }
  .front { width: 540px; height: 740px; border-radius: 18px; color: #fff; padding: 36px 32px; display: flex; flex-direction: column; align-items: center; }
  .front .maker { font-size: 20px; font-weight: 700; opacity: 0.9; }
  .front .name { font-size: 38px; font-weight: 800; text-align: center; line-height: 1.2; margin-top: 28px; text-shadow: 0 2px 3px rgba(0,0,0,0.35); }
  .front .icon { font-size: 200px; line-height: 1; margin-top: 30px; font-family: "Segoe UI Emoji", sans-serif; }
  .front .catch { font-size: 22px; margin-top: 26px; background: rgba(255,255,255,0.18); padding: 6px 16px; border-radius: 20px; }
  .front .count { margin-top: auto; align-self: flex-end; font-size: 24px; font-weight: 700; background: #fff; color: #333; padding: 6px 14px; border-radius: 8px; }
  .front .sample { font-size: 13px; opacity: 0.8; margin-top: 10px; align-self: flex-start; }
  .label { width: 920px; background: #fffdf8; padding: 22px 24px; }
  .label table { width: 100%; border-collapse: collapse; font-size: 16px; line-height: 1.55; }
  .label th { width: 160px; text-align: left; vertical-align: top; font-weight: 700; border: 1.5px solid #333; padding: 5px 8px; }
  .label td { border: 1.5px solid #333; padding: 5px 8px; }
  .label .nutrition { margin-top: 14px; border: 1.5px solid #333; padding: 6px 8px; font-size: 14px; }
  .label .sample { margin-top: 8px; font-size: 12px; color: #777; text-align: right; }
"""


def package_html(width, height, body):
    css = PACKAGE_CSS.replace("{w}", str(width)).replace("{h}", str(height))
    return '<!doctype html>\n<html lang="ja">\n<head>\n<meta charset="utf-8">\n<style>{}</style>\n</head>\n<body>\n{}\n</body>\n</html>\n'.format(css, body)


def front_body(data):
    e = html.escape
    front = data["front"]
    return ('<div class="face front" style="background:{}"><div class="maker">{}</div><div class="name">{}</div>'
            '<div class="icon">{}</div><div class="catch">{}</div><div class="count">{}</div>'
            '<div class="sample">架空の商品のサンプルです</div></div>').format(
        e(front["color"]), e(data["maker"]), e(data["name"]).replace(" ", "<br>"), front["icon"], e(front["catch"]), e(front["count"]))


def label_body(face):
    e = html.escape
    rows = "".join("<tr><th>{}</th><td>{}</td></tr>".format(e(k), e(v).replace("\n", "<br>")) for k, v in face["rows"])
    nutrition = '<div class="nutrition">{}</div>'.format(e(face["nutrition"])) if face.get("nutrition") else ""
    return '<div class="face label"><table>{}</table>{}<div class="sample">※開発用の架空の商品です</div></div>'.format(rows, nutrition)


def label_height(face):
    """原材料表示の面の画像の高さ。表の行数をおよそで見積もり、背景の余白が大きくならないようにする。"""
    lines = 0
    for _, value in face["rows"]:
        lines += sum(max(1, math.ceil(len(x) / LABEL_CHARS_PER_LINE)) for x in value.split("\n"))
    return 36 * 2 + 22 * 2 + len(face["rows"]) * 11 + lines * 25 + (50 if face.get("nutrition") else 0) + 30 + 40


def ingredients_text(data):
    """原材料名を面をまたいでつなげた全文。改行と「→（つづく）」の印は除く（印刷上の都合で、原材料ではないため）。"""
    parts = []
    for face in data["faces"]:
        for key, value in face["rows"]:
            if key.startswith("原材料名") or key == "（原材料名のつづき）":
                parts.append(value.split(CONTINUE_MARK)[0].replace("\n", ""))
    return "".join(parts)


def build_package(json_path, browser, flyer_codes):
    with io.open(json_path, encoding="utf-8") as f:
        data = json.load(f)
    if data["flyerCode"] not in flyer_codes:
        raise SystemExit("{}: チラシに注文番号 {} の商品がありません".format(data["id"], data["flyerCode"]))
    full_text = ingredients_text(data)
    for word in data["expected"]["foundInIngredients"]:
        if word not in full_text:
            raise SystemExit("{}: 原材料名の全文に「{}」がありません".format(data["id"], word))

    base = os.path.join(PACKAGE_DIR, data["id"])
    pages = [("front", FRONT_W, FRONT_H, front_body(data))]
    pages += [(face["id"], LABEL_W, label_height(face), label_body(face)) for face in data["faces"]]
    work = tempfile.mkdtemp(prefix="sample-package-")
    try:
        profile = os.path.join(work, "profile")
        for suffix, width, height, body in pages:
            page = os.path.join(work, suffix + ".html")
            with io.open(page, "w", encoding="utf-8") as f:
                f.write(package_html(width, height, body))
            run_browser(browser, profile, page,
                        ["--window-size={},{}".format(width, height), "--force-device-scale-factor={}".format(PACKAGE_SCALE),
                         "--screenshot={}-{}.png".format(base, suffix)])
    finally:
        shutil.rmtree(work, ignore_errors=True)

    result = {
        "_note": "build.py が {}.json から作った正解データ。直接編集しない。foundInIngredients は、アレルゲン表示には出ず原材料名の全文で見つかる品目".format(data["id"]),
        "source": data["id"] + ".json",
        "flyerCode": data["flyerCode"],
        "images": ["{}-{}.png".format(data["id"], suffix) for suffix, _, _, _ in pages],
        "ingredientsText": full_text,
    }
    result.update(data["expected"])
    with io.open(base + ".expected.json", "w", encoding="utf-8", newline="\n") as f:
        json.dump(result, f, ensure_ascii=False, indent=2)
        f.write("\n")
    print("作りました: {}（{}枚）".format(os.path.relpath(base, ROOT), len(pages)))


def sources_in(folder):
    if not os.path.isdir(folder):
        return []
    return sorted(os.path.join(folder, x) for x in os.listdir(folder) if x.endswith(".json") and not x.endswith(".expected.json"))


def main():
    with io.open(TEMPLATE, encoding="utf-8") as f:
        template = f.read()
    browser = find_browser()
    flyers = sources_in(SRC_DIR)
    if not flyers:
        raise SystemExit("samples/flyers/ に元データ（*.json）がありません")
    flyer_codes = set()
    for path in flyers:
        flyer_codes.update(x["code"] for x in build(path, template, browser)["items"])
    for path in sources_in(PACKAGE_DIR):
        build_package(path, browser, flyer_codes)


if __name__ == "__main__":
    sys.exit(main())
