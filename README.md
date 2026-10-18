# JobHunter

A personal, local Blazor Server app that pulls new postings from free sources every day, filters and scores them against your profile, writes an application kit and, on request, a cover letter for the jobs you pursue, prefills ATS forms so that you only press Submit yourself, and tracks every application through the interview pipeline.

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

Scoring, kit writing and the Write cover letter button call the Anthropic API. Copy the tracked template and paste the key into the copy:

```powershell
Copy-Item src/JobHunter/appsettings.Local.Template.json src/JobHunter/appsettings.Local.json
```

Then set `Anthropic:ApiKey` in `src/JobHunter/appsettings.Local.json`. That file is gitignored and never belongs in a commit. The `ANTHROPIC_API_KEY` environment variable is read as a fallback when the file has no key. The file is reloaded while the app runs, so a pasted key takes effect without a restart, and the Settings page reports whether a key was detected without ever showing it.

The app is usable without a key through the export and import flow below.

## Your profile

Scoring, kits and cover letters are written against four Markdown files that describe you. They belong to you, not to the repository, and live in the `profile/` folder of the data root (`data/profile/` by default, or under `JobHunter:DataRoot` when that is set):

| File | What it holds |
|---|---|
| `profile.md` | Who you are: niche and target roles, stack, contract form, where and when you can work, the standard answers (work authorization, relocation, the languages you write kits in), your voice rules and the numbers a kit may cite |
| `rubric.md` | The seven scoring dimensions and their 0, 1 and 2 anchors, written for your niche, stack, hours and contract form |
| `questions.md` | The question bank for the first call |
| `cover-letter.md` | The cover-letter template: the `## Header` lines the app prints at the top of every letter, then the length, shape and tone of a letter and the base paragraphs it is built from, with `[Tailor: ...]` slots the model fills for each posting |

The repository ships a fictional example of each as `profile/<name>.example.md`. To set up your own, copy the four examples into the data root, drop `.example` from the names, and edit them, keeping the headings and the seven dimension names:

```powershell
New-Item -ItemType Directory -Force data/profile
Copy-Item profile/profile.example.md data/profile/profile.md
Copy-Item profile/rubric.example.md data/profile/rubric.md
Copy-Item profile/questions.example.md data/profile/questions.md
Copy-Item profile/cover-letter.example.md data/profile/cover-letter.md
```

The Header section of `cover-letter.md` belongs to the app and never reaches the model. Its lines may use `{name}`, `{email}`, `{phone}`, `{location}` and `{linkedin}`, filled from the contact details in Settings whenever a letter is shown or downloaded. A line is split into segments on ` · `; a segment whose placeholders are all empty is left out, a line with nothing left is left out, and `**bold**` is honoured. The rest of the file is guidance the model follows; for a cover letter its length, structure and tone rules take precedence over the short-message voice in `profile.md`, while the profile's other rules still bind.

Each file is looked up on its own: the copy in the data root wins, and a file missing there falls back to its example, so the app and the tests run on a fresh clone with no profile at all. The log says at startup which copy of each file is in use. The files are read once, so restart the app after editing them. `profile/*.md` other than the examples is gitignored, so a profile edited inside the repository by mistake does not reach a commit.

Six more files, `interview.md`, `glossary.md`, `cloud.md`, `system-design.md`, `project-example.md` and `ai-workflow.md`, hold what you say in interviews. The app never reads them; only the `/jh:prep` and `/jh:debrief` commands do (see Interview prep).

## Playwright's Chromium

Prefill needs Playwright's Chromium once: after the first build run `./playwright.ps1 install chromium` from `src/JobHunter/bin/Debug/net10.0/` (about 150 MB, one time).

The first headed launch raises a Windows Firewall prompt for Google Chrome for Testing; answer it once and later runs are clean.

## Run it in Docker

The app can also run as an always-on container instead of `dotnet run`. The image carries the app, `prompts/` and the example profile files; it never carries your own profile, the API key or the database. Your profile goes in the `profile/` folder of the mounted data root.

