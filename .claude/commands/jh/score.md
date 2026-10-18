---
description: Score every unscored JobHunter job, or re-score the jobs named by id or link even when already scored, with the score-jobs skill, in batches by subagents, posting each batch to the app as soon as it is scored; run mode scores the queue in rounds on one model until it is empty or a job count is reached
argument-hint: [max jobs] | <job id or link> [more ids or links] | run [haiku|sonnet|opus, default sonnet] [take=jobs per round, default batch × parallel] [batch=jobs per subagent, default 10] [parallel=subagents at once, default 5] [max=jobs in the whole run, default no limit]
---

Score the jobs JobHunter still holds unscored, or re-score the jobs named here. $ARGUMENTS

The app runs at `$JOBHUNTER_URL`, or `http://localhost:5150` when that variable is not set. Resolve the address once, before anything else, and give it in your first line:

```bash
echo "${JOBHUNTER_URL:-http://localhost:5150}"
```

Below, `<app>` is the address it printed. Write it out literally in every command and in every subagent's instructions, never as the variable, so every batch goes to the same app. Everything runs on this Claude Code session. Never call the Anthropic API and never read or write the app's database; the app does the storing and the classifying.

The arguments above choose what is scored:

- **`run` first:** run mode, described at the end; the steps right below do not apply.
- **Nothing:** every job scored before skills were extracted, then every job still waiting for a score.
- **A single number:** at most that many of those jobs, the first ones in the scoring order.
- **One or more job ids or job links:** exactly those jobs, scored or not. A link is a job page such as `<app>/jobs/<job_id>` or `/jobs/<job_id>`; its job id is the identifier after `/jobs/`. An argument that is neither a number, a job id nor a job link: say so and stop. Re-scoring replaces the job's score and class, the same as Score again on the job page; its triage, application and kit stay as they are.

1. **Fetch.** Make a temporary folder outside this repository and save the jobs into it without reading them into this conversation. Without arguments:

   ```bash
   curl -s -f "<app>/exchange/to-score" -o <folder>/to_score.jsonl
   ```

   With a number, ask for that many:

   ```bash
   curl -s -f "<app>/exchange/to-score?take=<number>" -o <folder>/to_score.jsonl
   ```

   With job ids, name each one in its own `job` parameter:

   ```bash
   curl -s -f "<app>/exchange/to-score?job=<job_id>&job=<job_id>" -o <folder>/to_score.jsonl
   ```

   Fetch the resume the skills are matched against into the same folder, also without reading it here:

   ```bash
   curl -s -f "<app>/exchange/resume" -o <folder>/resume.md
   ```

   When that fails, the app holds no readable resume: delete any `resume.md` it left, tell the subagents there is none, and say so in the finish.

   Each line has the to-score shape the score-jobs skill describes. The jobs scored before skills were extracted come first, so that they get their skills; the jobs still waiting for a score come after them. Each group comes in the app's scoring order: jobs added by hand first, then jobs that need no relocation, then onsite or hybrid jobs outside the home city, each by stated yearly pay with the highest first and unknown pay last, the newest first among equals. With a number, the app skips jobs another run still holds and holds the ones it returns for 30 minutes, or until their scores are posted. No answer means the app is not running at that address: say so and stop. With job ids, list the `job_id` of every line (for example `grep -o '"job_id":"[^"]*"'`, still without reading the rest of the lines here) and note every requested id that did not come back: the app holds no job under it. An empty file means nothing is waiting, or none of the named jobs is stored: say so, with the ids that did not come back, and stop.

2. **Split.** Cut the file into batches of 10 lines (`split -l 10`), still without reading them here.

3. **Score each batch in its own subagent,** so this conversation stays small. Run a few at a time. Each subagent gets the path of its batch, the path of `resume.md` or word that there is none, and these instructions:
   - Follow the score-jobs skill, `.claude/skills/score-jobs/SKILL.md`, in caller mode: score every line of the batch, matching the skills against the resume, and write the objects, one per line, into a scored file next to the batch.
   - Post that file as soon as it is written:

     ```bash
     curl -s -X POST "<app>/exchange/scored" --data-binary @<scored file>
     ```

     The answer lists every line with its `job_id` and either the `class`, `total` and `flags` the app computed or the `refusal` reason. When a refusal names a slip in the object (a schema or vocabulary mistake), fix that object and post it once more on its own.
   - Report back only: the count per class, the refusals with their job ids and reasons, the jobs that could not be scored with their job ids, the A and B jobs as job id, title and company, and which profile and rubric copies were read.

   A batch is stored the moment its subagent posts it, so an interrupted run loses at most the batches in flight. Without job ids, running this command again fetches only what is still unscored; with job ids, it scores the named jobs once more.

