---
description: Score every unscored JobHunter job with the score-jobs skill, in batches by subagents, posting each batch to the app as soon as it is scored
argument-hint: [max jobs]
---

Score the jobs JobHunter still holds unscored. $ARGUMENTS

The app runs at `$JOBHUNTER_URL`, or `http://localhost:5150` when that variable is not set; below it is written `<app>`. Everything runs on this Claude Code session. Never call the Anthropic API and never read or write the app's database; the app does the storing and the classifying.

1. **Fetch.** Make a temporary folder outside this repository and save the unscored jobs into it without reading them into this conversation:

   ```bash
   curl -s -f "${JOBHUNTER_URL:-http://localhost:5150}/exchange/to-score" -o <folder>/to_score.jsonl
   ```

   Each line has the to-score shape the score-jobs skill describes. No answer means the app is not running at that address: say so and stop. An empty file means nothing is waiting: say so and stop. When a number was given above, keep only that many lines from the top (the newest postings come first).

2. **Split.** Cut the file into batches of 10 lines (`split -l 10`), still without reading them here.

3. **Score each batch in its own subagent,** so this conversation stays small. Run a few at a time. Each subagent gets the path of its batch and these instructions:
   - Follow the score-jobs skill, `.claude/skills/score-jobs/SKILL.md`, in caller mode: score every line of the batch and write the objects, one per line, into a scored file next to the batch.
   - Post that file as soon as it is written:

     ```bash
     curl -s -X POST "${JOBHUNTER_URL:-http://localhost:5150}/exchange/scored" --data-binary @<scored file>
     ```

     The answer lists every line with its `job_id` and either the `class`, `total` and `flags` the app computed or the `refusal` reason. When a refusal names a slip in the object (a schema or vocabulary mistake), fix that object and post it once more on its own.
   - Report back only: the count per class, the refusals with their job ids and reasons, the jobs that could not be scored with their job ids, the A and B jobs as job id, title and company, and which profile and rubric copies were read.

   A batch is stored the moment its subagent posts it, so an interrupted run loses at most the batches in flight, and running this command again fetches only what is still unscored.

4. **Finish** with:
   - the jobs fetched, and the count per class across all batches;
   - the refusals and the jobs that could not be scored, with their job ids and reasons;
   - every A and B job as title, company and the link `<app>/jobs/<job_id>`;
   - which profile and rubric copies were read, flagged plainly when an example copy was used.

Delete the temporary folder at the end.