```bash
docker build -t jobhunter .
docker run -d --name jobhunter --restart unless-stopped -p 5150:8080 \
  -v <data-root>:/home/app/jobhunter \
  -v jobhunter-db:/home/app/db \
  -v <data-root>/keys:/home/app/.aspnet/DataProtection-Keys \
  -v <resume-folder>:/home/app/resume:ro \
  -v <repo>/src/JobHunter/appsettings.Local.json:/app/appsettings.Local.json:ro \
  -e JobHunter__DataRoot=/home/app/jobhunter \
  -e JobHunter__DatabaseFolder=/home/app/db \
  -e JobHunter__RepositoryRoot=/app \
  -e Prefill__BrowserEndpoint=http://host.docker.internal:9333 \
  jobhunter
```

- `JobHunter__DataRoot`: your profile files, cached downloads, the exchange folder, the browser profile and the daily database copies, plus the database itself when `JobHunter__DatabaseFolder` is not set.
- `JobHunter__DatabaseFolder`: the folder that holds `jobhunter.db`, here the `jobhunter-db` named volume. On Docker Desktop for Windows a SQLite file on a bind-mounted host folder is very slow to read, so the database lives on a named volume, which also outlives `docker rm`.
- `JobHunter__RepositoryRoot`: where `prompts/` and the example profile files live inside the image.
- The `keys` mount keeps the ASP.NET Core data-protection keys across rebuilds.
- In Settings, point the resume paths at `/home/app/resume/<file>`.
- The container listens on 8080 inside and is published on host port 5150 here; pick any host port.

To replace a running container, build the new image first, then `docker stop jobhunter` (a graceful stop lets SQLite checkpoint its write-ahead log), `docker rm jobhunter` and run again. Never copy a database file over `jobhunter.db` while a `jobhunter.db-wal` or `jobhunter.db-shm` from another run sits next to it: SQLite would replay that log onto the new file and tear it.

The app writes a copy of its database every day to `backup/jobhunter-YYYY-MM-DD.db` in the data root and keeps the newest seven. Those copies are the ones to read or sync from the host, for example to a cloud drive. Never open or copy the live `jobhunter.db` from the host, wherever it lives: its newest writes sit in `jobhunter.db-wal`, and a process on the host shares neither SQLite's locks nor its log with the container.

Because the container has no desktop, prefill drives a Chromium already running on the host over the Chrome DevTools Protocol instead of launching one. Start it once, headed, on its own profile and a free debug port:

```bash
chrome --remote-debugging-port=9333 --user-data-dir=<folder> --no-first-run --no-default-browser-check
```

`Prefill__BrowserEndpoint` points the container at it; the container reconnects on every prefill and never closes it.

## Daily use

Refresh fetches every enabled source, dedupes, applies the deterministic rules and retires the postings that are no longer listed or went stale. It does not score. Score, next to it, sends the jobs that passed the rules and carry no score to the model: the ones added by hand first, then the ones that need no relocation, then the onsite or hybrid ones outside the home city, each by stated yearly pay with the highest first and unknown pay last, the newest first among equals, up to the per-run cap. The button shows how many jobs are waiting and is disabled while a refresh or a score run is in progress; the two never run at the same time. The Inbox then shows only the class A and B jobs waiting for a decision, 50 to a page with a page bar under the list, and narrows them by class and by flags (a job must carry every ticked flag, as on All Jobs); pay is normalized to the base currency from Settings, per year.

- Save creates the application at the Saved status; "Write the kit" on the job page writes the application kit when you want it. Skip removes the job from the Inbox for good.
- The job page carries the score and its reasoning, the kit with a copy button per section, "Open & prefill" and a Status card. The card moves the application to any status with a note, and records the channel when the status is Applied; a job with no application gets one, without a kit.
- The Cover letter section of a saved job's page writes a cover letter on demand with "Write cover letter" (and "Rewrite" once one exists, replacing it), through the kit model, from your profile, your cover-letter template and your resume. It shows the letter as it will read, header and name included, copies it as plain text, and downloads it as a .docx: A4, one font at 11 pt, the name line bold and larger, named after you and the company. The header is filled from Settings at the moment you view or download it, so a changed phone number shows up in every letter. Without an API key the button is off and the section names the `/jh:cover` command for that job instead.
- Prefill opens the posting in a headed browser and fills the standard fields and the resume. It never clicks Submit or Apply, and it leaves custom questions alone.
- Pipeline tracks each application through its statuses with notes, a contact and a next action; Stats answers how the search is going.
- All Jobs shows everything including the jobs the rules dropped, with the reason, so the rules stay auditable. It lists 50 jobs a page, newest first, with the number of matches and a page bar under the table; the filters narrow the whole list, and a click on Company, Title or Age sorts the whole list, not just the page.
- Skills lists every skill the scored postings ask for: in how many jobs and what share of them, how often as primary, required or nice to have, the median pay, and the category, narrowed by class, a pay floor, the posting age and whether to leave out US jobs. Setting a skill's CV mark there makes it yours (scoring never overwrites it) and reclassifies every job naming the skill; merging one skill into another turns its names into aliases of the other and reclassifies the same way, without calling the model.

