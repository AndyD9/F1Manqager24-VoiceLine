"""List drivers and race engineers (with staff IDs) from an F1 Manager 2024 career save.

Read-only port of F1M24 Voice Manager's SaveGameReader/NativeSqlite logic:
the .sav embeds a zlib-compressed SQLite database located after a fixed
signature. The save file is never modified.

Usage: python read_save_staff.py [save.sav] [--json] [--f1]
"""

import json
import os
import sqlite3
import struct
import sys
import tempfile
import zlib

DB_SIGNATURE = bytes([0, 5, 0, 0, 0]) + b"None\x00" + bytes([5, 0, 0, 0]) + b"None\x00"

STAFF_QUERY = """
SELECT b.StaffID, b.FirstName, b.LastName, b.Gender, b.IsGeneratedStaff,
       COALESCE(d.DriverCode, ''), g.StaffType,
       COALESCE(t.TeamName, ''), t.Formula, c.PosInTeam
FROM Staff_BasicData b
INNER JOIN Staff_GameData g ON g.StaffID = b.StaffID
LEFT JOIN Staff_DriverData d ON d.StaffID = b.StaffID
LEFT JOIN Staff_Contracts c ON c.StaffID = b.StaffID AND c.ContractType = 0
LEFT JOIN Teams t ON t.TeamID = c.TeamID
WHERE g.StaffType IN (0, 2)
ORDER BY t.Formula IS NULL, t.Formula, t.TeamID, c.PosInTeam, b.LastName, b.FirstName
"""

# Staff IDs with shipped radio recordings (from Voice Manager's VoiceCatalog).
DRIVER_VOICE_IDS = {
    1, 2, 3, 8, 9, 10, 11, 12, 13, 14, 15, 17, 18, 20, 22, 23, 74, 77, 78, 80,
    81, 83, 87, 88, 91, 95, 99, 102, 105, 106, 107, 109, 110, 114, 115, 116,
    117, 119, 120, 121, 123, 127, 128, 130, 132, 133, 135, 140, 141, 142, 143,
    144, 242, 243, 244, 245, 246, 247, 248, 249, 250, 251, 252, 253, 255, 264,
    265, 267, 268, 269, 270, 272, 273, 274, 275, 276, 277, 278, 279, 280, 281,
    282, 283, 284, 285, 286, 287, 288, 289, 300, 301, 302, 303, 304, 305, 306,
    307, 308, 322, 371, 376, 377, 401, 402, 405, 409, 410, 413,
}
ENGINEER_VOICE_IDS = {
    6, 7, 47, 48, 49, 50, 51, 54, 55, 56, 58, 59, 60, 61, 63, 64, 148, 149,
    150, 151, 155, 158, 159, 160, 161, 174, 176, 320, 321, 393, 415,
}


def find_db_offset(data):
    start = 0
    while True:
        i = data.find(DB_SIGNATURE, start)
        if i < 0:
            return -1
        off = i + len(DB_SIGNATURE) + 4
        if off + 16 <= len(data):
            csize, usize = struct.unpack_from("<ii", data, off)
            if csize > 0 and usize > 0 and csize <= len(data) - (off + 16):
                return off
        start = i + 1


def decode_name(value):
    prefix = "[STRING_LITERAL:Value=|"
    if value.startswith(prefix):
        end = value.find("|", len(prefix))
        if end > len(prefix):
            return value[len(prefix):end]
    if value.startswith("[") and value.endswith("]"):
        value = value[1:-1]
        cut = value.rfind("_")
        if 0 <= cut < len(value) - 1:
            value = value[cut + 1:]
    if len(value) > 1 and value[-1].isdigit():
        return value[:-1]
    return value


def read_staff(save_path):
    with open(save_path, "rb") as f:
        data = f.read()
    off = find_db_offset(data)
    if off < 0:
        raise ValueError("Not a supported F1 Manager 2024 career save.")
    csize, usize = struct.unpack_from("<ii", data, off)
    db_bytes = zlib.decompress(data[off + 16:off + 16 + csize])[:usize]

    fd, tmp = tempfile.mkstemp(suffix=".db")
    try:
        with os.fdopen(fd, "wb") as f:
            f.write(db_bytes)
        con = sqlite3.connect(f"file:{tmp}?mode=ro", uri=True)
        try:
            rows = con.execute(STAFF_QUERY).fetchall()
        finally:
            con.close()
    finally:
        os.remove(tmp)

    staff = []
    for sid, first, last, gender, generated, code, stype, team, formula, pos in rows:
        first, last = decode_name(first or ""), decode_name(last or "")
        if first.lower() == "placeholder" and last.lower() == "placeholder":
            continue
        role = "driver" if stype == 0 else "engineer"
        voiced = sid in (DRIVER_VOICE_IDS if role == "driver" else ENGINEER_VOICE_IDS)
        staff.append({
            "id": sid, "name": f"{first} {last}".strip(), "role": role,
            "gender": gender, "generated": bool(generated), "code": decode_name(code),
            "team": team, "formula": formula, "pos_in_team": pos,
            "has_recorded_voice": voiced,
        })
    return staff


def default_save():
    folder = os.path.join(os.environ["LOCALAPPDATA"], "F1Manager24", "Saved", "SaveGames")
    saves = [os.path.join(folder, n) for n in os.listdir(folder)
             if n.startswith(("save", "autosave")) and n.endswith(".sav")]
    return max(saves, key=os.path.getmtime)


if __name__ == "__main__":
    sys.stdout.reconfigure(encoding="utf-8")
    args = [a for a in sys.argv[1:] if not a.startswith("--")]
    f1_only = "--f1" in sys.argv
    path = args[0] if args else default_save()
    staff = read_staff(path)
    if "--json" in sys.argv:
        print(json.dumps(staff, ensure_ascii=False, indent=1))
    else:
        print(f"Save: {path}")
        for role in ("driver", "engineer"):
            people = [s for s in staff if s["role"] == role
                      and (not f1_only or s["formula"] == 1)]
            print(f"\n== {role}s ({len(people)}) ==")
            for s in people:
                flags = ("voice" if s["has_recorded_voice"] else "NO VOICE") + \
                        (", generated" if s["generated"] else "")
                team = f"F{s['formula']} {s['team']}" if s["team"] else "-"
                print(f"  {s['id']:>5}  {s['code']:<4} {s['name']:<28} {team:<22} [{flags}]")
