# Project example

The systems I walk an interviewer through, written the way I say them. The interview prep (`/jh:prep`) takes its talking points for tech, system design and manager rounds from this file, and quotes its lines as written. Every fact about my work in this file traces to my profile and my resume. Only `/jh:prep` reads this file; it is private and never sent to anyone.

This is the example project file that ships with JobHunter, for the fictional candidate of the example profile: every fact below is a placeholder. Copy it to `profile/project-example.md` under your data root and replace the system with your own, one heading per system, the main one first.

Each system opens with a short outline: six to eight lines, in the order you would tell it. Then come its fields: Summary, Requirements, Design, Data flow, Failure handling, Security, Scale, Trade-offs, What I would change, Deep-dive points and Honest limits. A field you need to say out loud can be written in layers: **Say:**, one or two spoken sentences; **If they dig:**, three to five short bullets; **Why:**, one line on the trade-off. Keep each sentence to 20 words at most, with one idea per sentence, in the active voice, and use one term for one concept.

## The ledger service

**Outline**, in the order I tell it:

1. What it is: the double-entry ledger for 40 million transactions a day, mine end to end.
2. What it must guarantee: every transaction once, balances that add up, settlement in minutes.
3. How it is built: Kafka events in, consumers, PostgreSQL, a balance API.
4. One payment through it: an event, a debit and a credit in one transaction.
5. What happens when things fail: idempotency keys, retries, a dead-letter topic, daily reconciliation.
6. What it gives up: eventually consistent balances, in exchange for throughput.
7. What I would change: continuous reconciliation and a balance stream.
8. Where it stops: one region.

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
