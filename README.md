# JobHunter

A personal, local Blazor Server app that pulls new postings from free sources every day, filters and scores them against your profile, writes an application kit for the jobs you pursue, prefills ATS forms so that you only press Submit yourself, and tracks every application through the interview pipeline.

Everything stays on this machine: one process, one SQLite file under `data/`, no accounts, no hosting, nothing submitted automatically.

## Run it

The .NET 10 SDK is the only prerequisite.

```powershell
dotnet run --project src/JobHunter --launch-profile https
```

The app serves `https://localhost:5160` with the ASP.NET Core development certificate; run `dotnet dev-certs https --trust` once if the browser refuses it. The `https` launch profile sets `ASPNETCORE_ENVIRONMENT=Development`, which is required for `dotnet run` from the build output: under Production the static assets are not served from `bin/`, so the pages render but never become interactive. The published image below runs Production and serves them from its own output, so this applies only to `dotnet run`.

The database is created on first start at `data/jobhunter.db`. A refresh runs by itself at startup when the last one is older than the auto-refresh window in Settings, twelve hours by default, and a window of 0 turns the startup refresh off; the Refresh button in the header runs one on demand from any page. A refresh, at startup or from the button, only brings jobs in and sends nothing to the model. Scoring runs only when you press Score, next to Refresh.

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

## Your profile

Scoring and kits are written against three Markdown files that describe you. They belong to you, not to the repository, and live in the `profile/` folder of the data root (`data/profile/` by default, or under `JobHunter:DataRoot` when that is set):

| File | What it holds |
|---|---|
| `profile.md` | Who you are: niche and target roles, stack, contract form, where and when you can work, the standard answers (work authorization, relocation, the languages you write kits in), your voice rules and the numbers a kit may cite |
| `rubric.md` | The seven scoring dimensions and their 0, 1 and 2 anchors, written for your niche, stack, hours and contract form |
| `questions.md` | The question bank for the first call |

The repository ships a fictional example of each as `profile/<name>.example.md`. To set up your own, copy the three examples into the data root, drop `.example` from the names, and edit them, keeping the headings and the seven dimension names:

```powershell
New-Item -ItemType Directory -Force data/profile
Copy-Item profile/profile.example.md data/profile/profile.md
Copy-Item profile/rubric.example.md data/profile/rubric.md
Copy-Item profile/questions.example.md data/profile/questions.md
```

Each file is looked up on its own: the copy in the data root wins, and a file missing there falls back to its example, so the app and the tests run on a fresh clone with no profile at all. The log says at startup which copy of each file is in use. The files are read once, so restart the app after editing them. `profile/*.md` other than the examples is gitignored, so a profile edited inside the repository by mistake does not reach a commit.

## Playwright's Chromium

Prefill needs Playwright's Chromium once: after the first build run `./playwright.ps1 install chromium` from `src/JobHunter/bin/Debug/net10.0/` (about 150 MB, one time).

The first headed launch raises a Windows Firewall prompt for Google Chrome for Testing; answer it once and later runs are clean.

## Run it in Docker

The app can also run as an always-on container instead of `dotnet run`. The image carries the app, `prompts/` and the three example profile files; it never carries your own profile, the API key or the database. Your profile goes in the `profile/` folder of the mounted data root.

```bash
docker build -t jobhunter .
docker run -d --name jobhunter --restart unless-stopped -p 5150:8080 \
  -v <data-root>:/home/app/jobhunter \
  -v <data-root>/keys:/home/app/.aspnet/DataProtection-Keys \
  -v <resume-folder>:/home/app/resume:ro \
  -v <repo>/src/JobHunter/appsettings.Local.json:/app/appsettings.Local.json:ro \
  -e JobHunter__DataRoot=/home/app/jobhunter \
  -e JobHunter__RepositoryRoot=/app \
  -e Prefill__BrowserEndpoint=http://host.docker.internal:9333 \
  jobhunter
```

