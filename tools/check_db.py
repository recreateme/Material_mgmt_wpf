"""可选：检查本地 SQLite 库（在仓库根目录执行）。"""
import os
import sqlite3

root = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
db = os.path.join(root, "data", "material_mgmt.db")
if not os.path.isfile(db):
    print(f"数据库不存在: {db}")
    raise SystemExit(1)

c = sqlite3.connect(db)
print("tables:", [r[0] for r in c.execute("select name from sqlite_master where type='table'")])
print("users:", list(c.execute("select username, role from users")))
print("materials:", c.execute("select count(*) from materials").fetchone()[0])
