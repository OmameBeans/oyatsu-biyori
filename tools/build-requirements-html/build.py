"""docs/README.md と docs/requirements/*.md を、サイドバー目次つきの1枚の HTML にまとめる。

使い方（リポジトリのどこからでも実行できる）:
    python tools/build-requirements-html/build.py

出力: docs/requirements/index.html（md を直したら作り直す）
"""
import html
import io
import os
import re

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))  # リポジトリのルート
REQ = os.path.join(ROOT, "docs", "requirements")
FILES = [("README", os.path.join(ROOT, "docs", "README.md"))] + [
    (os.path.splitext(f)[0], os.path.join(REQ, f))
    for f in sorted(os.listdir(REQ))
    if re.match(r"\d\d-.*\.md$", f)
]

BADGES = {
    "必須": "b-must", "あると良い": "b-nice", "余裕があれば": "b-extra",
    "決定": "b-done", "一部決定": "b-part", "未決": "b-open",
}


def slug(text):
    """GitHub と同じ規則で見出しのアンカーを作る（md 内のリンクと合わせるため）。"""
    t = re.sub(r"<[^>]+>", "", text).lower()
    t = re.sub(r"[^\w\- ]", "", t)
    return t.replace(" ", "-")


def plain(md_text):
    return re.sub(r"[*`]|\[([^\]]*)\]\([^)]*\)", lambda m: m.group(1) or "", md_text)


def fix_link(url, stem):
    m = re.match(r"^(?:\.\./requirements/|requirements/|\./)?(README|\d\d-[\w-]+)\.md(?:#(.*))?$", url)
    if m:
        target, frag = m.group(1), m.group(2)
        return f"#{target}--{frag}" if frag else f"#{target}"
    if url.startswith("#"):
        return f"#{stem}--{url[1:]}"
    if not re.match(r"^[a-z]+:", url) and stem == "README":
        return "../" + url  # README は docs/ 直下にあるため、相対パスを1段上げる
    return url


def inline(text, stem):
    codes = []

    def keep_code(m):
        codes.append(f"<code>{html.escape(m.group(1))}</code>")
        return f"\x00{len(codes) - 1}\x00"

    text = re.sub(r"`([^`]+)`", keep_code, text)
    text = html.escape(text, quote=False)
    text = re.sub(r"&lt;br\s*/?&gt;", "<br>", text)  # 表のセル内の改行（GitHub の md 表示と同じ扱い）
    text = re.sub(r"\*\*(.+?)\*\*", r"<strong>\1</strong>", text)
    text = re.sub(
        r"\[([^\]]+)\]\(([^)]+)\)",
        lambda m: f'<a href="{html.escape(fix_link(html.unescape(m.group(2)), stem))}">{m.group(1)}</a>',
        text,
    )
    return re.sub(r"\x00(\d+)\x00", lambda m: codes[int(m.group(1))], text)


def cell(text, stem):
    s = text.strip()
    if s in BADGES:
        return f'<span class="badge {BADGES[s]}">{html.escape(s)}</span>'
    return inline(s, stem)


def split_row(line):
    return [c for c in re.split(r"(?<!\\)\|", line.strip().strip("|"))]