Contact details, the resume paths, compensation minimums and the enabled sources all live on the Settings page, and your profile lives in the data root. Nothing personal is stored in the repository.

## Working without an API key

The same work can run through Claude Code instead of the API. Without a key the Score button stays disabled and the Inbox points to this path. The app exports JSONL, a repository-local skill writes JSONL back, and the app imports it. A skill never touches the database.

1. **Export.** The Inbox has an Export button. It writes three files into `data/exchange/`:
   - `to_score.jsonl` — every active job that passed the rules and was scored before skills were extracted, then every such job that carries no score, one per line.
   - `to_kit.jsonl` — every saved job whose kit is missing or failed, one per line, with the score it was chosen on.
   - `resume.md` — a copy of the resume markdown from the path in Settings, for the scoring skill to match skills against and the kit skill to draw facts from.

   The export is not capped. After a first intake it can run to thousands of lines; cut the file down to the jobs worth scoring before running the skill.

2. **Run the skill.** Start Claude Code in the repository root and invoke the skill by name:
   - `score-jobs` reads `to_score.jsonl` and `resume.md` with your profile and rubric, `prompts/score.md` and `prompts/schemas/score.schema.json`, and appends one object per job to `data/exchange/scored.jsonl`.
   - `write-kits` reads `to_kit.jsonl` and `resume.md` with your profile and question bank, `prompts/kit.md` and `prompts/schemas/kit.schema.json`, and appends one object per job to `data/exchange/kits.jsonl`.

   Both append and both skip job ids they have already written, so an interrupted run is safe to repeat.

3. **Import.** The Import button next to Export reads `scored.jsonl` and `kits.jsonl`. Every line goes through the same contract the API path is held to — the schema's required properties, its nullability, the 0 to 2 range of the scores, and the vocabulary the schema allows for fields such as the remote policy and the employment type, with any property the schema does not declare refused outright — and is stored the same way, with the model recorded as `claude-code`. The class letter is computed in the app from the dimension scores, the facts and the settings, on this path exactly as on the API path. A line that does not hold up is refused on its own, listed with its file, its line number and the reason, and the other lines still import.

4. **Import clears its input.** A line that imports is removed from `scored.jsonl` or `kits.jsonl`, so running Import again cannot apply it a second time. Once a file has nothing left to import, Import renames it with a timestamp instead of deleting it, so the result stays on disk if it is ever worth checking again. A line that was refused stays behind in the original file, under its own name, ready to fix and import again.

### Claude Code commands: `/jh:add`, `/jh:score` and `/jh:cover`

Two commands in `.claude/commands/jh/` do the same scoring without the Export and Import buttons, and a third writes a cover letter without a key. They run on the Claude Code subscription, never on the API, and score with the `score-jobs` skill. They talk to the running app over HTTP, and the app imports through the same code as the Import button, so the class, the flags and the pay come from the app exactly as for any other job.

