"""配置の前に、差分のマイグレーションの SQL を確かめる（スキル deploy-package から使う）。

使い方: python -I check_migration.py <当て済みまでの SQL> <差分の SQL>

空の一時 DB に「当て済みまで」の SQL を当てて本番の状態を再現し、差分の SQL を当てる。
記録の行・integrity_check・危ない文（DROP TABLE・RENAME・DELETE FROM）を報告する。
"""

from __future__ import annotations

import os
import re
import sqlite3
import sys
import tempfile


def read_sql(path: str) -> str:
    # BOM の有無にかかわらず読む
    with open(path, encoding="utf-8-sig") as f:
        return f.read()


def history(conn: sqlite3.Connection) -> list[str]:
    return [r[0] for r in conn.execute('select MigrationId from "__EFMigrationsHistory" order by 1')]


def main() -> int:
    if len(sys.argv) != 3:
        print(__doc__)
        return 2

    base_sql = read_sql(sys.argv[1])
    diff_sql = read_sql(sys.argv[2])

    risky = [
        line.strip()
        for line in diff_sql.splitlines()
        if re.search(r"\b(DROP\s+TABLE|RENAME|DELETE\s+FROM)\b", line, re.IGNORECASE)
    ]
    history_inserts = len(re.findall(r'INSERT INTO "__EFMigrationsHistory"', diff_sql))

    fd, db_path = tempfile.mkstemp(suffix=".db")
    os.close(fd)
    ok = True
    try:
        conn = sqlite3.connect(db_path)
        conn.executescript(base_sql)
        before = history(conn)
        print("当て済みの状態:", before[-1] if before else "(なし)")

        conn.executescript(diff_sql)
        after = history(conn)
        added = after[len(before):]
        print("差分で増えた記録:", added)
        if len(added) != history_inserts or not added:
            print("NG: 記録の行の数が合わない（SQL の INSERT は", history_inserts, "行）")
            ok = False

        integrity = conn.execute("pragma integrity_check").fetchone()[0]
        print("integrity_check:", integrity)
        if integrity != "ok":
            ok = False

        allergens = conn.execute("select count(*) from allergens").fetchone()[0]
        print("allergens の行数:", allergens)
        conn.close()
    except sqlite3.Error as e:
        print("NG: SQL を当てられない:", e)
        ok = False
    finally:
        os.remove(db_path)

    if risky:
        print("注意: データが消える・移るかもしれない文があります。中身を読んで説明すること:")
        for line in risky:
            print("  ", line[:160])
    else:
        print("危ない文: なし")

    print("結果:", "OK" if ok else "NG")
    return 0 if ok else 1


if __name__ == "__main__":
    sys.exit(main())
