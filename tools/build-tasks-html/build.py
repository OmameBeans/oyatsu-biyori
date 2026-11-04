"""docs/implementation-tasks/implementation-tasks.md を、目次と絞り込みつきの1枚の HTML にする。

使い方（リポジトリのどこからでも実行できる）:
    python tools/build-tasks-html/build.py

出力: docs/implementation-tasks/index.html（md を直したら作り直す）

- 「ID | タスク | ...」の表は、1タスク1枚のカードにする（長い本文を読みやすくするため）
- 本文中の T1-05・U-11 などは、そのタスク・論点へのリンクにする
- ../requirements/xx.md へのリンクは、要件定義の閲覧用ページ（../requirements/index.html）へ向ける
"""
import html
import io
import os
import re

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))  # リポジトリのルート
SRC = os.path.join(ROOT, "docs", "implementation-tasks", "implementation-tasks.md")
DEST_DIR = os.path.dirname(SRC)  # HTML は md と同じフォルダに出す

# 前後は ASCII だけで区切る（「T4-09を」のように日本語が続いても拾うため）
ID_RE = re.compile(r"(?<![A-Za-z0-9_-])((?:T\d|TD)-\d\d|U-\d\d)(?![A-Za-z0-9_-])")
PRIORITY = {"必須": "b-must", "あると良い": "b-nice", "余裕があれば": "b-extra"}
ISSUES_URL = "https://github.com/OmameBeans/oyatsu-biyori/issues/"  # 13. ブラッシュアップの issue

known_ids = set()  # リンク先として存在する ID（カードと表の行）


def slug(text):
    """GitHub と同じ規則で見出しのアンカーを作る（md 内のリンクと合わせるため）。"""
    t = re.sub(r"<[^>]+>", "", text).lower()
    t = re.sub(r"[^\w\- ]", "", t)
    return t.replace(" ", "-")


def plain(md_text):
    return re.sub(r"[*`]|\[([^\]]*)\]\([^)]*\)", lambda m: m.group(1) or "", md_text)


def fix_link(url):
    # md と HTML は同じフォルダにあるので、要件定義の md へのリンクだけ閲覧用ページに向け直す
    m = re.match(r"^\.\./requirements/(\d\d-[\w-]+)\.md(?:#(.*))?$", url)
    if m:
        stem, frag = m.group(1), m.group(2)
        return "../requirements/index.html#" + (f"{stem}--{frag}" if frag else stem)
    return url


def link_ids(text):
    """コード・リンク・タグの外にある T1-05・U-11 などをリンクにする。"""
    parts = re.split(r"(<a\b.*?</a>|<code>.*?</code>|<[^>]+>)", text)
    for i in range(0, len(parts), 2):
        parts[i] = ID_RE.sub(
            lambda m: f'<a class="ref" href="#{m.group(1)}">{m.group(1)}</a>' if m.group(1) in known_ids else m.group(1),
            parts[i],
        )
    return "".join(parts)


def inline(text, ids=True):
    codes = []

    def keep_code(m):
        codes.append(f"<code>{html.escape(m.group(1))}</code>")
        return f"\x00{len(codes) - 1}\x00"

    text = re.sub(r"`([^`]+)`", keep_code, text)
    text = html.escape(text, quote=False)
    text = re.sub(r"&lt;br\s*/?&gt;", "<br>", text)
    text = re.sub(r"\*\*(.+?)\*\*", r"<strong>\1</strong>", text)
    text = re.sub(
        r"\[([^\]]+)\]\(([^)]+)\)",
        lambda m: f'<a href="{html.escape(fix_link(html.unescape(m.group(2))))}">{m.group(1)}</a>',
        text,
    )
    text = re.sub(r"\x00(\d+)\x00", lambda m: codes[int(m.group(1))], text)
    return link_ids(text) if ids else text


def split_row(line):
    return [c.strip().replace("\\|", "|") for c in re.split(r"(?<!\\)\|", line.strip().strip("|"))]


def mark(value, label, cls):
    return f'<span class="badge {cls}">{label}</span>' if value.strip() == "○" else ""


