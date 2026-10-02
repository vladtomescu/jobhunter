# Interview

My spoken material for interviews, written the way I say it. The interview prep (`/jh:prep`) reuses my pitch, my recurring answers and my stories word for word: it only chooses and orders them, and writes one sentence for the role in the [Tailor] slot of the pitch. Every fact about my work in this file traces to my profile and my resume. When a round needs a story or a fact that is not here, the prep says so instead of inventing one. Only `/jh:prep` reads this file; it is private and never sent to anyone.

This is the example interview file that ships with JobHunter, for the fictional candidate of the example profile: every fact below is a placeholder. Copy it to `profile/interview.md` under your data root and replace each section with your own material, keeping the headings.

## Coaching

How the mock interviewer treats me.

- Inside the role-play, call me Alex. Outside it, talk to me plainly, as a coach.
- Ask cold: no hints, and no lists of possible answers. Hints make the practice worthless.
- After the round: a verdict in one or two sentences, then per answer a grade (✅ strong, 🟡 OK, 🔴 risk), the single most important fix, and how my plain phrasing is usually put in an interview: a translation, never a correction.
- Flag any answer over two minutes. I run long when I am nervous.
- Every suggested answer stays inside my files: never add a title, a metric, a scope or a technology.
- What I want drilled: stopping after the result, "I" instead of "we", and the pay question.

## Pitch

Each version ends with a [Tailor] slot on its own line. The prep puts one sentence for the role there, from the posting and the research, and quotes the rest word for word.

### 30 seconds

> I'm a backend engineer with nine years in software, the last six on payment and ledger services. At a fintech product company I own the ledger service that settles 40 million transactions a day, and I led its move from a nightly batch to an event-driven pipeline. I'm looking for a team where payments are the product.
>
> [Tailor: one sentence that ties the pitch to this role.]

### 90 seconds

> I'm a backend engineer with nine years in software, and the last six have been on payment and ledger services.
>
> Since 2021 I've worked at a fintech product company, on a remote team. I own the ledger service there end to end: design, delivery, on-call and the roadmap. It settles 40 million transactions a day, and other teams build on it.
>
> The biggest change I led was moving settlement from a nightly batch to an event-driven pipeline on Kafka. Settlement lag went from hours to minutes. I also mentor two engineers and run the on-call rotation for the service.
>
> I work in Java and Kotlin with Spring Boot, on PostgreSQL and Kafka, running on Kubernetes in AWS.
>
> The ledger work is where I do my best engineering, so I'm looking for a team where payments are the product rather than a side system.
>
> [Tailor: one sentence that ties the pitch to this role.]

### 2 minutes

> I'm a backend engineer. I've been in software for nine years, and for the last six I've worked on payment and ledger services.
>
> Since 2021 I've been at a fintech product company, on a remote, English-speaking team. I own the ledger service: design, delivery, on-call and the roadmap for it. It settles 40 million transactions a day, and other teams build on it, so most of my work is about changing it safely.
>
> The change I'm proudest of is the move from a nightly batch to an event-driven pipeline. Settlement used to wait for the batch, so balances lagged by hours. I designed the new flow on Kafka, ran it next to the batch until the totals matched, and then we switched over. Settlement lag went from hours to minutes.
>
> I also mentor two engineers, and I run the on-call rotation for the service.
>
> Day to day I work in Java and Kotlin with Spring Boot, on PostgreSQL and Kafka, deployed on Kubernetes in AWS with Terraform.
>
> The ledger work is where I do my best engineering. What I'm looking for now is a team where payments are the product rather than a side system.
>
> [Tailor: one sentence that ties the pitch to this role.]

## Recurring answers

### Why are you looking?

> I'm happy where I am, and I'm not in a hurry. The ledger work is where I do my best engineering, and I'm looking for a team where payments are the product rather than a side system.

**Never:** a complaint about my current company, or money as the reason.

### Why this company?

**Shape:** two concrete things from the posting or the product, then one sentence on why they matter to me. The prep builds this line for each company from the research.

**Never:** praise without a fact behind it.

### What are you looking for in your next role?

