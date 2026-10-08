# AI workflow

How I work with AI coding agents day to day, written the way I say it. The interview prep (`/jh:prep`) quotes it word for word, like `interview.md`, when a round asks how I use AI, and for roles that build or run AI tooling. Every claim in it is about my own work, so it must hold when an interviewer digs. Only `/jh:prep` reads this file; it is private and never sent to anyone.

This is the example AI workflow file that ships with JobHunter, for the fictional candidate of the example profile: every line below is a placeholder. Copy it to `profile/ai-workflow.md` under your data root and replace each topic with how you work: the instruction files you keep, how the agent finds its way around your code and your notes, how you plan and split the work, what the agent remembers, the skills and hooks you rely on, and how you review what it writes.

The file opens with an outline, in the order you would tell it. Each topic then has three layers: **Say:**, one or two spoken sentences; **If they dig:**, three to five short bullets; **Why:**, one line on the trade-off. Keep each sentence to 20 words at most, with one idea per sentence, in the active voice, and use one term for one concept.

## Outline

1. I use Claude Code every day, and I stay responsible for every line.
2. Instruction files tell the agent how each repository works.
3. A code index and a notes folder let it find things fast.
4. Bigger work starts with a plan I approve, then runs in small tasks.
5. Memory keeps my corrections, so I make each one once.
6. Skills and hooks turn repeated steps into checks.
7. I review every change like a colleague's pull request.

## Instruction files

**Say:** Each repository has an instruction file for the agent. It holds the build commands, the conventions and the rules it must not break.

**If they dig:**
- The file lives in the repository, and changes to it get a review like code.
- A personal file holds my own rules, such as how short a report should be.
- A rule the agent breaks twice gets sharper wording.
- No secrets go in it.

**Why:** The file costs some upkeep, and it saves the same correction in every session.

## Finding its way around

**Say:** The agent searches a code index before it opens files. A notes folder holds the decisions and how the parts fit together.

**If they dig:**
- The index answers "where is this used" without reading the whole codebase.
- The notes folder has one index page that links to the rest.
- Each note covers one topic, so the agent reads only what the task needs.
- A wrong note gets fixed in the same change that found it.

**Why:** Current notes take effort, and they keep the agent's context small and correct.

## Planning and execution

**Say:** For anything bigger than a small fix, the agent writes a plan first. I approve it, and then it works through it task by task.

**If they dig:**
- Each step names the check that proves it is done.
- Subagents run the independent tasks in parallel.
- Each task names its model: a stronger one for design, a faster one for routine edits.
- The plan and its progress live in files, so the work survives a restart.
- After two failed fixes, the agent stops and reports the cause.

**Why:** A plan slows the start, and it catches a wrong direction before any code exists.

## Memory

**Say:** When I correct the agent, it saves the rule together with my reason. The next session starts with it.

**If they dig:**
- One short file per rule, and an index the agent reads at the start.
- The reason lets the agent judge cases the rule does not name.
- I prune rules that no longer hold.

**Why:** Memory can go stale, so I keep it short and check it.

## Skills and hooks

**Say:** A skill is a written procedure the agent loads for one kind of task. A hook is a script that runs on an event and can block a step.

**If they dig:**
- Skills cover the work I repeat, such as a release checklist.
- Hooks enforce the rules I will not leave to memory, such as tests before a commit.
- Both live in version control, so the setup moves with me.

**Why:** A hook is strict and sometimes in the way, and it never forgets.

## Review

**Say:** I review every change before I commit it. I read the diff, and I watch the change work myself.

**If they dig:**
- The agent proves a change works with a test, a log line or a request.
- A clean build proves that the code compiles, not that it runs.
- The agent explains any line I do not understand, and I reject what I cannot defend.
- For a risky plan, a second model reviews it before any code.

**Why:** Full review costs time, and I stay accountable for what ships.
