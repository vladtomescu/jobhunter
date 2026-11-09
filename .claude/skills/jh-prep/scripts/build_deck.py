#!/usr/bin/env python3
"""Validate cards.json and build a self-contained deck.html from player_template.html.
Usage: python build_deck.py cards.json player_template.html deck.html
No third-party dependencies."""
import json, sys, re

def fail(errors):
    print("Deck validation failed:")
    for e in errors:
        print("  -", e)
    sys.exit(1)

def validate(deck):
    errors, warnings = [], []
    meta = deck.get("meta", {})
    if not re.fullmatch(r"[a-z0-9-]+", meta.get("id", "")):
        errors.append("meta.id must be lowercase letters, digits and hyphens")
    for k in ("title", "dateline"):
        if not meta.get(k):
            errors.append(f"meta.{k} is missing")
    sections = {s["id"] for s in deck.get("sections", [])}
    if not sections:
        errors.append("no sections")
    seen = set()
    for i, c in enumerate(deck.get("cards", [])):
        where = f"card {i} ({c.get('id', '?')})"
        cid = c.get("id", "")
        if not cid or cid in seen:
            errors.append(f"{where}: id missing or duplicate")
        seen.add(cid)
        if c.get("section") not in sections:
            errors.append(f"{where}: unknown section {c.get('section')}")
        if c.get("scope") not in ("core", "company"):
            errors.append(f"{where}: scope must be core or company")
        if not c.get("q"):
            errors.append(f"{where}: q is empty")
        t = c.get("type")
        if t == "recall":
            a = c.get("a", "")
            if not a:
                errors.append(f"{where}: recall card needs a")
            elif len(a.split()) > 60:
                warnings.append(f"{where}: answer has {len(a.split())} words (aim for 50 or fewer)")
        elif t == "mc":
            o = c.get("options", [])
            if not 3 <= len(o) <= 4:
                errors.append(f"{where}: mc needs 3 or 4 options, first one correct")
            if len(set(o)) != len(o):
                errors.append(f"{where}: duplicate options")
        else:
            errors.append(f"{where}: type must be recall or mc")
    if errors:
        fail(errors)
    for w in warnings:
        print("warning:", w)
    counts = {}
    for c in deck["cards"]:
        counts[c["section"]] = counts.get(c["section"], 0) + 1
    print(f"{len(deck['cards'])} cards:", ", ".join(f"{k} {v}" for k, v in counts.items()))

def main():
    src, template, out = sys.argv[1:4]
    deck = json.load(open(src, encoding="utf-8"))
    validate(deck)
    payload = json.dumps(deck, ensure_ascii=False).replace("</", "<\\/")
    html = open(template, encoding="utf-8").read()
    marker = "/*__DECK_JSON__*/null"
    if marker not in html:
        fail(["template is missing the /*__DECK_JSON__*/null marker"])
    open(out, "w", encoding="utf-8").write(html.replace(marker, payload))
    print("wrote", out)

if __name__ == "__main__":
    main()