> A product company where payments are the product, and a role where I keep owning a service end to end: the design, the delivery and the on-call.

### What is your main strength?

> I own things end to end. When I take on a service, I take the design, the delivery, the on-call and the roadmap, and other teams can rely on it.

### What is a weakness of yours?

> I tend to hold on to work I could hand over. Mentoring two engineers has helped: I now give them whole parts of the service, not only tickets.

**Never:** a strength dressed up as a weakness.

### This role is levelled differently from your title.

> Titles mean different things at different companies. What matters to me is the scope: owning a service and where it goes next. I'm happy to talk about where this role sits.

**Never:** arguing about the title.

## Gaps and bridges

- **Go.** Honest answer: "I haven't run Go in production. My services are in Java and Kotlin." Nearest: services on the JVM with Kafka and PostgreSQL, the same kind of work.
- **Azure or Google Cloud.** Honest answer: "My cloud experience is AWS." Nearest: Kubernetes and Terraform on AWS, which carry over to any cloud.
- **Line management.** Honest answer: "I don't have direct reports." Nearest: mentoring two engineers and running the on-call rotation.

## Stories

Six to eight stories cover most behavioral rounds. This example holds four, so a prep for a fit round flags conflict and failure as missing.

Every story has the same fields: Proves, Answers, Situation, Task, Action, Result, Telling (the spoken version, about 200 words), Short telling (about 80 words without jargon, for recruiters and product people), Cue lines and Follow-ups.

### From nightly batch to events

- **Proves:** technical leadership; a risky change delivered safely.
- **Answers:** a project you are proud of; your biggest technical challenge; a change you led.
- **Situation:** Settlement ran as a nightly batch, so balances lagged by hours and other teams waited on it.
- **Task:** Cut the lag without putting the books at risk.
- **Action:** I designed an event-driven pipeline on Kafka: every transaction became an event, and the ledger updated as events arrived, with consumers that could replay an event without changing anything. I ran the new flow next to the batch and compared the totals every day, then switched over one product line at a time.
- **Result:** Settlement lag went from hours to minutes, and other teams now read fresh balances.
- **Telling:** Settlement at my company used to run as a nightly batch. Every payment of the day waited for that one job, so balances lagged by hours, and the teams that build on the ledger waited with them. My task was to cut that lag without putting the books at risk. I designed an event-driven pipeline on Kafka. Every transaction became an event, and the ledger updated as the events arrived. I built the consumers so that an event could be replayed without changing anything, because in a ledger a duplicate entry is worse than a late one. I didn't want to switch on day one, so I ran the new flow next to the batch and compared the totals every day. Before each switch I checked that the totals had matched on every day of the run, not only the last one. When they had, we switched over one product line at a time, which kept every step small enough to undo. Settlement lag went from hours to minutes, and the other teams now read fresh balances instead of yesterday's. The side-by-side run was slower than one big switch, but nobody had to take the new flow on faith.
- **Short telling:** Our payment records used to be brought up to date once a night, so the teams that rely on them worked with numbers that were hours old. I rebuilt the system so that each payment is recorded as it happens. To be safe, I ran the new way next to the old one and compared the totals every day, then moved over one product at a time. Now the numbers are minutes old instead of hours, and nobody had to take the new way on faith.
- **Cue lines:** batch to events; replay changes nothing; side by side, totals compared; hours to minutes.
- **Follow-ups:** Why not run the batch more often? What went wrong during the switch? What would you do differently?

### Owning the on-call rotation

