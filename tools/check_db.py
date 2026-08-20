import sqlite3
c = sqlite3.connect(r"D:\Develop\Material_Mgmt\data\material_mgmt.db")
print("tables:", [r[0] for r in c.execute("select name from sqlite_master where type='table'")])
print("users:", list(c.execute("select username, role from users")))
print("materials:", c.execute("select count(*) from materials").fetchone()[0])
