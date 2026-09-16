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

`score` holds the scores, facts and blocking unknowns from the scoring pass. `class` is the letter the app computed and `flags` are its prefilter labels; both travel as context for what to emphasise, and no rule here depends on either. `language_hint` is the app's guess at the posting language; the posting text decides.

## Output

`data/exchange/kits.jsonl`, one JSON object per line, each one matching `prompts/schemas/kit.schema.json`: a single line of compact JSON, no wrapping array, no markdown fence. Every line break inside a text field is escaped, so one kit stays on one line.

Write it as UTF-8 and end every object, the last one included, with a newline. Appending to a file whose last line has no newline fuses two objects onto one line and the import rejects both.

## How to run

1. Read `data/exchange/kits.jsonl` if it exists and collect the `job_id` values already in it.
2. Read `data/exchange/to_kit.jsonl` and skip every line whose `job_id` is already written. Re-runs must never produce a duplicate.
3. Work in batches of 10 remaining lines. Kits are slower than scores, so treat each one as its own piece of writing rather than a fill-in of the previous kit.
4. Check each object before writing it, mechanically rather than by eye; a throwaway validator is worth writing. The schema settles the properties and their types: every required property present, no extra properties, `language` set to `en`. The lengths live in `prompts/kit.md` rather than in the schema and hold just as firmly: exactly three `fit_summary` bullets, the six `ats_answers` in their order, 3 to 5 `call_questions`.
5. Check each kit against the rules in `prompts/kit.md` before writing it. A kit fails when it contains any of these, and it is rewritten rather than written out:
   - a currency amount, a start date or a notice period on a line with no [CONFIRM] marker,
   - the word "agentic" in front of "harness",
   - an exclamation mark,
   - two em dashes on the same line,
   - a "not just X, but Y" construction,
   - one of my own contact details: my name, an email address, a phone number, a postal address, or an address beginning `http://`, `https://` or `www.`, none of which the [CONFIRM] marker excuses. The company's name, the country I am authorized to work in, and the mandated answer `LinkedIn profile URL [CONFIRM]` are not contact details and stay.

   That list is the floor, not the standard. The rules in `prompts/kit.md` that no check can catch, above all "never invent experience, employers, tools or dates", decide whether a kit is any good; six green checks are not permission to write one out.
6. Append the batch to `data/exchange/kits.jsonl`, then move to the next batch.
7. When a single kit cannot be written, skip that line and note the job id in your closing report. Never write a partial or invented kit. A kit cannot be written when its line is not valid JSON, when `job_id` is missing, or when `description` is empty or holds no posting.

## This skill writes nothing else

- The only file it writes inside this repository is `data/exchange/kits.jsonl`, and only by appending. A validator or any other scratch file goes in a temporary folder outside the repository.
- No database access, no other files, no edits to `profile/`, `prompts/` or any source file, no git commands.
- Nothing is submitted anywhere. The kits are text for me to paste.

## Finish

Report three counts: the lines in `to_kit.jsonl`, the kits appended in this run, and the lines left without a kit, split between the ones already present in `kits.jsonl` and the ones that could not be written, with the job ids of the second group. Then tell me to run Import in the app.