- `JobHunter__DataRoot`: the database, your profile files, cached downloads, the exchange folder and the browser profile.
- `JobHunter__RepositoryRoot`: where `prompts/` and the example profile files live inside the image.
- The `keys` mount keeps the ASP.NET Core data-protection keys across rebuilds.
- In Settings, point the resume paths at `/home/app/resume/<file>`.
- The container listens on 8080 inside and is published on host port 5150 here; pick any host port.

To replace a running container, build the new image first, then `docker stop jobhunter` (a graceful stop lets SQLite checkpoint its write-ahead log), `docker rm jobhunter` and run again. Never copy a database file over `jobhunter.db` while a `jobhunter.db-wal` or `jobhunter.db-shm` from another run sits next to it: SQLite would replay that log onto the new file and tear it.

Because the container has no desktop, prefill drives a Chromium already running on the host over the Chrome DevTools Protocol instead of launching one. Start it once, headed, on its own profile and a free debug port:

```bash
chrome --remote-debugging-port=9333 --user-data-dir=<folder> --no-first-run --no-default-browser-check
```

`Prefill__BrowserEndpoint` points the container at it; the container reconnects on every prefill and never closes it.

## Daily use

Refresh fetches every enabled source, dedupes, applies the deterministic rules and retires the postings that are no longer listed or went stale. It does not score. Score, next to it, sends the jobs that passed the rules and carry no score to the model: the ones added by hand first, then the newest, up to the per-run cap. The button shows how many jobs are waiting and is disabled while a refresh or a score run is in progress; the two never run at the same time. The Inbox then shows only the class A and B jobs waiting for a decision, with pay normalized to the base currency from Settings, per year.

- Pursue creates the application; "Write the kit" on the job page writes the application kit when you want it. Skip removes the job from the Inbox for good.
- The job page carries the score and its reasoning, the kit with a copy button per section, "Open & prefill" and "Mark applied".
- Prefill opens the posting in a headed browser and fills the standard fields and the resume. It never clicks Submit or Apply, and it leaves custom questions alone.
- Pipeline tracks each application through its statuses with notes, a contact and a next action; Stats answers how the search is going.
- All Jobs shows everything including the jobs the rules dropped, with the reason, so the rules stay auditable.

Contact details, the resume paths, compensation minimums and the enabled sources all live on the Settings page, and your profile lives in the data root. Nothing personal is stored in the repository.

## Working without an API key

The same work can run through Claude Code instead of the API. Without a key the Score button stays disabled and the Inbox points to this path. The app exports JSONL, a repository-local skill writes JSONL back, and the app imports it. A skill never touches the database.

1. **Export.** The Inbox has an Export button. It writes three files into `data/exchange/`:
   - `to_score.jsonl` — every active job that passed the rules and carries no score, one per line.
   - `to_kit.jsonl` — every pursued job whose kit is missing or failed, one per line, with the score it was chosen on.
   - `resume.md` — a copy of the resume markdown from the path in Settings, for the kit skill to draw facts from.

   The export is not capped. After a first intake it can run to thousands of lines; cut the file down to the jobs worth scoring before running the skill.

2. **Run the skill.** Start Claude Code in the repository root and invoke the skill by name:
   - `score-jobs` reads `to_score.jsonl` with your profile and rubric, `prompts/score.md` and `prompts/schemas/score.schema.json`, and appends one object per job to `data/exchange/scored.jsonl`.
   - `write-kits` reads `to_kit.jsonl` and `resume.md` with your profile and question bank, `prompts/kit.md` and `prompts/schemas/kit.schema.json`, and appends one object per job to `data/exchange/kits.jsonl`.

   Both append and both skip job ids they have already written, so an interrupted run is safe to repeat.

3. **Import.** The Import button next to Export reads `scored.jsonl` and `kits.jsonl`. Every line goes through the same contract the API path is held to — the schema's required properties, its nullability, the 0 to 2 range of the scores, and the vocabulary the schema allows for fields such as the remote policy and the employment type, with any property the schema does not declare refused outright — and is stored the same way, with the model recorded as `claude-code`. The class letter is computed in the app from the dimension scores, the facts and the settings, on this path exactly as on the API path. A line that does not hold up is refused on its own, listed with its file, its line number and the reason, and the other lines still import.

