---
name: score-jobs
description: Score exported JobHunter postings when no API key is available. Reads data/exchange/to_score.jsonl together with the profile and the rubric (from the data root, else the shipped examples) and the scoring prompt in this repo, and appends one schema-valid JSON object per job to data/exchange/scored.jsonl. Use when the app shows unscored jobs and no Anthropic key is configured (appsettings.Local.json or ANTHROPIC_API_KEY).
---

# score-jobs

The backup scoring path for JobHunter. The app exports the jobs, this skill scores them, the app imports the results and computes the class. Run it from the repo root.

## Read first

1. `profile.md`: who I am, positioning, constraints, voice.
2. `rubric.md`: the seven dimensions and their 0, 1 and 2 anchors.
3. `prompts/score.md`: the scoring instructions, the output rules and the shape of a returned object.
4. `prompts/schemas/score.schema.json`: the schema every output line must satisfy.

Those four files are the whole instruction set. Follow them exactly; this file only describes the mechanics.

The two profile files belong to the user and are looked up one by one, the same way the app looks them up: first `<data root>/profile/<name>.md`, and when that file does not exist, the example `profile/<name>.example.md` in this repository. The data root is the folder that holds `exchange/`: `data/` in this repository unless the app runs with `JobHunter:DataRoot` pointing elsewhere. Say in the closing report which copy of each file you read.

## Input

`data/exchange/to_score.jsonl`, one job per line:

```json
{"job_id":"","title":"","company":"","location":"","remote_hint":"","employment_hint":"","comp_text":"","posted_at":"","flags":[],"description":""}
```

`description` is plain text and may be truncated. `flags` are the app's own prefilter flags (H1, H2, H3, H4, HomeCountry, CU, WA, StackMatch, HighPay); they are context, not scores, and no score has to account for them.

`data/exchange/resume.md` sits in the same folder. It belongs to the kit skill; scoring never reads it.

## Output

`data/exchange/scored.jsonl`, one JSON object per line, each one matching `prompts/schemas/score.schema.json`: a single line of compact JSON, no trailing commas, no wrapping array, no markdown fence.

Write it as UTF-8 and end every object, the last one included, with a newline. Appending to a file whose last line has no newline fuses two objects onto one line and the import rejects both.

## How to run

1. Read `data/exchange/scored.jsonl` if it exists and collect the `job_id` values already in it.
2. Read `data/exchange/to_score.jsonl` and skip every line whose `job_id` is already scored. Re-runs must never produce a duplicate.
3. Work in batches of 10 remaining lines. For each line in the batch, score the posting against the rubric and build the object described in `prompts/score.md`.
4. Validate each object against `prompts/schemas/score.schema.json` before writing it: every required property present, no extra properties, the seven scores integers 0, 1 or 2, enum values exactly as the schema spells them, nullable fields either the stated type or null. Check it mechanically rather than by eye; a throwaway validator is worth writing.
5. Append the batch to `data/exchange/scored.jsonl`, then move to the next batch. Appending per batch keeps the work done so far if the session is interrupted.
6. When a single job cannot be scored, skip that line and note the job id in your closing report. Never write a partial or invented object. A job cannot be scored when its line is not valid JSON, when `job_id` is missing, or when `description` is empty or holds no posting; a thin posting is still scored, on the little evidence it gives.

## This skill writes nothing else

- The only file it writes inside this repository is `data/exchange/scored.jsonl`, and only by appending. A validator or any other scratch file goes in a temporary folder outside the repository.
- No database access, no other files, no edits to either profile folder, `prompts/` or any source file, no git commands.
- No class letter anywhere in the output. The app computes the class from the scores, the facts and settings that are not in this repo.

## Finish

Report three counts: the lines in `to_score.jsonl`, the objects appended in this run, and the lines left unscored, split between the ones already present in `scored.jsonl` and the ones that could not be scored, with the job ids of the second group. Then tell me to run Import in the app.