4. **Finish** with:
   - the jobs fetched, and the count per class across all batches;
   - with job ids, every requested id that did not come back;
   - the refusals and the jobs that could not be scored, with their job ids and reasons;
   - every A and B job as title, company and the link `<app>/jobs/<job_id>`;
   - which profile and rubric copies were read, flagged plainly when an example copy was used.

Delete the temporary folder at the end.

## Run mode

`run` scores the waiting jobs round after round on one model, until a round finds nothing to take or the run has taken `max` jobs. Its settings follow `run`, in any order:

- **The model:** `haiku`, `sonnet` or `opus`, bare or as `model=`; default `sonnet`. Refuse any other model, Fable included, and stop: in a headless run Fable bills usage credits without asking. The app records it as `claude-haiku-5-5`, `claude-sonnet-5-5` or `claude-opus-5-5`, written `<model id>` below.
- **`take=<n>`:** the jobs one round takes; default `batch` × `parallel`.
- **`batch=<n>`:** the jobs one subagent scores; default 10.
- **`parallel=<n>`:** the subagents running at once; default 5.
- **`max=<n>`:** the jobs the whole run takes; default no limit.

Each number is a positive whole number. Anything else, or any other argument: say so and stop.

A round takes `take` jobs, or what is left of `max` when that is fewer, cuts them into batches of `batch` lines and scores the batches `parallel` at a time, the next group once the one before it has answered. A round that takes more than `batch` × `parallel` jobs simply runs more groups.

1. **Start.** Make one temporary folder outside this repository for the whole run, and fetch the resume into it once, without reading it here:

   ```bash
   curl -s -f "<app>/exchange/resume" -o <folder>/resume.md
   ```

   When that fails, delete any `resume.md` it left and write `<resume file>` below as `none`; otherwise `<resume file>` is that path. Your first line gives `<app>`, `<model id>`, the settings and whether a resume was found, for example `run at http://localhost:5150 on claude-sonnet-5-5: take 50, batch 10, parallel 5, no max, resume found`.

2. **Take** the round's jobs, without reading them into this conversation:

   ```bash
   curl -s -f "<app>/exchange/to-score?take=<n>" -o <folder>/round.jsonl
   ```

   No answer means the app is not running at that address: say so and stop. Count the lines with `wc -l`. A round that takes 0 lines ends the run: nothing is waiting, or another run holds everything left.

3. **Split** the file with `split -l <batch> <folder>/round.jsonl <folder>/batch_`, still without reading it.

4. **Score.** One subagent per batch, with the short model name (`haiku`, `sonnet` or `opus`) as its `model`. Start each group of up to `parallel` subagents in one message, in the foreground, and wait for all of them. Each subagent gets exactly this text, with `<batch file>`, `<resume file>`, `<app>` and `<model id>` filled in:

   ```text
   Score the JobHunter jobs in <batch file> and post the scores to the app at <app>.

   1. Follow the score-jobs skill, .claude/skills/score-jobs/SKILL.md, in caller mode: read the files it names yourself, with the resume at <resume file>, score every line of the batch, and write the objects, one per line, into <batch file>.scored.jsonl.
   2. Post that file to exactly this address:

      curl -s -X POST "<app>/exchange/scored?model=<model id>" --data-binary @<batch file>.scored.jsonl

      The answer lists every line with its job_id and either the class the app computed or a refusal. When a refusal names a slip in the object (a schema or vocabulary mistake), fix that object, save it on its own in a new file and post that file the same way, once.
   3. Answer with only this, nothing else:

      posted <n> (A <n>, B <n>, C <n>, D <n>), refused <n>, failed <n>
      <job_id>: <reason in a few words>, one line per refused or failed job

      posted counts the lines the app imported, refused the lines it still refuses, failed the lines you could not score or could not post (all of them when the app does not answer). When you read the example copy of profile.md or rubric.md, add a last line saying which.
   ```

5. **Report the round** in one line, adding up the subagents' answers, for example `round 3: 50 taken, 48 posted (A 1, B 6, C 20, D 21), 2 refused, 0 failed; 150 so far`, where "so far" counts the jobs this run has taken. A subagent that fails or answers in another form counts its whole batch as failed. It is not tried again: the app hands its jobs out again once their 30-minute hold ends. When a subagent read an example profile or rubric, say so on the round's line and end the run, because a score against the example profile is a score for somebody else. Otherwise delete the round's files and go back to step 2, unless the run has taken `max` jobs, which ends it.

6. **Finish** the run with one line giving the rounds, the jobs taken and the totals in the round-line form, then every refused or failed job with its job id and reason, and a pointer to the Inbox at `<app>` for the new A and B jobs. Delete the temporary folder.

Esc stops the run. Every batch already posted stays stored, and running `run` again carries on with what is still waiting, because the queue and the holds live in the app.