- **Proves:** ownership; reliability.
- **Answers:** ownership; how you handle pressure; how you run a production service.
- **Situation:** The ledger service had alerts nobody trusted, and on-call was handled by whoever knew the service best.
- **Task:** Make on-call something the whole team could carry.
- **Action:** I went through every alert with the team, removed the ones nobody acted on, and wrote a short runbook for each one we kept. Then I set up the rotation and took the first shifts with each new person.
- **Result:** Every engineer on the team now takes on-call, with a runbook for each alert.
- **Telling:** On the ledger service, on-call used to fall on whoever knew the service best, and the alerts didn't help. Most of them fired without anyone acting on them, so nobody trusted any of them, and a real problem could hide in the noise. I wanted on-call to be something the whole team could carry, not one or two people. So I sat down with the team and went through every alert, one by one. For each one we asked a single question: the last time it fired, did anyone do anything about it? If nobody had, we removed it. For each alert we kept, I wrote a short runbook: what the alert means, what to check first, and when to call for help. Then I set up a proper rotation. For each new person on it, I took the first shifts together with them, so nobody met their first page alone. Today every engineer on the team takes on-call, and every alert has a runbook. A page now means something, and knowing how to run the service no longer depends on one or two people.
- **Short telling:** Our service had warning alerts that nobody trusted, and when something broke out of hours, it fell to whoever knew the system best. I went through every alert with the team, removed the ones nobody acted on, and wrote short instructions for each one we kept. Then I set up a rotation and took the first shifts with each new person. Today the whole team shares that responsibility, and every alert comes with clear instructions.
- **Cue lines:** alerts nobody trusted; one runbook per alert; first shifts together.
- **Follow-ups:** What did you remove, and how did you decide? What happens when a page comes in at night?

### Mentoring two engineers

- **Proves:** growing people; leading without authority.
- **Answers:** mentoring; leadership; how you give feedback.
- **Situation:** Two engineers joined the ledger team without a payments background.
- **Task:** Get them to own parts of the service.
- **Action:** I paired with them first, then reviewed their changes by asking questions instead of handing them answers, and then gave each of them a whole part of the service.
- **Result:** Both own a part of the service and take on-call.
- **Telling:** Two engineers joined the ledger team without a payments background. A ledger is unforgiving: a small mistake turns into money in the wrong place, so they needed more than a tour of the code. My task was to get each of them to the point where they owned a part of the service. I started by pairing with them on real changes, so they saw how I think about the books and not only about the code. Pairing came first because the hardest part of a ledger is knowing which numbers must always add up. Then I changed how I reviewed their work. Instead of telling them what to fix, I asked questions. What happens if this event arrives twice? What does the balance look like halfway through? They found most of the answers themselves, and those answers stuck. When their changes needed fewer questions, I gave each of them a whole part of the service, with its design and its on-call. Today both of them own a part of the service and take on-call. Asking took longer than telling at the start, and I would make the same choice again.
- **Short telling:** Two engineers joined my team without any background in payments, where a small mistake means money in the wrong place. I worked side by side with them first. Then I reviewed their work by asking questions instead of handing them answers, so they learned to spot problems themselves. Once they were ready, I gave each of them a whole part of our system to look after. Today both of them run their parts on their own, problems out of hours included.
- **Cue lines:** pair, then questions in review, then a whole part each.
- **Follow-ups:** How do you give hard feedback? What did you learn from mentoring?

### Changing an API other teams depend on

- **Proves:** working across teams; changing a shared system safely.
- **Answers:** working with other teams; a time you changed how others work; cross-functional work.
- **Situation:** Other teams call the ledger service, and the change we needed would have broken their calls.
- **Task:** Ship the change without breaking any team that depends on the service.
- **Action:** I put the change in a new version of the API and kept the old one running. I told each calling team what changed and why, tracked who still called the old version, and removed it only when the calls had stopped.
- **Result:** The change shipped without an incident on any calling team's side.
- **Telling:** Other teams call the ledger service to read balances, and we needed a change that would have broken their calls. Breaking them wasn't an option, because those teams build their own features on what the ledger tells them. My task was to ship the change without breaking any team that depends on the service. I put the change in a new version of the API and kept the old version running next to it. Then I went to each calling team, told them what was changing and why, in terms of their own calls, and agreed when they would move. I tracked which teams still called the old version and checked the numbers every week, so I always knew who was left instead of guessing. Teams moved at their own pace, and that was fine, because the old version stayed until the calls stopped. When the count for the old version reached zero and stayed there, I removed it. The change shipped without an incident on any calling team's side, and no team learned about it from an error.
- **Short telling:** Several other teams build on our service, and a change we needed would have broken their work. Instead of switching everyone over at once, I kept the old version running next to the new one, explained to each team what was changing and why, and kept track of who had moved. I removed the old version only once nobody used it anymore. The change went out without a single problem for any of those teams.
- **Cue lines:** new version next to the old; every team told; old version removed when calls stopped.
- **Follow-ups:** What if a team never moved? How did you track the calls?