- `/jh:add <link> [more links]` reads each posting in your Chrome through the Claude in Chrome extension, scores it, adds it as a manual job and answers with the class, the total out of 14, the reasons that decided it, the blocking unknowns and the job's page. A link the app already holds is not added twice; the answer shows the stored class. When Chrome cannot show the posting (for example when you are logged out), the command asks you to paste the text.
- `/jh:score [max jobs]` scores every job still waiting for a score, in batches of about 10, one subagent per batch. Each batch is imported the moment it is scored, so an interrupted run keeps its progress. It ends with the count per class, the refused lines, and the A and B jobs with their links.
- `/jh:score <job id or link> [more ids or links]` re-scores exactly those jobs, even when they are already scored, taking a job id or a job page link such as `http://localhost:5150/jobs/<id>`. Like Score again on the job page, it replaces the score and the class and leaves the triage, the application and the kit alone. It also lists every id the app does not hold.
- `/jh:score run [haiku|sonnet|opus] [take=<n>] [batch=<n>] [parallel=<n>] [max=<n>]` scores the waiting jobs in rounds until none is left to take or the run has taken `max` jobs. Each round takes `take` jobs through `GET /exchange/to-score?take=`, cuts them into batches of `batch` and scores them on the one model with up to `parallel` subagents at once; each subagent posts its batch with `?model=`, so every score records the model that wrote it. The session prints one line per round. Esc stops the run, and running it again carries on, because the queue and the held jobs live in the app. Fable is refused: in a headless run it bills usage credits without asking. The command's argument hint gives the defaults. A long run belongs in an interactive Claude Code desktop session with usage credits off: at a usage limit that session waits and carries on by itself, while `claude -p` does not wait.
- `/jh:cover <job id or link>` writes the cover letter for one saved, scored job from your profile, your cover-letter template and your resume, the same instructions the Write cover letter button sends to the API. The app checks and lints the letter exactly as it does for the button and stores it on the job's application, replacing any earlier one; the command answers with the job's page, where the letter can be copied or downloaded as .docx, and the lint findings.

Two environment variables point the commands at the app, set where Claude Code runs:

- `JOBHUNTER_URL` — the address of the running app. Default `http://localhost:5150`.
- `JOBHUNTER_DATA_ROOT` — the data root whose `profile/` the skill reads, when it is not `data/` in the repository (for example the host folder a container mounts). Without the right profile the skill falls back to the example files and says so.

The endpoints, all local and without authentication like the rest of the app:

| Route | Body | Answer |
|---|---|---|
| `GET /exchange/to-score` | — | the to-score lines in scoring order, as the Export button writes them: the jobs scored before skills were extracted, then the unscored jobs |
| `GET /exchange/resume` | — | the resume markdown from the path in Settings, which the scoring skill matches skills against; `404` with `refusal` when it cannot be read |
| `GET /exchange/to-score?take=<n>` | — | at most n of those lines, skipping the jobs an earlier batch still holds; each job handed out is held for 30 minutes or until its score is posted, and a restart frees them all; `400` with `refusal` when n is not a positive whole number |
| `POST /exchange/scored[?model=<id>]` | score lines (`prompts/schemas/score.schema.json`), one per line | `imported`, `refused`, and per line the `job_id` with its `class`, `total` and `flags`, or its `refusal`; each score records `model` (1 to 64 letters, digits, dots or dashes) or `claude-code` without it; `400` with `refusal` and nothing imported for any other model |
| `POST /exchange/new-job` | one new-job line (`prompts/schemas/new_job.schema.json`) | `201` with `job_id`, `class`, `total`, `flags` and `job_page`; `200` with `already_exists` when the link is already held; `400` with `refusal` |
| `GET /exchange/to-cover?job=<job_id>` | — | the saved job as a kit line (job, score, class, flags, `language_hint`) plus `resume`, the resume markdown or null; `404` with `refusal` for an unknown or malformed id; `409` with `refusal` for a job that is not saved or not scored |
| `POST /exchange/cover-letter` | one cover letter (`prompts/schemas/cover-letter.schema.json`) | `201` with `job_id`, `job_page` and `lint_issues`; `400` with `refusal` |
| `GET /exchange/to-prep?job=<job_id>` | — | any stored job for an interview prep: the posting with its whole description, the score and class or null, `resume` or null, the `application` (status, history, notes, contact, pay discussed, kit, cover letter) or null, and `pay`, the pay settings; `404` with `refusal` for an unknown or malformed id |

The job page downloads its letter from `GET /jobs/<job_id>/cover-letter`.

## Interview prep