def convert(md, stem):
    lines = md.splitlines()
    out, toc = [], []
    i = 0
    while i < len(lines):
        line = lines[i]
        if line.startswith("```"):
            buf = []
            i += 1
            while i < len(lines) and not lines[i].startswith("```"):
                buf.append(lines[i])
                i += 1
            out.append(f"<pre><code>{html.escape(chr(10).join(buf))}</code></pre>")
            i += 1
            continue
        m = re.match(r"^(#{1,4}) (.+)$", line)
        if m:
            level, text = len(m.group(1)), m.group(2).strip()
            hid = stem if level == 1 else f"{stem}--{slug(text)}"
            out.append(f'<h{level} id="{hid}">{inline(text, stem)}</h{level}>')
            if level <= 3:
                toc.append((level, hid, plain(text)))
            i += 1
            continue
        if re.match(r"^---+\s*$", line):
            out.append("<hr>")
            i += 1
            continue
        if line.startswith("|") and i + 1 < len(lines) and re.match(r"^\|[\s:|-]+\|\s*$", lines[i + 1]):
            head = split_row(line)
            i += 2
            rows = []
            while i < len(lines) and lines[i].startswith("|"):
                rows.append(split_row(lines[i]))
                i += 1
            t = ['<div class="table-wrap"><table><thead><tr>']
            t += [f"<th>{cell(c, stem)}</th>" for c in head]
            t.append("</tr></thead><tbody>")
            for r in rows:
                cls = ' class="safety"' if r and re.match(r"\s*SR-\d", r[0]) else ""
                t.append(f"<tr{cls}>" + "".join(f"<td>{cell(c, stem)}</td>" for c in r) + "</tr>")
            t.append("</tbody></table></div>")
            out.append("".join(t))
            continue
        if line.startswith(">"):
            buf = []
            while i < len(lines) and lines[i].startswith(">"):
                buf.append(lines[i].lstrip(">").strip())
                i += 1
            out.append(f"<blockquote><p>{'<br>'.join(inline(b, stem) for b in buf)}</p></blockquote>")
            continue
        if re.match(r"^\s*([-*]|\d+\.) ", line):
            items = []
            while i < len(lines) and re.match(r"^\s*([-*]|\d+\.) ", lines[i]):
                mm = re.match(r"^(\s*)([-*]|\d+\.) (.*)$", lines[i])
                items.append((len(mm.group(1)) // 2, mm.group(2)[0].isdigit(), mm.group(3)))
                i += 1
            out.append(render_list(items, stem))
            continue
        if line.strip() == "":
            i += 1
            continue
        buf = []
        while i < len(lines) and lines[i].strip() and not re.match(r"^(#|\||```|>|\s*([-*]|\d+\.) |---)", lines[i]):
            buf.append(lines[i].strip())
            i += 1
        out.append(f"<p>{'<br>'.join(inline(b, stem) for b in buf)}</p>")
    return "\n".join(out), toc


def render_list(items, stem):
    html_parts, stack = [], []
    for depth, ordered, text in items:
        tag = "ol" if ordered else "ul"
        while len(stack) > depth + 1:
            html_parts.append(f"</li></{stack.pop()}>")
        if len(stack) < depth + 1:
            while len(stack) < depth + 1:
                html_parts.append(f"<{tag}>")
                stack.append(tag)
        else:
            html_parts.append("</li>")
        html_parts.append(f"<li>{inline(text, stem)}")
    while stack:
        html_parts.append(f"</li></{stack.pop()}>")
    return "".join(html_parts)


sections, nav = [], []
for stem, path in FILES:
    body, toc = convert(io.open(path, encoding="utf-8-sig").read(), stem)
    sections.append(f'<section class="doc" id="sec-{stem}">{body}</section>')
    title = next((t for lv, _, t in toc if lv == 1), stem)
    subs = "".join(
        f'<li class="lv{lv}"><a href="#{hid}">{html.escape(t)}</a></li>' for lv, hid, t in toc if lv > 1
    )
    nav.append(
        f'<li class="doc-item"><a class="doc-link" href="#{stem}">{html.escape(title)}</a>'
        f'<ul class="sub">{subs}</ul></li>'
    )

TEMPLATE = io.open(os.path.join(os.path.dirname(__file__), "template.html"), encoding="utf-8").read()
result = TEMPLATE.replace("{{NAV}}", "\n".join(nav)).replace("{{BODY}}", "\n".join(sections))
dest = os.path.join(REQ, "index.html")
io.open(dest, "w", encoding="utf-8", newline="\n").write(result)
print(f"作成しました: {os.path.relpath(dest, ROOT)}")
