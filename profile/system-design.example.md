# System design and reasoning drill

This is the example system design file that ships with JobHunter. Copy it to `profile/system-design.md` under your data root and grow it into your own drill: the reasoning half of your technical preparation, on architecture and messaging, domain-driven design, and the scenarios that show how you think. The general knowledge questions stay in `glossary.md`. Tech and system design rounds draw on both files, and a prep names the items to rehearse here as "Focus on system design: 7, 9". A ★ after a question means a real interview asked it, one star per round: `/jh:debrief` adds them after each round, and the prep ranks by them. Lines marked **Say:** are your own experience, word for word as in `interview.md`; everything else is knowledge, not a claim about your work.

The items share one number series with `glossary.md`, so a number names one item across both files. An item moved here from the glossary keeps its number, and the glossary keeps a gap where it was. A new item takes the next free number.

## Architecture and messaging

**7. A direct call or a message?**
- A direct call when the caller needs the answer now; a message when the work can finish later.
- A direct call ties both services together in time: when the receiver is down, the request fails.
- A message waits in the broker until the receiver is back, and makes tracing harder: one request becomes several hops.

**8. The broker delivers at least once. How do you stop the work from running twice?**
- Make the consumer idempotent: each message carries a key, and the consumer skips a key it has already handled.
- Store the key in the same transaction as the change it guards.
- Outbox pattern: write the event to a table in the same transaction, then publish it from there.
- A message that keeps failing goes to a dead-letter queue and raises an alert.

## Domain-Driven Design

**9. What is a bounded context?**
- The part of a system where one model and its terms hold. Outside it, the same word can mean something else, such as "account" as a login or as a balance.
- Each context owns its data; other contexts reach it through its API or its events.
- An anti-corruption layer translates another context's model at the border.

## Scenarios: how you think

**6. A page that was fast is now slow. Where do you start?**
- Ask one question first: since when, for everyone or one customer?
- Measure before changing anything: which request, which query, how often.
- Fix the biggest cost first, and check with production-sized data that it worked.
- **Say:** "<a line from your interview.md that fits, word for word>"

**10. A service you call goes down. What does your service do?**
- A timeout on every call, never the library default.
- Retries with backoff, and only for calls that are safe to repeat.
- A circuit breaker stops the calls for a while after repeated failures.
- Degrade: serve a cached value or queue the work, and tell the user.
