---
name: score-jobs
description: Score exported JobHunter postings when no API key is available. Reads data/exchange/to_score.jsonl together with the profile, the rubric and the scoring prompt in this repo, and appends one schema-valid JSON object per job to data/exchange/scored.jsonl. Use when the app shows unscored jobs and no Anthropic key is configured (appsettings.Local.json or ANTHROPIC_API_KEY).
---

# score-jobs

The backup scoring path for JobHunter. The app exports the jobs, this skill scores them, the app imports the results and computes the class. Run it from the repo root.

## Read first

1. `profile/profile.md`: who I am, positioning, constraints, voice.
2. `profile/rubric.md`: the seven dimensions and their 0, 1 and 2 anchors.
3. `prompts/score.md`: the scoring instructions, the output rules and the shape of a returned object.
4. `prompts/schemas/score.schema.json`: the schema every output line must satisfy.

Those four files are the whole instruction set. Follow them exactly; this file only describes the mechanics.

## Input

`data/exchange/to_score.jsonl`, one job per line:

```json
{"job_id":"","title":"","company":"","location":"","remote_hint":"","employment_hint":"","comp_text":"","posted_at":"","flags":[],"description":""}
```

`description` is plain text and may be truncated. `flags` are the app's own prefilter flags (H1, H2, H3, H4, CU); they are context, not scores.

## Output

`data/exchange/scored.jsonl`, one JSON object per line, each one matching `prompts/schemas/score.schema.json`: a single line of compact JSON, no trailing commas, no wrapping array, no markdown fence.

## How to run

1. Read `data/exchange/scored.jsonl` if it exists and collect the `job_id` values already in it.
2. Read `data/exchange/to_score.jsonl` and skip every line whose `job_id` is already scored. Re-runs must never produce a duplicate.
3. Work in batches of 10 remaining lines. For each line in the batch, score the posting against the rubric and build the object described in `prompts/score.md`.
4. Validate each object against `prompts/schemas/score.schema.json` before writing it: every required property present, no extra properties, the seven scores integers 0, 1 or 2, enum values exactly as the schema spells them, nullable fields either the stated type or null.
5. Append the batch to `data/exchange/scored.jsonl`, then move to the next batch. Appending per batch keeps the work done so far if the session is interrupted.
6. When a single job cannot be scored, skip that line and note the job id in your closing report. Never write a partial or invented object.

## This skill writes nothing else

- The only file it writes is `data/exchange/scored.jsonl`, and only by appending.
- No database access, no other files, no edits to `profile/`, `prompts/` or any source file, no git commands.
- No class letter anywhere in the output. The app computes the class from the scores, the facts and settings that are not in this repo.

## Finish

Report three numbers: lines read, objects appended, jobs skipped with their ids. Then tell me to run Import in the app.