`/jh:prep <job id or link> [type[+type]] [context]` prepares one interview round, on the Claude Code subscription only. It reads the job through `GET /exchange/to-prep`, researches the company on the web, and never writes to the app: the application's status stays as it is.

- **Types:** `screening` (a recruiter: motivation, logistics, pay), `manager` (the hiring manager: past projects, your own impact, how you work), `tech` (live coding, pair programming, a take-home review or stack questions), `system-design` (a design on a shared whiteboard, or a system you built) and `fit` (behavioral and values, often the final round). A joint round joins two types, as in `manager+tech`. Without a type, the command takes it from the application's status. Anything after the type is free-text context: who interviews, how long, the platform, the format, the invitation.
- **Files:** `<data root>/interviews/<Company>/`, with `<Company>_<Round>_Prep.md`, `<Company>_<Round>_Mock_Interview.md` and the cue cards as `<Company>_<Round>_Cue_Cards.pdf`. The prep is what you read before the call, on any device or on paper, and it never sends you to another file: a header, then Part 1, every topic the round may touch, led by any topic list the company sent, each item covered in full, each topic with what it is in plain words and your answer with your own experience worked in, a plain "haven't used it, here's the nearest thing" for a gap; then Part 2, the questions you may get with short answers; then your questions, a short checklist and the abbreviations it uses. The mock interview is a separate brief that works on its own when you attach it to a chat in the Claude app and rehearse by voice; attach the prep too for feedback against your prepared answers. A live-coding mock runs as a text chat. The cue cards are one A4 page, printed with headless Edge or Chrome from an HTML file that is deleted after the print. When no browser can print it, the HTML is kept instead.
- **`interview.md`:** your coaching rules for the mock interviewer, your pitch at 30 seconds, 90 seconds and 2 minutes, recurring answers, gaps and bridges, stories, your pay method, logistics and drills. The prep quotes the pitch word for word, so it stays the same in every round, and writes its answers from your recurring answers and stories, keeping their facts. It flags a missing story instead of inventing one. It is looked up like the other profile files: `<data root>/profile/interview.md`, else `profile/interview.example.md`.
- **`glossary.md`:** your tech drill, numbered short answers to the general knowledge questions: the runtime and the language, data, testing, the front end, process and tools. The prep explains its topics and writes out its general questions from these items, without item numbers, and lists any topic the drill files lack so you can add it. Looked up the same way: `<data root>/profile/glossary.md`, else `profile/glossary.example.md`.
- **`cloud.md`:** the cloud and infrastructure half of the drill: how a cloud fits together, each cloud's services in the depth your roles need, networking and identity, load balancing, infrastructure as code and containers. Its items share the glossary's number series, and the prep draws on them the same way. Looked up the same way: `<data root>/profile/cloud.md`, else `profile/cloud.example.md`.
- **`system-design.md`:** the reasoning half of the drill: architecture and messaging, domain-driven design, and scenarios that show how you think. Its items share the glossary's number series, so an item moved out of the glossary keeps its number. The prep draws on them the same way, and tech and system design rounds draw on both files. Looked up the same way: `<data root>/profile/system-design.md`, else `profile/system-design.example.md`.
- **`project-example.md`:** your reference systems, the systems you walk an interviewer through: for each, its summary, requirements, design, data flow, failure handling, security, scale, trade-offs, what you would change, deep-dive points and honest limits. Tech, system design and manager rounds take their talking points from it. Looked up the same way: `<data root>/profile/project-example.md`, else `profile/project-example.example.md`.
- **`ai-workflow.md`:** how you work with AI coding agents day to day: instruction files, how the agent finds its way around your code and notes, planning and subagents, memory, skills and hooks, and how you review its work. The prep answers from it when a round asks how you use AI, and for roles that build AI tooling. Looked up the same way: `<data root>/profile/ai-workflow.md`, else `profile/ai-workflow.example.md`.
- **Stars:** a ★ after a drill item's question means a real interview asked it, one star per round. The prep always writes out the items with two or more stars that the round touches, and keeps starred items before the rest when it has to cut.
- **Format:** `project-example.md` and `ai-workflow.md` open with a short outline, in the order you would tell it. Each topic of `ai-workflow.md`, and any field of `project-example.md` you say out loud, then comes in layers: what you say, what to add if they dig, and why, in short active sentences. `glossary.md`, `cloud.md` and `system-design.md` stay numbered questions with short bullets.
- **Pay:** the prep is the only place the pay settings reach a model. It works out your line for the job's location and contract form, with market figures and their sources, and keeps it in your data root. Each job's range and walk-away are saved in the company's research file with what they rest on, and the ask-first question always comes from `interview.md`, so every later round for that job says the same line: a range you already gave the company and logged in JobHunter stays as given, and one not yet given is worked out again only when a pay setting it uses, the contract form, a market figure it rests on or the moving cost changes. Scoring, kits and cover letters never see the numbers.
- **Research:** the prep keeps what it finds in private files in the data root, one per company and one per country. `interviews/<Company>/<Company>_Research.md` holds the company, its reported interview process, what later rounds look for (the team, the stack, the values) and the pay market for the role; `interviews/_Take_Home/<Country>.md` holds how pay is paid and taxed in that country for a named tax year, a gross-to-net table by amount that any job there can use, and the one-off cost of moving to each city a prep looked at, worked out once per city and reused. Every fact carries its source and the date it was checked, a computed figure says what it was computed from, and a search that found nothing is noted with its date. A later prep searches only for what the files lack and for what has gone stale: news after 30 days, take-home pay when the prep needs a later tax year, and everything else after 180 days. To force fresh research, delete a file, or the lines you want checked again.
- **Statuses:** the pipeline names its statuses after the rounds: Saved, Applied, Screening, Manager, Tech, System design, Fit, Offer, then Accepted, Rejected, Withdrawn or Ghosted. An application stored at the old Interview 1 or Interview 2 moves to Tech, and Final moves to Fit.

