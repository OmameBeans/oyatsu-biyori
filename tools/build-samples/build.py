"""samples/ の元データ（架空のチラシ・パッケージ）から、画像・PDF と正解データを作る（TD-01）。

使い方（リポジトリのどこからでも実行できる）:
    python tools/build-samples/build.py

チラシの出力（samples/flyers/。flyer-01 の場合）:
    flyer-01.html                 チラシ（ブラウザで見る用）
    flyer-01.png                  チラシ全体の画像（ファイルのアップロード用）
    flyer-01.pdf                  チラシの PDF（1ページ）
    flyer-01-photo-top.png        撮影風の画像（上半分を近づけて、少し傾けて撮った想定）
    flyer-01-photo-bottom.png     撮影風の画像（下半分）
    flyer-01-photo-full.png       撮影風の画像（1ページを丸ごと、机に置いて斜めから撮った想定）
    flyer-01.expected.json        チラシの読み取りの正解データ（画像ごとに写っている商品・1回の読み取りで送る画像の組つき）

チラシの見た目は、生協のお菓子のページの書き方に寄せている（商品・価格・文章はすべて架空）:
    - 4列。商品の写真の下に、メーカー・商品名・内容量・番号の黒い枠・価格・説明文を並べる
    - アレルゲンは特定原材料だけを略して「（卵・乳・麦・え）」と書く。括弧は元データの "labelBrackets" で変えられる
    - 3桁と4桁の注文番号が混ざる。次回予定は「12月1回」のように「回」で書く（「毎週」の商品もある）
    - 説明文は、価格の右（既定）か、番号・価格の上（元データの "descPosition": "above"）に置く

取り込みの分類（D-100。docs/requirements/09-flyer-import.md）を確かめるための、元データの印:
    - "maker": null              メーカーの行を描かない（正解の maker も null。メーカーが読めない＝再入力）
    - "priceUnreadable": true    税込価格をかすれさせて描く（正解の priceIncludingTax は null。税込価格が読めない＝再入力）
    - "nonFood": "理由"          食品以外の掲載品（正解に nonFood・nonFoodReason を付ける。外した掲載品になる）

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

COLUMNS = 4
CELL_H = 262
HEAD_H = 56
FOOT_H = 22
PAD = 12
SHEET_W = 1480 + PAD * 2

# 撮影風の画像（上半分・下半分）：近づけて、少し傾けて、半分ずつ撮った想定（FR-FLYER-01 の案内に合わせる）。
# 4列がすべて画像に入る大きさにする（端の列が切れると、写っていない商品を正解に数えてしまうため）
PHOTO_W = 1600
PHOTO_LEFT = 24
PHOTO_SCALE = (PHOTO_W - PHOTO_LEFT * 2) / SHEET_W
PHOTO_ROTATE = -0.8

# 撮影風の画像（1ページ丸ごと）：机に置いたチラシを、少し斜めから撮った想定（台形のゆがみ・照明の映り込み）。
# 画像の縦横は紙に合わせ、余白は少しだけにする
FULL_PHOTO_W = 1640
FULL_PHOTO_MARGIN = 50

# アレルゲンの略し方（実物のチラシは特定原材料だけを1〜2文字で書く）。ここにない品目はチラシに書けない
# カシューナッツはカタカナの「カ」（ひらがなの「か」＝かにと字で分ける。09 の 4・FR-IMPORT-32）
SHORT_ALLERGEN_LABELS = {"卵": "卵", "乳": "乳", "小麦": "麦", "えび": "え", "かに": "か", "落花生": "落", "そば": "そ", "くるみ": "く", "カシューナッツ": "カ"}

# デモの山場（ヘーゼルナッツはパッケージで初めて見つかる。TD-01）を載せたチラシ（"hazelnutDemo": true）に出してはいけない言葉
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


def label_text(item):
    """チラシに印刷するアレルゲンの表記（略した形・括弧なし。例：「卵・乳・麦」）。表記がなければ None。"""
    labels = item.get("allergenLabel")
    if not labels:
        return None
    return "・".join(SHORT_ALLERGEN_LABELS[x] for x in labels)


def bracketed_label(data, item):
    """括弧つきのアレルゲンの表記（「（乳・麦）」。元データの "labelBrackets" で括弧を変えられる）。表記がなければ空。"""
    label = label_text(item)
    if not label:
        return ""
    opening, closing = data.get("labelBrackets", ["（", "）"])
    return opening + label + closing


def full_description(data, item):
    """注意書き（あれば）・説明文・アレルゲンの表記（あれば）を、チラシと同じ順につなげる。"""
    return item.get("notice", "") + item["description"] + bracketed_label(data, item)


def check(data, seen_codes):
    items = data["items"]
    codes = [x["code"] for x in items]
    if len(set(codes)) != len(codes):
        raise SystemExit("注文番号が重複しています")
    for code in codes:
        if code in seen_codes:
            raise SystemExit("{}: ほかのチラシと注文番号が重複しています（パッケージの紐づけに使うため）".format(code))
    if len(items) % COLUMNS != 0:
        raise SystemExit("商品の数は {} の倍数にしてください（格子が欠けるため）".format(COLUMNS))
    for item in items:
        if data.get("hazelnutDemo"):
            # アレルゲンの表記（allergenLabel）は品目名で書くが、チラシには略記（「カ」など）だけが印刷されるので、確かめから外す
            text = json.dumps({k: v for k, v in item.items() if not k.startswith("_") and k != "allergenLabel"}, ensure_ascii=False)
            for word in FORBIDDEN_WORDS:
                if word in text:
                    raise SystemExit("{}: このチラシに「{}」を出してはいけません（TD-01）".format(item["code"], word))
        if item.get("bulk") or item.get("stickers"):
            raise SystemExit("{}: まとめ買いの表示（bulk）・シール（stickers）はチラシに描かないので、元データに書けません".format(item["code"]))
        if item.get("unitPrice") and not item["piecesPerPackCandidates"]:
            raise SystemExit("{}: 1個あたりの単価を出すには入数の候補が要ります".format(item["code"]))
        for label in item.get("allergenLabel") or []:
            if label not in SHORT_ALLERGEN_LABELS:
                raise SystemExit("{}: 「{}」は特定原材料ではないので、チラシの表記には書けません".format(item["code"], label))
        if "nonFood" in item and (not isinstance(item["nonFood"], str) or not item["nonFood"].strip()):
            raise SystemExit("{}: 食品以外の掲載品（nonFood）には、外す理由を文字で書いてください".format(item["code"]))
        if item.get("nonFood") and (item.get("allergenLabel") or item["piecesPerPackCandidates"] or item.get("unitPrice")):
            raise SystemExit("{}: 食品以外の掲載品に、アレルゲンの表記・入数の候補・1個あたりの単価は書けません".format(item["code"]))
        if "maker" not in item:
            raise SystemExit("{}: メーカー（maker）を書いてください。チラシに載せないときは null にします".format(item["code"]))


def cell_html(data, item):
    """1商品。上に商品の写真（皿の絵とパッケージ）、下にメーカー・商品名・内容量・番号・価格・説明文。"""
    e = html.escape
    above = data.get("descPosition") == "above"
    parts = ['    <div class="p-cell{}">'.format(" above" if above else "")]
    plate = "".join('<span>{}</span>'.format(item["package"]["icon"]) for _ in range(3))
    parts.append('<div class="p-photo"><div class="p-plate">{}</div>'
                 '<div class="p-pkg" style="background:{}"><div class="icon">{}</div><div class="label">{}</div></div>'.format(
                     plate, e(item["package"]["color"]), item["package"]["icon"], e(item["package"]["label"])))
    if item.get("banner"):
        parts.append('<div class="p-banner {}">{}</div>'.format("red" if item.get("specialPrice") else "", e(item["banner"])))
    if item.get("origin"):
        parts.append('<div class="p-origin">{}</div>'.format(e(item["origin"])))
    if item.get("badge"):
        parts.append('<div class="p-badge">{}</div>'.format(e(item["badge"]).replace("ベスト", "<br>ベスト")))
    parts.append("</div>")
    # メーカーが null の商品は、メーカーの行を描かない（読み取りで「メーカーが読めない」＝再入力になるかを確かめる。FR-IMPORT-03）
    if item["maker"] is not None:
        parts.append('<div class="p-maker">{}</div>'.format(e(item["maker"])))
    name = e(item["name"])
    if item["name"].startswith("CO・OP "):
        # 生協の商品の印。読み取りでは「CO・OP」として商品名に含める（照合の正規化で除く言葉の確認用。実物のマークには似せない）
        name = '<span class="p-coop">CO・OP</span>' + e(item["name"][len("CO・OP "):])
    parts.append('<div class="p-name">{}</div>'.format(name))
    parts.append('<div class="p-volume">{}</div>'.format(e(item["volume"])))
    notice = '<span class="p-notice">{}</span>'.format(e(item["notice"])) if item.get("notice") else ""
    label = bracketed_label(data, item)
    # アレルゲンの表記は行の途中で分かれないようにする（「（乳・」と「麦）」に分かれると、片方だけ読まれるおそれがある）
    label_html = '<span class="p-label">{}</span>'.format(e(label)) if label else ""
    desc = '<div class="p-desc">{}{}{}</div>'.format(notice, e(item["description"]), label_html)
    if above:
        parts.append(desc)
    parts.append('<div class="p-bottom"><div class="p-left"><div class="p-code">{}</div><div class="p-dates">'.format(e(item["code"])))
    if item.get("chilled"):
        parts.append('<div class="p-chilled">冷蔵</div>')
    # 「毎週」の商品は次回予定の代わりに「毎週」とだけ書く。次回予定がない商品は空欄
    next_html = '<b class="p-weekly">毎週</b>' if item.get("next") == "毎週" else "次回予定<br>" + e(item.get("next") or "")
    parts.append('<div class="p-next">{}</div><div class="p-shelf"><span>賞</span>{}</div></div></div>'.format(next_html, e(item["shelf"])))
    price = item["priceExcludingTax"]
    # "priceUnreadable": true の商品は、税込価格を印刷のかすれで読めなくする（税込価格が読めない＝再入力。09 の 3.1）
    tax_html = ('<div class="tax">(税込<span style="filter: blur(3.5px); opacity: 0.55">{:,}</span>円)</div>' if item.get("priceUnreadable")
                else '<div class="tax">(税込{:,}円)</div>').format(including_tax(price))
    parts.append('<div class="p-mid"><div class="p-price{}"><span class="honntai">本体</span><span class="num">{:,}</span><span class="yen">円</span>'
                 '{}</div>'.format(" special" if item.get("specialPrice") else "", price, tax_html))
    if item.get("unitPrice"):
        parts.append('<div class="p-unit">{}/本体{}円</div>'.format(e(item["unitPrice"]), unit_price(item)))
    parts.append("</div>")
    if not above:
        parts.append(desc)
    parts.append("</div></div>")
    return "".join(parts)


def render(template, data, sheet_h, body_class="", extra_css=""):
    values = {
        "title": "{} {}（架空のチラシ）".format(data["publisher"], data["issue"]),
        "sheet_w": str(SHEET_W),
        "sheet_h": str(sheet_h),
        "cell_h": str(CELL_H),
        "columns": str(COLUMNS),
        "section": html.escape(data["section"]),
        "subsection": html.escape(data["subsection"]),
        "publisher": html.escape(data["publisher"]),
        "issue": html.escape(data["issue"]),
        "cells": "\n".join(cell_html(data, x) for x in data["items"]),
        "body_class": body_class,
        "extra_css": extra_css,
    }
    out = template
    for key, value in values.items():
        out = out.replace("{{" + key + "}}", value)
    return out


def full_photo_size(sheet_h):
    """1ページ丸ごとの撮影風の画像の大きさ（紙の縦横に合わせる）"""
    scale = (FULL_PHOTO_W - FULL_PHOTO_MARGIN * 2) / SHEET_W
    return FULL_PHOTO_W, math.ceil(sheet_h * scale + FULL_PHOTO_MARGIN * 2)


def photo_full_css(sheet_h):
    """撮影風（1ページ丸ごと）：紙全体が写るように縮め、奥が少し小さくなるように傾け、照明の映り込みを付ける。"""
    width, height = full_photo_size(sheet_h)
    scale = (width - FULL_PHOTO_MARGIN * 2) / SHEET_W
    # transform-origin が紙の中心なので、縮めたあとの紙の左上が余白の位置に来るように、縮めるぶんだけずらす
    left = FULL_PHOTO_MARGIN - SHEET_W * (1 - scale) / 2
    top = FULL_PHOTO_MARGIN - sheet_h * (1 - scale) / 2
    return """
  body.photo {{ background: #6b5a48; width: {w}px; height: {h}px; overflow: hidden; perspective: 2600px; }}
  body.photo .sheet {{ transform-origin: 50% 50%; position: absolute; left: {left:.0f}px; top: {top:.0f}px; transform: scale({s:.3f}) rotateX(6deg) rotateY(-3deg) rotate(-1.2deg); filter: blur(0.7px) contrast(0.88) brightness(0.97) saturate(0.85); box-shadow: 0 8px 22px rgba(0,0,0,0.5); }}
  body.photo::after {{ content: ""; position: fixed; inset: 0; pointer-events: none; background: radial-gradient(ellipse 30% 22% at 62% 30%, rgba(255,255,250,0.35), transparent 70%), radial-gradient(ellipse at 20% 85%, rgba(255,250,235,0.15), transparent 50%), linear-gradient(100deg, rgba(0,0,0,0) 60%, rgba(0,0,0,0.22)); }}
""".format(w=width, h=height, left=left, top=top, s=scale)


def photo_css(row_top, photo_h):
    """撮影風（上半分・下半分）：row_top（チラシ上の y）が画像の上端近くに来るように、縮め・回転・ぼかし・影を付ける。"""
    ty = 30 - row_top * PHOTO_SCALE
    return """
  body.photo {{ background: #5c4b3b; width: {w}px; height: {h}px; overflow: hidden; }}
  body.photo .sheet {{ transform-origin: 0 0; transform: translate({left}px, {ty:.0f}px) rotate({r}deg) scale({s:.3f}); filter: blur(0.6px) contrast(0.9) brightness(0.96) saturate(0.88); box-shadow: 0 6px 18px rgba(0,0,0,0.5); }}
  body.photo::after {{ content: ""; position: fixed; inset: 0; pointer-events: none; background: radial-gradient(ellipse at 25% 15%, rgba(255,250,235,0.22), transparent 55%), linear-gradient(115deg, rgba(0,0,0,0) 55%, rgba(0,0,0,0.24)); }}
""".format(w=PHOTO_W, h=photo_h, left=PHOTO_LEFT, ty=ty, r=PHOTO_ROTATE, s=PHOTO_SCALE)


def find_browser():
    for path in BROWSERS:
        if os.path.exists(path):
            return path
    raise SystemExit("Edge か Chrome が見つかりません")


def run_browser(browser, profile, html_path, args):
    subprocess.run([browser, "--headless=new", "--disable-gpu", "--hide-scrollbars", "--no-first-run",
                    "--user-data-dir=" + profile] + args + [pathlib.Path(html_path).as_uri()],
                   check=True, capture_output=True, timeout=120)


def expected(data, images, reading_sets):
    items = []
    for i, item in enumerate(data["items"]):
        entry = {
            "code": item["code"],
            "row": i // COLUMNS + 1,
            "name": item["name"],
            "maker": item["maker"],
            "volume": item["volume"],
            "priceExcludingTax": item["priceExcludingTax"],
            "priceIncludingTax": None if item.get("priceUnreadable") else including_tax(item["priceExcludingTax"]),
            "ageLabel": None,
            "allergenLabel": item.get("allergenLabel") or [],
            "allergenLabelText": label_text(item),
            "description": full_description(data, item),
            "bulk": None,
            "unitPrice": {"unit": item["unitPrice"], "priceExcludingTax": unit_price(item)} if item.get("unitPrice") else None,
            "piecesPerPackCandidates": item["piecesPerPackCandidates"],
            "snackCategory": item["snackCategory"],
        }
        if item.get("nonFood"):
            # 食品以外の掲載品（Claude が印と理由を付けて返し、アプリが外した掲載品にする。FR-IMPORT-20）
            entry["nonFood"] = True
            entry["nonFoodReason"] = item["nonFood"]
        if item.get("_trap"):
            entry["_trap"] = item["_trap"]
        items.append(entry)
    return {
        "_note": "build.py が {}.json から作った正解データ。直接編集しない。allergenLabel はチラシの略した表記（allergenLabelText）を品目名に直したもの（カ＝カシューナッツ・か＝かに）。snackCategory・piecesPerPackCandidates は Claude の候補の目安（確定は職員）。maker・priceIncludingTax の null はチラシで読めない（再入力になる）、nonFood は食品以外の掲載品（外した掲載品になる）".format(data["id"]),
        "source": data["id"] + ".json",
        "images": images,
        "readingSets": reading_sets,
        "items": items,
    }


def build(json_path, template, browser, seen_codes):
    with io.open(json_path, encoding="utf-8") as f:
        data = json.load(f)
    check(data, seen_codes)
    base = os.path.join(SRC_DIR, data["id"])
    rows = len(data["items"]) // COLUMNS
    sheet_h = PAD * 2 + HEAD_H + CELL_H * rows + FOOT_H
    codes_of = lambda first, last: [x["code"] for i, x in enumerate(data["items"]) if first <= i // COLUMNS + 1 <= last]
    half = (rows + 1) // 2
    # 上半分・下半分の画像の高さ：半分の段がちょうど入る高さ（上下に少し余白）
    photo_h = math.ceil(CELL_H * half * PHOTO_SCALE + 60)

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
                f.write(render(template, data, sheet_h, "photo", photo_css(row_top, photo_h)))
            run_browser(browser, profile, photo_html,
                        ["--window-size={},{}".format(PHOTO_W, photo_h), "--force-device-scale-factor=1", "--screenshot={}-photo-{}.png".format(base, suffix)])
        full_w, full_h = full_photo_size(sheet_h)
        photo_html = os.path.join(work, "photo-full.html")
        with io.open(photo_html, "w", encoding="utf-8") as f:
            f.write(render(template, data, sheet_h, "photo", photo_full_css(sheet_h)))
        run_browser(browser, profile, photo_html,
                    ["--window-size={},{}".format(full_w, full_h), "--force-device-scale-factor=1", "--screenshot={}-photo-full.png".format(base)])
    finally:
        shutil.rmtree(work, ignore_errors=True)

    name = data["id"]
    images = {
        name + ".png": codes_of(1, rows),
        name + ".pdf": codes_of(1, rows),
        name + "-photo-top.png": codes_of(1, half),
        name + "-photo-bottom.png": codes_of(half + 1, rows),
        name + "-photo-full.png": codes_of(1, rows),
    }
    # 1回の読み取りで送る画像の組（読み取り精度の測定で使う。PDF は画面で画像にしてから送るので入れない）
    reading_sets = [[name + ".png"], [name + "-photo-top.png", name + "-photo-bottom.png"], [name + "-photo-full.png"]]
    with io.open(base + ".expected.json", "w", encoding="utf-8", newline="\n") as f:
        json.dump(expected(data, images, reading_sets), f, ensure_ascii=False, indent=2)
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
        flyer_codes.update(x["code"] for x in build(path, template, browser, flyer_codes)["items"])
    for path in sources_in(PACKAGE_DIR):
        build_package(path, browser, flyer_codes)


if __name__ == "__main__":
    sys.exit(main())