## Reference systems

### The ledger service

- **Summary:** The service that records every money movement as double-entry ledger entries and settles accounts, for 40 million transactions a day. I own it end to end.
- **Requirements:** Every transaction recorded exactly once; balances that always add up; settlement within minutes; an audit trail that never changes; balances other teams can read through an API.
- **Design:** Spring Boot services in Kotlin and Java. PostgreSQL holds the ledger. Transactions arrive as Kafka events, consumers write the entries, and an API serves balances. Everything runs on Kubernetes in AWS, with the infrastructure in Terraform.
- **Data flow:** A payment produces an event. The ledger consumer checks it and writes the debit and the credit in one database transaction. The balance changes, and settlement events go out to the teams that need them.
- **Failure handling:** Every event carries an idempotency key, so a replayed event changes nothing. Retries back off. An event that keeps failing goes to a dead-letter topic and raises an alert. A daily reconciliation compares the ledger with the payment records.
- **Security:** The ledger is reachable only through the service. Entries are append-only: a correction is a new entry, never an edit. Access is audited.
- **Scale:** 40 million transactions a day, about 460 a second on average and more at peaks. Kafka partitions let the consumers scale out.
- **Trade-offs:** Balances that other teams read are eventually consistent, in exchange for throughput. PostgreSQL rather than a dedicated ledger database, for its transactions and the team's experience with it. Append-only entries cost storage and make the audit trail simple.
- **What I would change:** Make reconciliation continuous instead of daily, and publish balance changes as a stream that other teams can subscribe to.
- **Deep-dive points:** idempotency keys; at-least-once delivery with idempotent writes; the side-by-side run during the move from the batch; partitioning by account.
- **Honest limits:** It runs in one region; I have not run a ledger across regions. I know the daily total by heart, not the peak figures.

## Pay

The method only. The numbers come from the app settings, and only the prep sees them. My minimum and my target are gross a year for employment, and my contractor minimum is a rate an hour; they hold wherever the job is.

I ask for their range first:

> Before I name a number, could you share the range you have for this role?

- If they press, a range and never a single number. It starts above my walk-away, at a figure I would accept with room to spare, and for employment it ends at about my target. I say it the way the market there quotes pay, by the year or by the month. When the market for the role sits below my target, the range starts near the top of the market band, and the prep says where it sits against the market.
- I ask what the package holds: bonus, pension, health cover, equity, and how many salary payments a year.
- My walk-away is my minimum, raised only for a move: its one-off cost spread over two years, which the prep states with its reason. I never say it. When it lands above what the market pays, I ask about remote work first, and about a contract only once remote work is ruled out.

**Never:** "whatever you think is fair", an apology, or my current salary.

## Logistics

- **Notice:** "My notice period is two months."
- **Hours:** "I work UTC+1 hours, and I'm happy to overlap up to four hours either side."
- **Relocation:** "I'm remote-first, and for the right role I'm open to moving within Exampleland and its neighbours. What would the relocation support look like?"
- **Work authorization:** "I'm authorized to work in Exampleland. For another country it would need checking."
- **Contract form:** "Employment is what I prefer. For the right role, a contractor engagement works too."
- **Other processes:** "I'm talking to a few companies, and I'm choosing carefully."

## Drills

- Stop after the result. Count to two and wait for the next question.
- Say "I" for what I did, and "we" only for what the team did.
- Give the number: 40 million a day, hours to minutes, two engineers.
- Say the ask-first pay line out loud until it comes without a pause.
- In tech rounds: clarify the input, state the assumptions, think aloud, give the complexity, test with cases.
- In system design rounds: clarify and set the requirements with numbers before drawing anything.
- Pause instead of filling silence.
