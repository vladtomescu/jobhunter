# Study cards instructions

These rules turn the prep into a deck of study cards for the phone. The prep stays the reference. The deck is what I study.

## Why cards

- Self-testing and spacing work. Rereading does not: it only feels like learning.
- Answering in my head counts, when the answer is revealed right after and I grade myself honestly.
- Multiple choice works only when the wrong options are plausible. Filler options turn it into recognition.
- One short spoken mock at a desk before the call covers the answers I must actually say.

## The file

`<Company>_<Round>_cards.json`, in the round folder. It is the source of truth for the player page and the Anki package.

```json
{
  "meta": {
    "id": "acme-tech-2026-11-10",
    "title": "Acme tech drill",
    "dateline": "Tue 10 Nov, 19:00 · think the answer before you tap",
    "company": "Acme",
    "round": "Tech"
  },
  "sections": [
    { "id": "lines", "name": "Red lines & traps" }
  ],
  "cards": [
    {
      "id": "lines-01",
      "section": "lines",
      "type": "mc",
      "scope": "company",
      "q": "They ask: \"Have you run Kubernetes in production?\"",
      "options": ["<correct, always first>", "<distractor>", "<distractor>", "<distractor>"],
      "explain": "<one sentence>"
    },
    {
      "id": "you-07",
      "section": "you",
      "type": "recall",
      "scope": "company",
      "q": "\"Have you migrated apps between servers?\"",
      "a": "Point one · Point two · Point three"
    }
  ]
}
```

- `meta.id`: lowercase letters, digits and hyphens, as `<company>-<round>-<YYYY-MM-DD>`. It keys the saved progress and the Anki company decks.
- `meta.dateline`: the date and local time of the call, then "think the answer before you tap".
- `type`: a `recall` card needs `a`. An `mc` card needs 3 or 4 `options`, the correct one first, and an `explain`. The player and the Anki builder shuffle the options.
- `scope`: `core` for general technical knowledge I can reuse in other interviews. `company` for this company, my own stories and phrasing, the red lines, my questions and the checklist.
- `id`: `<section>-<NN>`. When a deck for the same round is written again, every unchanged card keeps its id, because the progress is stored per id.
- Text: `" · "` (space, middle dot, space) splits an answer into points. Backticks mark code. No other markdown and no HTML.

The default sections. Adapt the names and drop an empty section:

| id | name | scope |
|---|---|---|
| `lines` | Red lines & traps | company |
| `you` | Your answers | company |
| `scen` | Scenarios | company |
| `live` | Live task | core |
| one per technical area, such as `aws`, `win`, `sec` | the area's name | core |
| `ask` | Your questions & checklist | company |

## From the prep to cards

| In the prep | Card |
|---|---|
| Each **Never:** line and each "Keep straight" item | `mc` in `lines`. The correct option is my safe phrasing. The dangerous wording is one of the distractors. |
| Each **Your answer:** | `recall` in `you`. `q` is the question as an interviewer asks it. `a` holds 3 to 6 key points, plus the Never line when the block has one. Never the whole scripted answer. |
| Each **If they ask …** follow-up | its own `recall` card |
| The Part 2 behavioural questions | `recall` in `you`, key points only |
| A scenario with a clear first step | `mc` "what do you do first?" in `scen`, plus a `recall` card for the whole sequence when the scenario has one |
| The **What it is:** facts | small `recall` cards, or `mc` where two things are easy to confuse |
| The live task | `recall` with the code outline as points, `mc` for the traps, and one card on the opening question about language, editor and tools |
| My questions, the checklist, who is on the call, the job in one breath | `recall` in `ask` |
| Abbreviations | no card, unless the abbreviation is the point of the question |

## Distractors

- Each one tempts a tired candidate. Take them from real mistakes: overselling, the wrong order of steps, a neighbouring concept, the wrong number, the tempting shortcut.
- They share the grammar, length and tone of the correct option. The correct option is not the longest in most cards.
- No "all of the above", no "none of the above", no jokes and no absurd options.
- `explain`: one sentence on why the answer is right, or what the tempting option really means.

## Facts

- Every card traces to the prep. Add no title, metric, scope, technology or experience the prep does not state.
- Keep the honest-gap wording exactly. Never soften a gap into exposure.
- Keep the prep's warnings, such as "Never give an uptime figure".
- No pay figure on any card.

## Size

- One idea per card. Split a card that needs two.
- `q`: 25 words or fewer where possible. `a`: 50 words or fewer. The builder warns above 60.
- I can grade a recall card in about 5 seconds, point by point against what I thought.
- A core card's `q` names no company, so a later interview shows the same card again.
- 80 to 130 cards per round. To cut, drop from the end of this order: red lines, honest-gap answers, scenarios, my stories, the live task, facts, my questions and the checklist.

## Examples

A bad recall answer is a script to learn by heart:

> "It was a mix. We have a system administrator, and I won't claim I did everything. About 85% of our servers are Windows VMs with IIS…"

A good one holds points I can speak from:

> "A mix. We have a sysadmin, I won't claim everything · ~85% Windows VMs with IIS · PowerShell scripts → Windows service → rewritten on .NET Core"

A bad multiple choice has filler distractors:

> "503 after a move means: the pool stopped / the moon / a typo / nothing"

A good one has competitive distractors:

> "503 right after a move usually means…": "The app pool stopped, often through rapid-fail protection." / "web.config needs a missing module." / "The certificate binding is missing." / "DNS wasn't switched." Explain: "500.19 = missing module in web.config."

## Quality gate

Before the deck is built:

1. `build_deck.py` passes: ids, sections, types and option counts.
2. Length bias: when the correct option is the longest in more than half of the `mc` cards, rewrite options.
3. Distractors: in each `mc` card, at least two distractors would tempt a tired candidate.
4. Trace: every card matches the prep. Remove anything the prep does not support.
5. Coverage: every **Never:** line and every "Keep straight" item has an `mc` card, and every **Your answer:** has a `recall` card.
6. Report the card count per section.
