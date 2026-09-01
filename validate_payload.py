from pathlib import Path

ROOT = Path(__file__).resolve().parent
ANIM = ROOT / "payload" / "animationdatasinglefile.txt"
ASET = ROOT / "payload" / "animationsetdatasinglefile.txt"
PREFIXES = ("tes4oblivion_", "tes4morrowind_")


def validate_animdata(path: Path):
    lines = path.read_text(encoding="utf-8-sig").splitlines()
    count = int(lines[0])
    names = lines[1:1 + count]
    pos = 1 + count
    tes = 0
    tes_motion = 0
    for name in names:
        anim_count = int(lines[pos]); pos += 1
        block = lines[pos:pos + anim_count]; pos += anim_count
        asset_count = int(block[1])
        has_motion = int(block[2 + asset_count])
        if name.lower().startswith(PREFIXES):
            tes += 1
            tes_motion += has_motion
        if has_motion:
            motion_count = int(lines[pos]); pos += 1 + motion_count
    assert pos == len(lines), (pos, len(lines))
    return count, tes, tes_motion


def validate_animset(path: Path):
    lines = path.read_text(encoding="utf-8-sig").splitlines()
    count = int(lines[0])
    names = lines[1:1 + count]
    tes = sum(Path(n.replace('\\', '/')).name.lower().startswith(PREFIXES) for n in names)
    return count, tes


if __name__ == "__main__":
    a = validate_animdata(ANIM)
    s = validate_animset(ASET)
    print(f"AnimData: projects={a[0]}, TES4={a[1]}, TES4 with motion={a[2]}")
    print(f"AnimSetData: projects={s[0]}, TES4={s[1]}")
    assert a == (539, 110, 110)
    assert s == (159, 110)
    print("Payload validation PASS")