def render_cards(head, rows):
    """「ID | タスク | 要件 | テスト | 安全 | 状態」または「ID | 優先度 | タスク | 要件」の表をカードにする。
    「状態」の列（進み具合の記録）があれば、カードの下に出す（md と HTML で読める情報をそろえる）。"""
    col = {name: i for i, name in enumerate(head)}
    out = ['<div class="cards">']
    for r in rows:
        get = lambda name: r[col[name]] if name in col and col[name] < len(r) else ""
        tid, task, reqs = get("ID"), get("タスク"), get("要件")
        # 13. ブラッシュアップの「#12」は GitHub の issue の番号。アンカーは issue-12、リンクは issue へ
        if tid.startswith("#"):
            anchor, href = f"issue-{tid[1:]}", ISSUES_URL + tid[1:]
        else:
            anchor, href = tid, f"#{tid}"
        safety, test, prio = get("安全") == "○", get("テスト") == "○", get("優先度")
        badges = mark(get("安全"), "安全レビュー", "b-safety") + mark(get("テスト"), "xUnit", "b-test")
        if prio in PRIORITY:
            badges += f'<span class="badge {PRIORITY[prio]}">{html.escape(prio)}</span>'
        if get("種類"):
            badges += f'<span class="badge b-extra">{html.escape(get("種類"))}</span>'
        if get("リスク"):
            badges += f'<span class="badge b-fix">リスク {html.escape(get("リスク"))}</span>'
        # 「NFR-SEC-08, 09」「SR-03, 07」のような番号だけの続きは、前の ID と同じチップにまとめる
        parts = []
        for c in (c.strip() for c in re.split(r",\s*", reqs)):
            if parts and re.match(r"^\d", c) and not re.match(r"^\d\d ", c):
                parts[-1] += ", " + c
            elif c:
                parts.append(c)
        chips = "".join(f'<span class="chip">{inline(c)}</span>' for c in parts)
        out.append(
            f'<article class="card{" is-safety" if safety else ""}" id="{anchor}" data-item'
            f' data-safety="{int(safety)}" data-test="{int(test)}">'
            f'<header><a class="tid" href="{href}">{html.escape(tid)}</a>{badges}</header>'
            f'<div class="task">{inline(task)}</div>'
            + (f'<footer><span class="req-label">要件</span>{chips}</footer>' if chips else "")
            + (f'<div class="status"><span class="req-label">状態</span>{inline(get("状態"))}</div>' if get("状態") else "")
            + "</article>"
        )
    out.append("</div>")
    return "".join(out)


def render_table(head, rows):
    t = ['<div class="table-wrap"><table><thead><tr>']
    t += [f"<th>{inline(c)}</th>" for c in head]
    t.append("</tr></thead><tbody>")
    for r in rows:
        rid = r[0] if r and ID_RE.fullmatch(r[0]) else ""
        attrs = f' id="{rid}" data-item' if rid and rid not in seen_rows else ""
        if rid:
            seen_rows.add(rid)
        cells = []
        for c in r:
            if c == "○":
                cells.append('<td class="center"><span class="badge b-fix">○</span></td>')
            else:
                cells.append(f"<td>{inline(c)}</td>")
        t.append(f"<tr{attrs}>" + "".join(cells) + "</tr>")
    t.append("</tbody></table></div>")
    return "".join(t)


seen_rows = set()


def render_list(items):
    parts, stack = [], []
    for depth, ordered, text in items:
        tag = "ol" if ordered else "ul"
        while len(stack) > depth + 1:
            parts.append(f"</li></{stack.pop()}>")
        if len(stack) < depth + 1:
            while len(stack) < depth + 1:
                parts.append(f"<{tag}>")
                stack.append(tag)
        else:
            parts.append("</li>")
        parts.append(f"<li>{inline(text)}")
    while stack:
        parts.append(f"</li></{stack.pop()}>")
    return "".join(parts)


def collect_ids(lines):
    for line in lines:
        if line.startswith("|"):
            first = split_row(line)[0]
            if ID_RE.fullmatch(first):
                known_ids.add(first)


