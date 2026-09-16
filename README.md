# JobHunter

A personal, local Blazor Server app that pulls new postings from free sources every day, filters and scores them against my profile, writes an application kit for the jobs I pursue, prefills ATS forms so that I only press Submit myself, and tracks every application through the interview pipeline.

Everything stays on this machine: one process, one SQLite file under `data/`, no accounts, no hosting, nothing submitted automatically.

## Run it

The .NET 10 SDK is the only prerequisite.

```powershell
dotnet run --project src/JobHunter --launch-profile https
```

The app serves `https://localhost:5150` with the ASP.NET Core development certificate; run `dotnet dev-certs https --trust` once if the browser refuses it. The `https` launch profile sets `ASPNETCORE_ENVIRONMENT=Development`, which is required: under Production the static assets are not served, so the pages render but never become interactive.

The database is created on first start at `data/jobhunter.db`. A refresh runs by itself at startup when the last one is older than the auto-refresh window in Settings, twelve hours by default; the button in the header runs one on demand from any page.

Tests:

```powershell
dotnet test JobHunter.slnx
```

## The API key

Scoring and kit writing call the Anthropic API. Copy the tracked template and paste the key into the copy:

```powershell
Copy-Item src/JobHunter/appsettings.Local.Template.json src/JobHunter/appsettings.Local.json
```

Then set `Anthropic:ApiKey` in `src/JobHunter/appsettings.Local.json`. That file is gitignored and never belongs in a commit. The `ANTHROPIC_API_KEY` environment variable is read as a fallback when the file has no key. The file is reloaded while the app runs, so a pasted key takes effect without a restart, and the Settings page reports whether a key was detected without ever showing it.

The app is usable without a key through the export and import flow below.

## Playwright's Chromium

Prefill needs Playwright's Chromium once: after the first build run `./playwright.ps1 install chromium` from `src/JobHunter/bin/Debug/net10.0/` (about 150 MB, one time).

The first headed launch raises a Windows Firewall prompt for Google Chrome for Testing; answer it once and later runs are clean.

## Daily use

Refresh fetches every enabled source, dedupes, applies the deterministic rules and scores what passed. The Inbox then shows only the class A and B jobs waiting for a decision, with pay normalized to EUR per year.

- Pursue writes the application kit and opens the job page; Skip removes the job from the Inbox for good.
- The job page carries the score and its reasoning, the kit with a copy button per section, "Open & prefill" and "Mark applied".
- Prefill opens the posting in a headed browser and fills the standard fields and the resume. It never clicks Submit or Apply, and it leaves custom questions alone.
- Pipeline tracks each application through its statuses with notes, a contact and a next action; Stats answers how the search is going.
- All Jobs shows everything including the jobs the rules dropped, with the reason, so the rules stay auditable.

Contact details, the resume paths, compensation minimums and the enabled sources all live on the Settings page. Nothing personal is stored in the repository.

## Working without an API key

The same work can run through Claude Code instead of the API. The app exports JSONL, a repository-local skill writes JSONL back, and the app imports it. A skill never touches the database.

1. **Export.** The Inbox has an Export button. It writes three files into `data/exchange/`:
   - `to_score.jsonl` — every active job that passed the rules and carries no score, one per line.
   - `to_kit.jsonl` — every pursued job whose kit is missing or failed, one per line, with the score it was chosen on.
   - `resume.md` — a copy of the resume markdown from the path in Settings, for the kit skill to draw facts from.

   The export is not capped. After a first intake it can run to thousands of lines; cut the file down to the jobs worth scoring before running the skill.

2. **Run the skill.** Start Claude Code in the repository root and invoke the skill by name:
   - `score-jobs` reads `to_score.jsonl` with `profile/`, `prompts/score.md` and `prompts/schemas/score.schema.json`, and appends one object per job to `data/exchange/scored.jsonl`.
   - `write-kits` reads `to_kit.jsonl` and `resume.md` with `profile/`, `prompts/kit.md` and `prompts/schemas/kit.schema.json`, and appends one object per job to `data/exchange/kits.jsonl`.

   Both append and both skip job ids they have already written, so an interrupted run is safe to repeat.

3. **Import.** The Import button next to Export reads `scored.jsonl` and `kits.jsonl`. Every line goes through the same contract the API path is held to — the schema's required properties, its nullability and the 0 to 2 range of the scores — and is stored the same way, with the model recorded as `claude-code`. The class letter is computed in the app from the dimension scores, the facts and the settings, on this path exactly as on the API path. A line that does not hold up is refused on its own, listed with its file, its line number and the reason, and the other lines still import.

4. **Clear the result files.** Import does not delete `scored.jsonl` or `kits.jsonl`; a second Import would apply them again. Delete or move them once the results are in.

## Cost guard

Scoring is the only part of a refresh that spends money, and two settings bound it.

- **Max scores per run** (`MaxScoresPerRun`, default 300) caps how many jobs one Refresh sends to the model. Everything above the cap stays unscored and is picked up by the next run, so lowering it slows the intake down rather than losing jobs.
- **First-run window (days)** (`FirstRunWindowDays`, default 21) is how far back a posting may have been published to be taken in at all. It is the upstream control: a narrower window means fewer jobs reach scoring in the first place.

The score model and the kit model are settings too, and a cheaper score model reading the same rubric is the third way to bring the bill down. A change to any of these takes effect on the next refresh, with no restart.

## Layout

| Path | What is in it |
|---|---|
| `src/JobHunter/` | The app: `Domain/`, `Data/`, `Sources/`, `Pipeline/`, `Llm/`, `Prefill/`, `Components/Pages/` |
| `tests/JobHunter.Tests/` | Unit tests over the rules, the parsers, the scoring and the exchange round trip |
| `profile/` | Positioning, voice rules, the scoring rubric and the question bank, shared by both model paths |
| `prompts/` | The scoring and kit instructions and their JSON schemas, shared by both model paths |
| `.claude/skills/` | The `score-jobs` and `write-kits` skills |
| `data/` | The database, cached source downloads, the exchange folder and the browser profile. Gitignored |
