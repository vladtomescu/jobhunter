# Cloud and infrastructure drill

This is the example cloud drill that ships with JobHunter. Copy it to `profile/cloud.md` under your data root and grow it into your own: short answers to the general questions on clouds, networking, identity, load balancing, infrastructure as code and containers, numbered so a prep can say "Focus on cloud: 11, 13". The items share one number series with `glossary.md` and `system-design.md`: an item moved between them keeps its number. A ★ after a question means a real interview asked it, one star per round: `/jh-debrief` adds them after each round, and the prep ranks by them. Lines marked **Say:** are your own experience, word for word as in `interview.md`; everything else is knowledge, not a claim about your work.

## How a cloud fits together

**11. How do services in a cloud talk to each other?**
- Two paths: over the network inside a private network (a VPC or a VNet), and through the provider's service APIs.
- Every API call is authenticated and checked against identity policies, so identity is the real perimeter.
- Asynchronous links go through queues and topics, so one service can be down without losing work.

## Identity

**12. What does least privilege mean in practice?**
- Each workload gets its own identity with only the actions and resources it needs.
- Prefer short-lived credentials from a role or a managed identity over long-lived keys.
- Start narrow and widen on evidence from denied calls, not by guessing.

## Infrastructure as code

**13. What is configuration drift?**
- Real resources changed by hand so they no longer match the code.
- A scheduled plan run shows it; fix it by updating the code or reverting the change, never by ignoring it.
