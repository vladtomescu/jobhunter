# Glossary and tech drill

This is the example glossary that ships with JobHunter. Copy it to `profile/glossary.md` under your data root and grow it into your own drill: short answers to the general technical questions, numbered so a prep can say "Focus on glossary: 2, 5". A ★ after a question means a real interview asked it, one star per round: `/jh-debrief` adds them after each round, and the prep ranks by them. Lines marked **Say:** are your own experience, word for word as in `interview.md`; everything else is knowledge, not a claim about your work. When an interview finds a general gap, add it here.

This file holds only the knowledge questions. The reasoning questions, on architecture and messaging, domain-driven design and scenarios, live in `system-design.md`, in the same number series: an item moved there keeps its number, so this file has a gap where it was.

## Runtime basics

**1. Stack vs heap?**
- Stack: local values and method calls, freed when the method returns. Fast, small.
- Heap: objects, cleaned up by the garbage collector. Larger, slower to allocate.

**2. What is connection pooling?**
- Opening a database connection is expensive, so the driver keeps a pool of open ones and lends them out.
- Close or dispose the connection quickly: that returns it to the pool.

## Databases

**3. What do the isolation levels protect against?**
- Read committed: no dirty reads. Repeatable read: a row you read does not change under you. Serializable: no phantom rows.
- Higher isolation means more locking and less concurrency.

**4. Optimistic vs pessimistic concurrency?**
- Optimistic: no lock; check a version column on save and fail if someone changed the row.
- Pessimistic: lock the row while you work on it. Safer under heavy contention, slower.

## Testing

**5. Mock, stub or fake?**
- Stub: returns canned answers. Mock: also checks how it was called. Fake: a working, simplified version, such as an in-memory store.