4. **Import clears its input.** A line that imports is removed from `scored.jsonl` or `kits.jsonl`, so running Import again cannot apply it a second time. Once a file has nothing left to import, Import renames it with a timestamp instead of deleting it, so the result stays on disk if it is ever worth checking again. A line that was refused stays behind in the original file, under its own name, ready to fix and import again.

### Claude Code commands: `/jh:add` and `/jh:score`

Two commands in `.claude/commands/jh/` do the same scoring without the Export and Import buttons. They run on the Claude Code subscription, never on the API, and score with the `score-jobs` skill. They talk to the running app over HTTP, and the app imports through the same code as the Import button, so the class, the flags and the pay come from the app exactly as for any other job.

- `/jh:add <link> [more links]` reads each posting in your Chrome through the Claude in Chrome extension, scores it, adds it as a manual job and answers with the class, the total out of 14, the reasons that decided it, the blocking unknowns and the job's page. A link the app already holds is not added twice; the answer shows the stored class. When Chrome cannot show the posting (for example when you are logged out), the command asks you to paste the text.
- `/jh:score [max jobs]` scores every job still waiting for a score, in batches of about 10, one subagent per batch. Each batch is imported the moment it is scored, so an interrupted run keeps its progress. It ends with the count per class, the refused lines, and the A and B jobs with their links.

Two environment variables point the commands at the app, set where Claude Code runs:

- `JOBHUNTER_URL` — the address of the running app. Default `http://localhost:5150`.
- `JOBHUNTER_DATA_ROOT` — the data root whose `profile/` the skill reads, when it is not `data/` in the repository (for example the host folder a container mounts). Without the right profile the skill falls back to the example files and says so.

The endpoints, all local and without authentication like the rest of the app:

| Route | Body | Answer |
|---|---|---|
| `GET /exchange/to-score` | — | the to-score lines, as the Export button writes them |
| `POST /exchange/scored` | score lines (`prompts/schemas/score.schema.json`), one per line | `imported`, `refused`, and per line the `job_id` with its `class`, `total` and `flags`, or its `refusal` |
| `POST /exchange/new-job` | one new-job line (`prompts/schemas/new_job.schema.json`) | `201` with `job_id`, `class`, `total`, `flags` and `job_page`; `200` with `already_exists` when the link is already held; `400` with `refusal` |

## Cost guard

Of the two runs, only Score spends money; a refresh never calls the model. Two settings bound it.

- **Max scores per run** (`MaxScoresPerRun`, default 300) caps how many jobs one press of Score sends to the model. Everything above the cap stays unscored and waits for the next press, so lowering it slows scoring down rather than losing jobs.
- **First-run window (days)** (`FirstRunWindowDays`, default 21) is how far back a posting may have been published to be taken in at all. It is the upstream control: a narrower window means fewer jobs reach scoring in the first place.

The score model and the kit model are settings too, and a cheaper score model reading the same rubric is the third way to bring the bill down. A change takes effect with no restart: the window on the next refresh, the cap and the score model on the next score run, the kit model on the next kit written.

## Layout

| Path | What is in it |
|---|---|
| `src/JobHunter/` | The app: `Domain/`, `Data/`, `Sources/`, `Pipeline/`, `Llm/`, `Prefill/`, `Components/Pages/` |
| `tests/JobHunter.Tests/` | Unit tests over the rules, the parsers, the scoring and the exchange round trip |
| `profile/` | The three example profile files; your own copies live in the data root (see Your profile) and are shared by both model paths |
| `prompts/` | The scoring and kit instructions and their JSON schemas, shared by both model paths |
| `.claude/skills/` | The `score-jobs` and `write-kits` skills |
| `.claude/commands/jh/` | The `/jh:add` and `/jh:score` commands |
| `data/` | The database, your profile files, cached source downloads, the exchange folder and the browser profile. Gitignored |