The instructions live in `prompts/interview/`: `prep.md` holds the rules every round shares, one guide per type says who is in the room, what they score and how the mock interviewer behaves, and `cue-cards.html` is the card template.

`/jh:debrief <transcript file>` runs after a round, on the transcript saved as `<data root>/interviews/<Company>/<Company>_<Round>_transcript.md`. It lists every question the round asked in `<Company>_<Round>_Debrief.md`, with the drill item that answers it and how the answer went, writes a new item for a question no item answers (a chain of follow-ups becomes one scenario), and adds a ★ to each item it maps. A round is counted once: run it again and it stops. The next prep for the company writes out every question its debriefs list.

## Cost guard

Of the two runs, only Score spends money; a refresh never calls the model. Two settings bound it.

- **Max scores per run** (`MaxScoresPerRun`, default 300) caps how many jobs one press of Score sends to the model. Everything above the cap stays unscored and waits for the next press, so lowering it slows scoring down rather than losing jobs.
- **First-run window (days)** (`FirstRunWindowDays`, default 21) is how far back a posting may have been published to be taken in at all. It is the upstream control: a narrower window means fewer jobs reach scoring in the first place.

The score model and the kit model, which also writes cover letters, are settings too, and a cheaper score model reading the same rubric is the third way to bring the bill down. A change takes effect with no restart: the window on the next refresh, the cap and the score model on the next score run, the kit model on the next kit written.

## Layout

| Path | What is in it |
|---|---|
| `src/JobHunter/` | The app: `Domain/`, `Data/`, `Sources/`, `Pipeline/`, `Llm/`, `Prefill/`, `Components/Pages/` |
| `tests/JobHunter.Tests/` | Unit tests over the rules, the parsers, the scoring and the exchange round trip |
| `profile/` | The example profile files: the four the app reads and `interview.example.md` for `/jh:prep`; your own copies live in the data root (see Your profile) and are shared by both model paths |
| `prompts/` | The scoring, kit and cover-letter instructions and their JSON schemas, shared by both model paths, and the interview-prep rules, guides and card template in `prompts/interview/` |
| `.claude/skills/` | The `score-jobs` and `write-kits` skills |
| `.claude/commands/jh/` | The `/jh:add`, `/jh:score`, `/jh:cover` and `/jh:prep` commands |
| `data/` | The database, your profile files, cached source downloads, the exchange folder, the interview preps and the browser profile. Gitignored |
