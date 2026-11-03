# システム構成図の元原稿

`../system.html` と `../system.png`・`../system-dark.png` は、ここにあるファイルから作る生成物。図を直すときは `system.src.html` を編集して作り直す（生成物は直接編集しない）。

- `system.src.html`：図の元原稿（stack-diagram スキルのテンプレートが元）。アイコンは `%%ICON_<名前>%%` で参照する
- `icons/`：アイコン（`manifest.json` に取得元を記録）

## 作り直す

リポジトリのルートで実行する（Python 3.9 以上が必要）。

```
python .claude/skills/stack-diagram/scripts/build.py docs/architecture/src/system.src.html docs/architecture/src/icons docs/architecture/system.html --fonts .claude/skills/stack-diagram/assets/fonts
python .claude/skills/stack-diagram/scripts/render_png.py docs/architecture/system.html docs/architecture/system.png --selector .canvas --scale 2
```

PNG を描き出すには Playwright が必要（`pip install playwright` と `python -m playwright install chromium`）。ライト版の `system.png` とダーク版の `system-dark.png` が両方できる。
