# -*- coding: utf-8 -*-
"""
The cast's body language (World/SdPersona): Gemini reads every hero's profile — name, rank,
department, nickname, bio, lines, role — and gives each a persona archetype, an idle and a victory
from that archetype's own pool, balanced across the cast so a squad does not stand alike (the
user: "캐릭터들의 자세가 너무 획일화"). Written into sdspec as hand edits (sd_spec.py --set).

    python tools/sdspec_persona_gemini.py            # print the plan
    python tools/sdspec_persona_gemini.py --apply    # and write it
SECURITY: the key is read from the env file by NAME; never printed or written.
"""
import collections, json, os, subprocess, sys, urllib.request
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import gen_cards_gemini as g

DATA = os.path.join(g.ROOT, "Assets", "ExcelHeroes", "Resources", "Data")
# kept in step with World/SdPersona.Table
POOLS = {
    "confident": ([1, 2, 9], [1, 8, 10]), "elegant": ([10, 0, 6], [3, 2, 11]), "shy": ([10, 3], [4, 7]),
    "energetic": ([12, 0, 5], [0, 5, 6]), "lazy": ([13, 4, 9], [4, 8]), "stern": ([8, 2], [9, 3, 11]),
    "nerdy": ([6, 3, 11], [2, 10]), "cool": ([9, 4], [8, 10]), "cheerful": ([0, 5, 12], [6, 2, 4]),
    "caring": ([10, 1], [7, 4]), "executive": ([8, 2], [11, 3, 9]), "playful": ([5, 11, 1], [2, 6, 10]),
}
IDLES = ("0 stand, weight on one leg · 1 hands on hips · 2 arms crossed · 3 hand at the chin, thinking · 4 relaxed lean back · "
         "5 one hand at the shoulder · 6 pushing up glasses / tucking hair · 7 a big stretch · 8 hands behind the back · "
         "9 hands in pockets · 10 hands clasped in front · 11 checking the phone · 12 bouncing on the toes · 13 sleepy, yawning")
WINS = ("0 arms up, hopping · 1 fist pump · 2 V-sign by the cheek · 3 a bow · 4 a wave · 5 a spin · 6 double V · 7 clapping · "
        "8 thumbs up · 9 a salute · 10 pointing at the camera · 11 hand on the heart, a nod")


def heroes():
    d = json.load(open(os.path.join(DATA, "heroes.json"), encoding="utf-8"))
    L = d if isinstance(d, list) else next(v for v in d.values() if isinstance(v, list))
    keys = ("id", "name", "grade", "role", "dept", "nick", "gender", "bio", "line", "ult")
    return [{k: h.get(k, "") for k in keys} for h in L]


def ask(key, cast):
    pools = "\n".join(f"- {n}: idles {i}, victories {w}" for n, (i, w) in POOLS.items())
    text = (f"You are casting body language for a chibi office-worker hero game (Blue Archive-style SD battle figures). "
            f"Here is the whole cast as JSON:\n{json.dumps(cast, ensure_ascii=False)}\n\n"
            f"Give EVERY character one persona archetype from this list, with an idle and a victory taken ONLY from that archetype's pools:\n{pools}\n"
            f"Idles: {IDLES}\nVictories: {WINS}\n"
            "Read who each person is (rank, department, nickname, bio, lines) and choose what they would really do. Balance the cast: "
            "no archetype more than 7 times, use all twelve, and avoid giving two characters the same (archetype, idle, victory) combination. "
            'Answer JSON only: [{"id": "...", "persona": "...", "idle": n, "win": n, "why": "a few words"}]')
    body = json.dumps({"contents": [{"parts": [{"text": text}]}],
                       "generationConfig": {"temperature": 0.4, "responseMimeType": "application/json"}}).encode("utf-8")
    req = urllib.request.Request("https://generativelanguage.googleapis.com/v1beta/models/gemini-3.1-pro-preview:generateContent", data=body, method="POST",
                                 headers={"Content-Type": "application/json", "x-goog-api-key": key})
    with urllib.request.urlopen(req, timeout=400) as r: d = json.loads(r.read().decode("utf-8"))
    t = "".join(p.get("text", "") for c in d.get("candidates", []) for p in c.get("content", {}).get("parts", []) if not p.get("thought"))
    return json.loads(t)


def fix(plan, cast):
    """Keeps the answer inside the rules: a known archetype, idle / win from its pools."""
    ids = {h["id"] for h in cast}
    out = []
    for r in plan:
        if r.get("id") not in ids: continue
        p = r.get("persona") if r.get("persona") in POOLS else "cheerful"
        i, w = POOLS[p]
        out.append({"id": r["id"], "persona": p, "idle": r.get("idle") if r.get("idle") in i else i[0],
                    "win": r.get("win") if r.get("win") in w else w[0], "why": r.get("why", "")})
    return out


if __name__ == "__main__":
    cast = heroes()
    plan = fix(ask(g.read_key(), cast), cast)
    json.dump(plan, open(os.path.join(g.ROOT, "tools", "out", "persona.json"), "w", encoding="utf-8"), ensure_ascii=False, indent=1)
    names = {h["id"]: h["name"] for h in cast}
    for r in plan: print(f'{r["id"]:14s} {names.get(r["id"], ""):8s} {r["persona"]:10s} idle{r["idle"]:<3d} win{r["win"]:<3d} {r["why"]}')
    print(collections.Counter(r["persona"] for r in plan))
    print("combos unique:", len({(r["persona"], r["idle"], r["win"]) for r in plan}), "/", len(plan), " missing:", sorted({h["id"] for h in cast} - {r["id"] for r in plan}))
    if "--apply" in sys.argv:
        spec = os.path.join(os.path.dirname(os.path.abspath(__file__)), "sd_spec.py")
        for r in plan:
            subprocess.run([sys.executable, spec, "--set", r["id"], f'persona={r["persona"]}', f'idle={r["idle"]}', f'win={r["win"]}'], check=False, capture_output=True)
        print("applied", len(plan))
