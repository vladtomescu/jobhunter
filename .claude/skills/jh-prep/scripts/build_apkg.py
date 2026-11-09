#!/usr/bin/env python3
"""Build an Anki package (.apkg) from cards.json for AnkiDroid.
Usage: python build_apkg.py cards.json out.apkg
Needs: pip install genanki
Core cards go to "Interview::Core::<section>" and keep the same GUID across interviews
(GUID from the normalised question), so re-importing updates them instead of duplicating.
Company cards go to "Interview::<meta.id>::<section>"."""
import json, sys, re, random, html
import genanki

MODEL_ID = 1607392319  # fixed forever: changing it breaks updates on re-import
CSS = (".card{font-family:sans-serif;font-size:19px;text-align:left;line-height:1.45;"
       "color:#e4e1d8;background:#12151c}code{font-family:monospace;background:#252b36;"
       "padding:0 4px;border-radius:4px}li{margin:4px 0}.k{color:#e0a458;font-size:13px;"
       "text-transform:uppercase;letter-spacing:.06em}.ok{color:#6fbf8e;font-weight:600}")
MODEL = genanki.Model(MODEL_ID, "Interview Drill",
    fields=[{"name": "Front"}, {"name": "Back"}],
    templates=[{"name": "Card", "qfmt": "{{Front}}", "afmt": "{{Front}}<hr id=answer>{{Back}}"}],
    css=CSS)

def inline(s):
    return re.sub(r"`([^`]+)`", r"<code>\1</code>", html.escape(s, quote=False))

def points(s):
    parts = s.split(" · ")
    return inline(s) if len(parts) == 1 else "<ul>" + "".join(f"<li>{inline(p)}</li>" for p in parts) + "</ul>"

def deck_id(name):
    return 1 << 30 | (int.from_bytes(name.encode("utf-8"), "little") % (1 << 30))

def main():
    src, out = sys.argv[1:3]
    d = json.load(open(src, encoding="utf-8"))
    meta, names = d["meta"], {s["id"]: s["name"] for s in d["sections"]}
    decks = {}
    rnd = random.Random(meta["id"])
    for c in d["cards"]:
        sec = names[c["section"]]
        dname = f"Interview::Core::{sec}" if c["scope"] == "core" else f"Interview::{meta['id']}::{sec}"
        deck = decks.setdefault(dname, genanki.Deck(deck_id(dname), dname))
        if c["type"] == "mc":
            opts = list(enumerate(c["options"]))
            rnd.shuffle(opts)
            letters = "ABCD"
            front = "<div class=k>Pick one</div>" + inline(c["q"]) + "<br><br>" + "<br>".join(
                f"<b>{letters[i]}</b>. {inline(o)}" for i, (_, o) in enumerate(opts))
            right = next(i for i, (j, _) in enumerate(opts) if j == 0)
            back = f"<span class=ok>{letters[right]}</span>. {inline(c['options'][0])}"
            if c.get("explain"):
                back += "<br><br>" + inline(c["explain"])
        else:
            front = "<div class=k>Answer in your head</div>" + inline(c["q"])
            back = points(c["a"])
        if c["scope"] == "core":
            guid = genanki.guid_for("core", re.sub(r"\W+", " ", c["q"].lower()).strip())
        else:
            guid = genanki.guid_for(meta["id"], c["id"])
        deck.add_note(genanki.Note(model=MODEL, fields=[front, back], guid=guid,
                                   tags=[c["section"], c["scope"]]))
    genanki.Package(list(decks.values())).write_to_file(out)
    print("wrote", out, "with", len(d["cards"]), "notes in", len(decks), "decks")

if __name__ == "__main__":
    main()
