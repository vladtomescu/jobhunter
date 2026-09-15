---
name: write-kits
description: Write JobHunter application kits when no API key is available. Reads data/exchange/to_kit.jsonl and data/exchange/resume.md together with the profile, the question bank and the kit prompt in this repo, and appends one schema-valid JSON object per job to data/exchange/kits.jsonl. Use when jobs are marked for pursuit and no Anthropic key is configured (appsettings.Local.json or ANTHROPIC_API_KEY).
---

# write-kits

The backup kit path for JobHunter. The app exports the pursued jobs and my resume, this skill writes the kits, the app imports them. Run it from the repo root.

## Read first

1. `profile/profile.md`: who I am, positioning, constraints, voice.
2. `profile/questions.md`: the question bank for the first call.
3. `prompts/kit.md`: the kit instructions, the standard ATS answers, the voice rules and the shape of a returned object.
4. `prompts/schemas/kit.schema.json`: the schema every output line must satisfy.
5. `data/exchange/resume.md`: my resume, exported by the app. Facts may come from it; its contact details never appear in generated text.

Those files are the whole instruction set. Follow them exactly; this file only describes the mechanics.

## Input

`data/exchange/to_kit.jsonl`, one job per line:

```json
{"job_id":"","title":"","company":"","apply_url":"","description":"","score":{},"class":"","flags":[],"language_hint":""}
```

`score` holds the scores, facts and blocking unknowns from the scoring pass. `class` is the letter the app computed. `language_hint` is the app's guess at the posting language; the posting text decides.

## Output

`data/exchange/kits.jsonl`, one JSON object per line, each one matching `prompts/schemas/kit.schema.json`: a single line of compact JSON, no wrapping array, no markdown fence.

## How to run

1. Read `data/exchange/kits.jsonl` if it exists and collect the `job_id` values already in it.
2. Read `data/exchange/to_kit.jsonl` and skip every line whose `job_id` is already written. Re-runs must never produce a duplicate.
3. Work in batches of 10 remaining lines. Kits are slower than scores, so treat each one as its own piece of writing rather than a fill-in of the previous kit.
4. Validate each object against `prompts/schemas/kit.schema.json` before writing it: every required property present, no extra properties, exactly three `fit_summary` bullets, 3 to 5 `call_questions`, `language` set to `en`.
5. Check each kit against the rules in `prompts/kit.md` before writing it. A kit fails when it contains any of these, and it is rewritten rather than written out:
   - a currency amount, a start date or a notice period on a line with no [CONFIRM] marker,
   - the word "agentic" in front of "harness",
   - an exclamation mark,
   - a chain of em dashes,
   - a "not just X, but Y" construction,
   - a contact detail: a name, an email address, a phone number, an address or a profile link.
6. Append the batch to `data/exchange/kits.jsonl`, then move to the next batch.
7. When a single kit cannot be written, skip that line and note the job id in your closing report. Never write a partial or invented kit.

## This skill writes nothing else

- The only file it writes is `data/exchange/kits.jsonl`, and only by appending.
- No database access, no other files, no edits to `profile/`, `prompts/` or any source file, no git commands.
- Nothing is submitted anywhere. The kits are text for me to paste.

## Finish

Report three numbers: lines read, kits appended, jobs skipped with their ids. Then tell me to run Import in the app.