def convert(md):
    """md を、h2 ごとの <section> に分けた HTML にする。目次とタスク数も返す。"""
    lines = md.splitlines()
    collect_ids(lines)
    sections, toc, title, lead = [], [], "", []
    cur = None  # 今の h2 の {"id", "title", "body": [], "count"}
    i = 0

    def emit(s):
        (cur["body"] if cur else lead).append(s)

    while i < len(lines):
        line = lines[i]
        m = re.match(r"^(#{1,4}) (.+)$", line)
        if m:
            level, text = len(m.group(1)), m.group(2).strip()
            if level == 1:
                title = plain(text)
            elif level == 2:
                cur = {"id": slug(text), "title": text, "body": [], "count": 0}
                sections.append(cur)
                toc.append([2, cur["id"], plain(text), cur])
            else:
                hid = slug(text)
                emit(f'<h{level} id="{hid}">{inline(text)}</h{level}>')
                if level == 3:
                    toc.append([3, hid, plain(text), None])
            i += 1
            continue
        if re.match(r"^---+\s*$", line):
            i += 1  # 区切り線は section の境目で足りるので出さない
            continue
        if line.startswith("|") and i + 1 < len(lines) and re.match(r"^\|[\s:|-]+\|\s*$", lines[i + 1]):
            head = split_row(line)
            i += 2
            rows = []
            while i < len(lines) and lines[i].startswith("|"):
                rows.append(split_row(lines[i]))
                i += 1
            if head[:1] == ["ID"] and "タスク" in head:
                emit(render_cards(head, rows))
                if cur:
                    cur["count"] += len(rows)
            else:
                emit(render_table(head, rows))
            continue
        if line.startswith(">"):
            buf = []
            while i < len(lines) and lines[i].startswith(">"):
                buf.append(lines[i].lstrip(">").strip())
                i += 1
            emit(f"<blockquote><p>{'<br>'.join(inline(b) for b in buf)}</p></blockquote>")
            continue
        if re.match(r"^\s*([-*]|\d+\.) ", line):
            items = []
            while i < len(lines) and re.match(r"^\s*([-*]|\d+\.) ", lines[i]):
                mm = re.match(r"^(\s*)([-*]|\d+\.) (.*)$", lines[i])
                items.append((len(mm.group(1)) // 2, mm.group(2)[0].isdigit(), mm.group(3)))
                i += 1
            emit(render_list(items))
            continue
        if line.strip() == "":
            i += 1
            continue
        buf = []
        while i < len(lines) and lines[i].strip() and not re.match(r"^(#|\||>|\s*([-*]|\d+\.) |---)", lines[i]):
            buf.append(lines[i].strip())
            i += 1
        emit(f"<p>{'<br>'.join(inline(b) for b in buf)}</p>")

    body = [f'<section class="doc lead" id="top"><h1>{html.escape(title)}</h1>{"".join(lead)}{{{{STATS}}}}</section>']
    for s in sections:
        # 見出しの「〔作る順 1〕」はバッジにする
        m = re.match(r"^(.*?)\s*〔(.+)〕$", s["title"])
        h2 = inline(m.group(1)) + f' <span class="order">{html.escape(m.group(2))}</span>' if m else inline(s["title"])
        count = f'<span class="count">{s["count"]}件</span>' if s["count"] else ""
        body.append(
            f'<section class="doc" id="{s["id"]}" data-section><h2>{h2}{count}</h2>{"".join(s["body"])}'
            '<p class="empty">この節には条件に合うものがありません。</p></section>'
        )
    return title, "\n".join(body), toc


def build_nav(toc):
    out = []
    for level, hid, text, sec in toc:
        text = re.sub(r"\s*〔.+〕$", "", text)
        count = f'<span class="n">{sec["count"]}</span>' if sec and sec["count"] else ""
        out.append(f'<li class="lv{level}"><a href="#{hid}">{html.escape(text)}{count}</a></li>')
    return "\n".join(out)


def build_stats(body):
    cards = re.findall(r'<article class="card[^"]*" id="[^"]+" data-item data-safety="(\d)" data-test="(\d)"', body)
    total = len(cards)
    safety = sum(1 for s, _ in cards if s == "1")
    test = sum(1 for _, t in cards if t == "1")
    return (
        '<div class="stats">'
        f'<div class="stat"><b>{total}</b><span>タスク</span></div>'
        f'<div class="stat s-safety"><b>{safety}</b><span>安全レビューが要る</span></div>'
        f'<div class="stat s-test"><b>{test}</b><span>xUnit を書く</span></div>'
        "</div>"
    )


md = io.open(SRC, encoding="utf-8-sig").read()
title, body, toc = convert(md)
body = body.replace("{{STATS}}", build_stats(body))
template = io.open(os.path.join(os.path.dirname(__file__), "template.html"), encoding="utf-8").read()
result = (
    template.replace("{{TITLE}}", html.escape(title))
    .replace("{{NAV}}", build_nav(toc))
    .replace("{{BODY}}", body)
)
os.makedirs(DEST_DIR, exist_ok=True)
dest = os.path.join(DEST_DIR, "index.html")
io.open(dest, "w", encoding="utf-8", newline="\n").write(result)
print(f"作成しました: {os.path.relpath(dest, ROOT)}")
