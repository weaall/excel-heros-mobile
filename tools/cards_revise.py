# -*- coding: utf-8 -*-
"""
Targeted card revisions (Gemini image edit): the cast's framing made even (a few cards were
drawn far closer or far wider than the rest, or in another pose), and the outfits given more
variety in REAL office colours (five green / teal blazers, a floral suit, orange suits) with a
tasteful, grade-matched allure for the women — the user's round of 2026-09-29.

Image 1 = the character's current card (identity: face, hair, eyes, glasses, accessories).
Image 2 = the framing reference (a typical card of the set: cso).

    python tools/cards_revise.py                 # every id in REVISE
    python tools/cards_revise.py hr_jung cco     # some
      → ArtSource/Cards_v2/<id>.png (the previous one kept as ArtSource/Cards_v2/_prev/<id>.png)
Then: cards_v2_import.py <ids> · gen_standing_gemini.py <ids> · standing_v2.sh · sd2d_hf.py · 3D repaint.
SECURITY: the key is read from the env file by NAME; never printed or written.
"""
import base64, json, os, shutil, sys, time, urllib.request, urllib.error
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import gen_cards_gemini as g

CARDS = os.path.join(g.ROOT, "ArtSource", "Cards_v2")
REF = os.path.join(CARDS, "cso.png")

# id: (outfit change or "" to keep the outfit, what else to fix)
REVISE = {
    "design_lead": ("a cream off-shoulder knit sweater tucked into a fitted black high-waist pencil skirt, sheer black tights; keep her beret", "framing: much too close; pull back to match image 2"),
    "bd_lead": ("a charcoal-grey tailored suit: open fitted blazer, champagne silk blouse with the top two buttons undone, fitted pencil skirt", "framing: much too close; pull back to match image 2"),
    "ir_lead": ("a black tailored blazer over a white fitted blouse, a black pencil skirt with a small side slit, a thin gold necklace", "framing: much too close; pull back to match image 2"),
    "chro": ("", "framing: much too close — the camera must be clearly further back: her whole upper body and her skirt down to mid-thigh in frame, head small like image 2"),
    "cco": ("a blush-pink tailored blazer and matching fitted skirt over a white camisole, pearl earrings (realistic fabric, no floral print)", "framing: much too close; pull back to match image 2"),
    "pm_lead": ("", "framing: much too close; pull back to match image 2"),
    "intern_seo": ("", "framing: too close; pull back to match image 2"),
    "sales_kang": ("", "framing: too far and the pose different; match image 2's distance and relaxed standing pose (she may still hold her briefcase)"),
    "logistics_bae": ("", "pose: she is sitting on boxes; redraw her STANDING in a relaxed pose like image 2, same framing as image 2"),
    "parttime": ("", "framing: too far away; match image 2's distance and head size"),
    "chief_of_staff": ("", "her face is hidden behind glowing hair; show her face clearly and brightly lit, no glow over the face, framing like image 2"),
    "hr_jung": ("a soft beige tailored blazer, a light-blue blouse with the collar open, a fitted navy pencil skirt, sheer tights", "the green blazer goes (too many green blazers in the cast)"),
    "audit_han": ("a charcoal-grey pinstripe suit: fitted blazer, white shirt, slim tie, fitted skirt; keep her sunglasses and magnifier", "the green blazer goes (too many green blazers in the cast)"),
    "welfare": ("a dusty-rose soft cardigan over a white blouse, a fitted light-grey pencil skirt", "the teal blazer goes (too many green / teal blazers in the cast)"),
    "acct_lead": ("a navy tailored blazer, a white fitted blouse with the collar open, a grey pencil skirt, sheer black tights; keep her glasses", "the teal blazer goes (too many green / teal blazers in the cast)"),
    "trainer_seok": ("a camel knit vest over a crisp white shirt with a thin tie, a fitted dark-brown pencil skirt", "the bright orange suit goes (not a real office colour)"),
}

ASK = ("Image 1 is a character from our game; image 2 is another card from the same set, shown ONLY for its framing — take NOTHING else from image 2 (no props, no tablet, no clothes, no colours). Redraw the "
       "character of image 1 as a new card in the same Blue Archive official-illustration finish. KEEP her identity exactly: the "
       "same face, hairstyle and hair colour, eye colour, glasses and accessories, her job props. {outfit}FIX: {fix}. Framing: exactly "
       "like image 2 — the same camera distance and head size, a knee-up / thigh-up cowboy shot, the whole head in frame with space "
       "above the hair, centred, a relaxed standing pose, looking at the viewer, a soft bright blurred office background. She is a "
       "beautiful young adult woman in her twenties with Blue Archive proportions. Styling for her rank ({grade}): {glamour}. "
       "Office wear, tastefully alluring through fit and tailoring only: no nudity, no underwear, nothing see-through, not a school "
       "uniform. NO halo, no spreadsheet or grid behind her, no text, no frame, no watermark.")


def img(path):
    with open(path, "rb") as f: return {"inlineData": {"mimeType": "image/png", "data": base64.b64encode(f.read()).decode("ascii")}}


def generate(key, hid, spec, grade):
    outfit, fix = spec
    text = ASK.format(outfit=(f"CHANGE her outfit to: {outfit}. " if outfit else "Keep her outfit design and colours. "), fix=fix, grade=grade,
                      glamour=g.GLAMOUR.get(grade, g.GLAMOUR["D"]))
    src = os.path.join(CARDS, hid + ".png")
    body = json.dumps({"contents": [{"parts": [img(src), img(REF), {"text": text}]}],
                       "generationConfig": {"responseModalities": ["IMAGE"], "temperature": 0.6, "imageConfig": {"aspectRatio": "2:3"}}}).encode("utf-8")
    for model in g.MODELS:
        req = urllib.request.Request(f"https://generativelanguage.googleapis.com/v1beta/models/{model}:generateContent", data=body, method="POST",
                                     headers={"Content-Type": "application/json", "x-goog-api-key": key})
        try:
            with urllib.request.urlopen(req, timeout=300) as r: d = json.loads(r.read().decode("utf-8"))
        except urllib.error.HTTPError as e: print(f"  {hid} {model}: HTTP {e.code}"); continue
        for c in d.get("candidates", []):
            for p in c.get("content", {}).get("parts", []):
                inline = p.get("inlineData") or p.get("inline_data")
                if inline and inline.get("data"):
                    prev = os.path.join(CARDS, "_prev"); os.makedirs(prev, exist_ok=True)
                    if not os.path.exists(os.path.join(prev, hid + ".png")): shutil.copy(src, os.path.join(prev, hid + ".png"))
                    open(src, "wb").write(base64.b64decode(inline["data"])); print(f"  ok {hid} ({model})"); return True
    return False


if __name__ == "__main__":
    key = g.read_key()
    heroes = {h["id"]: h for h in json.load(open(os.path.join(g.DATA, "heroes.json"), encoding="utf-8"))["items"]}
    ids = sys.argv[1:] or list(REVISE)
    import concurrent.futures as cf
    with cf.ThreadPoolExecutor(4) as ex:
        list(ex.map(lambda h: generate(key, h, REVISE[h], heroes[h]["grade"]) or print("  FAILED", h), ids))
