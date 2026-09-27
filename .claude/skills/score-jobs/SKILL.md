---
name: score-jobs
description: Score JobHunter postings on the Claude subscription, without an API key. Reads the profile and the rubric (from the data root, else the shipped examples) and the scoring prompt in this repo, and turns to-score lines into schema-valid score objects. The lines come from data/exchange/to_score.jsonl (the objects are appended to data/exchange/scored.jsonl) or from a caller such as the /jh:score and /jh:add commands (the objects go back to the caller). Use when the app shows unscored jobs and no Anthropic key is configured (appsettings.Local.json or ANTHROPIC_API_KEY).
---

# score-jobs

The backup scoring path for JobHunter. The app hands out the jobs, this skill scores them, the app imports the results and computes the class. Run it from the repo root.

There are two ways in, and they differ only in where the lines come from and where the objects go:

- **File mode.** The Export button wrote `data/exchange/to_score.jsonl`; this skill appends to `data/exchange/scored.jsonl`, and the Import button reads it. See "How to run in file mode".
- **Caller mode.** A command such as `/jh:score` or `/jh:add` hands this skill to-score lines it fetched from the app or built from a posting, and takes the score objects back to post to the app itself. See "When a command calls this skill".

Everything under "Read first", "Input", "Scoring one job" and "This skill writes nothing else" holds in both modes.

## Read first

1. `profile.md`: who I am, positioning, constraints, voice.
2. `rubric.md`: the seven dimensions and their 0, 1 and 2 anchors.
3. `prompts/score.md`: the scoring instructions, the output rules and the shape of a returned object.
4. `prompts/schemas/score.schema.json`: the schema every output object must satisfy.

Those four files are the whole instruction set. Follow them exactly; this file only describes the mechanics.

The two profile files belong to the user and are looked up one by one, the same way the app looks them up: first `<data root>/profile/<name>.md`, and when that file does not exist, the example `profile/<name>.example.md` in this repository. The data root is the folder that holds `exchange/` and `profile/`: the folder named by the `JOBHUNTER_DATA_ROOT` environment variable when it is set (the app's `JobHunter:DataRoot`, for example the host folder a container mounts), otherwise `data/` in this repository. Say in the closing report which copy of each file you read, and say it plainly when the example copy was used, because a score against the example profile is a score for somebody else.

## Input

One job per line, the shape the Export button and the app's to-score endpoint both produce:

```json
{"job_id":"","title":"","company":"","location":"","remote_hint":"","employment_hint":"","comp_text":"","posted_at":"","flags":[],"description":""}
```

`description` is plain text and may be truncated. `flags` are the app's own prefilter flags (H1, H2, H3, H4, HomeCity, CU, WA, StackMatch, HighPay); they are context, not scores, and no score has to account for them.

A caller that scores a posting the app does not hold yet builds this line itself, with `job_id` the empty string and every field the posting does not give an empty string or an empty array.

`data/exchange/resume.md` sits in the same folder as the file input. It belongs to the kit skill; scoring never reads it.

## Scoring one job

1. Score the posting against the rubric and build the object described in `prompts/score.md`. `job_id` is copied from the input unchanged, the empty string included.
2. Validate each object against `prompts/schemas/score.schema.json` before handing it on: every required property present, no extra properties, the seven scores integers 0, 1 or 2, enum values exactly as the schema spells them, nullable fields either the stated type or null. Check it mechanically rather than by eye; a throwaway validator is worth writing.
3. Each object is a single line of compact JSON: no trailing commas, no wrapping array, no markdown fence.
4. When a single job cannot be scored, skip it and note its job id for the closing report. Never produce a partial or invented object. A job cannot be scored when its line is not valid JSON, when `job_id` is missing, or when `description` is empty or holds no posting; a thin posting is still scored, on the little evidence it gives.
5. No class letter anywhere in the output. The app computes the class from the scores, the facts and settings that are not in this repo.

## How to run in file mode

1. Read `data/exchange/scored.jsonl` if it exists and collect the `job_id` values already in it.
2. Read `data/exchange/to_score.jsonl` and skip every line whose `job_id` is already scored. Re-runs must never produce a duplicate.
3. Work in batches of 10 remaining lines, scoring each one as "Scoring one job" describes.
4. Append the batch to `data/exchange/scored.jsonl` as UTF-8, ending every object, the last one included, with a newline, then move to the next batch. Appending to a file whose last line has no newline fuses two objects onto one line and the import rejects both. Appending per batch keeps the work done so far if the session is interrupted.
5. Finish by reporting three counts: the lines in `to_score.jsonl`, the objects appended in this run, and the lines left unscored, split between the ones already present in `scored.jsonl` and the ones that could not be scored, with the job ids of the second group. Then tell me to run Import in the app.

## When a command calls this skill

The command owns the input and the output; this skill owns the scoring.

- The command says where the to-score lines are (a file it saved in a temporary folder, or a line it built from a posting) and where the score objects go (a file in the same temporary folder).
- Score every line as "Scoring one job" describes and write the objects there, one per line, each ending with a newline.
- The command posts the objects to the app, which imports them through the same code as the Import button and answers with the class. This skill never talks to the app and never states a class.

## This skill writes nothing else

- In file mode the only file it writes inside this repository is `data/exchange/scored.jsonl`, and only by appending. In caller mode it writes nothing inside this repository. A validator or any other scratch file goes in a temporary folder outside the repository.
- No database access, no other files, no edits to either profile folder, `prompts/` or any source file, no git commands.
