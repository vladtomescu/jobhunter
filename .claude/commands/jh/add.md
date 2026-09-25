---
description: Read job postings from Chrome by link, score them with the score-jobs skill and add them to JobHunter with the class the app computes
argument-hint: <posting link> [more links]
---

Add these postings to JobHunter, scored: $ARGUMENTS

The app runs at `$JOBHUNTER_URL`, or `http://localhost:5150` when that variable is not set; below it is written `<app>`. Everything runs on this Claude Code session. Never call the Anthropic API and never read or write the app's database; the app does the storing and the classifying.

The arguments are one or more posting links, separated by spaces or line breaks. An argument that is not an http or https link: say so in one line and skip it. For each link, one at a time:

1. **Normalize the link.** A LinkedIn link becomes `https://www.linkedin.com/jobs/view/<id>/`, the id taken from the `/jobs/view/<id>` path or the `currentJobId` parameter, so the same posting always arrives under the same link. Other links stay as given.

2. **Read the posting in a browser.** Load the browser tools with ToolSearch first when they are deferred.
   - First the Claude in Chrome tools (`mcp__claude-in-chrome__*`), which run in my own Chrome with my logins: open the link with `navigate`, then read it with `get_page_text`.
   - When Claude in Chrome is not connected, the Chrome DevTools tools (`mcp__chrome-devtools__*`): open the link with `new_page` in the background, read it with `evaluate_script` returning the `innerText` of the page's `main` element (of `body` when there is none), and close that page with `close_page` once the posting is read. It is a separate browser from my Chrome, so a site that needs my login can show a login wall there.

   In either browser, when the description is collapsed behind a "show more" control, expand it and read again. Take from the page: the job title, the company, the location line, the pay line exactly as written, and the full description.
   - When neither browser is available, or the page shows no posting (a login wall, an error page, an expired job), say so in one line and go on with the next link. Once every other link is done, ask me in one message to paste the text of each posting that could not be read, and continue with what I paste.

3. **Score it.** Follow the score-jobs skill, `.claude/skills/score-jobs/SKILL.md`, in caller mode. Build the to-score line from the posting with `job_id` the empty string, save it in a temporary folder outside this repository, and score it; the score object's `job_id` stays the empty string.

4. **Post it.** Build one new-job line as `prompts/schemas/new_job.schema.json` defines it (the posting fields plus the score object under `score`), save it in the same temporary folder, and send it:

   ```bash
   curl -s -w '\n%{http_code}\n' -X POST "${JOBHUNTER_URL:-http://localhost:5150}/exchange/new-job" --data-binary @<file>
   ```

   - `201`: stored. The body holds `job_id`, `class`, `total`, `flags` and `job_page`.
   - `200` with `already_exists: true`: the app already holds this link. Nothing was stored or rescored; the body holds the stored job's `class`.
   - `400`: refused. `refusal` gives the reason. Fix the line when the reason is in the line (a schema or vocabulary slip), post once more, and otherwise report the refusal.
   - No answer: the app is not running at that address. Say so and stop.

5. **Answer in 3 to 5 lines per posting:** the class and the total out of 14; the two or three reasons that decided it, taken from the score's `reasoning`; the blocking unknowns, or "none"; and the link `<app><job_page>`. For a duplicate, one line: it is already in JobHunter, its stored class, and the link.

Close with one line naming which profile and rubric copies the skill read.
