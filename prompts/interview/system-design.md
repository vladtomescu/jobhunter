# System design round guide

The round where senior engineers check how I design a system and talk about it. It is standard for senior roles.

## Who is in the room

One or two senior or staff engineers, sometimes an architect or the engineering manager.

## What they score

1. Framing: the clarifying questions before any box is drawn.
2. Requirements, functional and non-functional (scale, latency, availability, consistency), with rough numbers.
3. The high-level design: the main components and how data moves between them.
4. The data model and the APIs.
5. Deep dives into one or two components.
6. Scaling and bottlenecks.
7. Failure modes and reliability.
8. Trade-offs, each one with what it gives up.
9. Operational maturity: monitoring, alerting, deployment, migration.
10. Communication, which weighs as much as correctness: a clear order, thinking aloud, checking in with the interviewer.

## Format and length

45 to 60 minutes on a shared whiteboard, such as Excalidraw or Miro. The variants:

- Design from scratch, often a problem from the company's own domain.
- "Walk us through a system you built." The main reference system in `interview.md` is the backbone of this variant.
- Low-level or object-oriented design: the classes, the interfaces and their responsibilities.

In every variant the reference systems are the source of analogies: how the system I built handled the same problem, and what it gave up.

A time plan for 45 minutes: clarify and set the requirements (about 8 minutes), the high-level design (10), deep dives (15), scale, failure and trade-offs (8), the wrap-up (4).

## Likely questions, ranked

1. Design a feature or a system from the company's own product.
2. Walk us through a system you built: its requirements, its design, its failures and its trade-offs.
3. A classic problem near the company's domain, such as a notification service, a rate limiter, a job scheduler or an event pipeline.
4. The follow-ups: what happens at ten times the load, when this component fails, when the data must stay consistent across regions; what you would monitor; how you would roll it out.

## What to prepare

- Pitch: the 30-second version.
- Reference systems: the main system's walk-through when the round may ask for it, otherwise the fields that answer this company's problems, with their honest limits.
- Drills: the order of the steps, rough capacity estimates, and naming what each trade-off gives up. For the capacity estimates, when the company publishes no volumes, practise the method with stated assumptions.
- The company's domain: two or three problems it has to solve at its scale.

## Cards

My own facts (the components, versions, dates, the choices and why; a design target only when labelled as a target); the steps (clarify, requirements and numbers, high level, data and APIs, deep dive, scale, failure, trade-offs, operations); the trade-off pairs I know well. The subtitle for this round carries "think aloud" and "share one window". My questions last.

## Mock interviewer

- Persona: a staff engineer at the company.
- Behaviour: gives a short, open prompt and waits for my clarifying questions, then answers them with plausible numbers, labelled as invented for the exercise. Lets me drive. Changes one requirement halfway. Since the mock runs by voice, asks me to narrate the diagram as I go.
- Question plan: one main problem from the company's domain, or the "system you built" variant when the context says so; then deep dives into one or two components, one failure scenario and one scale-up.
- Press on: unstated assumptions, single points of failure, data consistency, what each trade-off gives up, how I would know it is broken in production.
- Level: senior to staff.
- Clock: 45 minutes, or a 25-minute version without the wrap-up.
- Ending: time for my questions, the next steps, goodbye.

## Feedback focus

- Order: did I clarify first and keep to a structure?
- Numbers: rough capacity estimates where they matter.
- Trade-offs named together with their cost.
- Communication: the narration, the checking in, how I handled the changed requirement.
- On my own system: depth, and honest limits stated before they were found.

## Research focus

- The company's architecture, scale and domain, from its engineering blog, talks and job pages.
- The system design format the company is reported to use, and the prompts candidates mention.

## Pay

No.
