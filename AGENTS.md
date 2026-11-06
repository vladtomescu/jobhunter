# JobHunter

JobHunter is a personal, local Blazor Server app. It pulls job postings, then filters and scores them against the user's profile.

## Commands

- Run: `dotnet run --project src/JobHunter --launch-profile https`
- Test: `dotnet test JobHunter.slnx`

## Layout

- `src/JobHunter/`: the app.
- `tests/`: the tests.
- `profile/`: example profile files.
- `prompts/`: prompts the app and commands use.
- `.claude/skills/`: the `jh-*` skills.

## Data

- `data/` holds personal job-search data, and a SQLite database by default.
- Do not copy personal data into commits, issues or public text.

`README.md` is the full reference.
