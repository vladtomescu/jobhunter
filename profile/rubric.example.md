# Rubric

Seven dimensions, each scored 0, 1 or 2, for a total between 0 and 14. Score what the posting supports and nothing else. When two anchors could both fit, take the lower one.

The class letter (A, B, C or D) is computed by the app from these scores, the extracted facts and my own thresholds. Never output a class, a letter or a recommendation. My compensation minimum and target are not in this prompt; never guess them and never ask for them.

This is the example rubric that ships with JobHunter, written for the fictional candidate in the example profile. Copy it to `profile/rubric.md` under your data root and rewrite the anchors for your own niche, stack, hours and contract form. Keep the seven dimension names: the app and the schema expect them.

## 1. niche (payments and backend)

In one line: Niche match against the target roles in the profile: payments and ledger systems first, then backend and distributed systems.

- **2**: the work itself is payments, ledgers, settlement or financial infrastructure at a product company.
- **1**: backend, platform or distributed systems in a product company, without the payments angle.
- **0**: none of those, or the role sits outside my targets entirely (frontend, mobile, QA, data science).

## 2. level

- **2**: staff, principal or lead scope: ownership of architecture or technical direction, or a title at staff level and above.
- **1**: levelled Senior, or level unstated with senior-shaped responsibilities.
- **0**: junior, mid, graduate or intern level, or a scope that would drop me to junior-adjacent work.

## 3. stack

In one line: Stack: Java or Kotlin on the JVM with event streaming or distributed systems work scores 2.

- **2**: Java or Kotlin at the centre, with event streaming, distributed systems or platform work around it.
- **1**: the JVM is one stack among several, or the role is backend and distributed systems on another language with substantial systems work.
- **0**: no backend or systems content, or a stack with nothing in common with mine.

## 4. remote_timezone

In one line: Remote policy and timezone fit against the hours in the profile (UTC+1 with up to four hours of overlap), relocation included.

- **2**: remote worldwide, remote within the regions the profile accepts, or a company that already works async within my hours.
- **1**: remote in an adjacent timezone; remote with the timezone unstated; remote restricted to hours far from mine (say so in `facts.timezone_note`); hybrid or on-site with relocation, a visa or a package on the table.
- **0**: fully on-site with no relocation package and no mention of one, or remote restricted to a region that rules me out.

## 5. contract_form

In one line: Whether the posting offers the contract form the profile prefers: employment, with a contractor engagement acceptable.

- **2**: employment is offered, or the posting welcomes either employees or contractors.
- **1**: the contract form is not stated, or the role is a contractor engagement only.
- **0**: the only form on offer is one I cannot use, for example employment through a payroll entity in a country where I am not authorized to work.

## 6. comp_signal

- **2**: a concrete figure or range is stated in the posting, with a currency and a period.
- **1**: compensation is not mentioned, or described only in words such as "competitive" or "depending on experience", or offered as equity and benefits with no cash figure.
- **0**: compensation is explicitly withheld, for example a statement that ranges are not disclosed, or made conditional on the candidate naming a number first.

Put any stated figure into `facts.comp` exactly as the posting gives it, with the currency and the period, and do not convert it. The app applies my own minimum and target to those numbers after scoring and can lower the class on its own. Keep figures out of the reasoning text.

## 7. company_signal

- **2**: the hiring company is named, is product-based, and the posting describes its own product and team.
- **1**: the company is named but the posting is generic, or an agency posts on behalf of a named end client.
- **0**: the end client is unnamed, the arrangement is an outsourcing bench with rotating projects, or the posting carries no role specifics at all.

## Facts to extract

Extract from the posting only. Unknown means unknown; a guess is worse than a null.

- `level_guess`: the level the posting implies, one of junior, mid, senior, staff, principal, lead or unknown.
- `remote_policy`: remote, hybrid, onsite or unknown.
- `employment_type`: b2b, employment, either or unknown.
- `comp.min` and `comp.max`: the numbers as stated, otherwise null. A single stated figure goes in both.
- `comp.currency`: the currency as stated, ISO code where the posting gives one, otherwise null.
- `comp.period`: hour, day, month or year, otherwise null.
- `timezone_note`: one short sentence about required hours or overlap, an empty string when the posting says nothing.
- `requires_us_authorization`: true only when United States work authorization must already be held, false when sponsorship or a visa is offered or authorization is not needed, null when the posting says nothing.
- `end_client_named`: true when the actual employer is named, false when an agency hides it, null when it cannot be told.
- `ai_meaning`: a short phrase on what "AI" concretely means in this role (building the tooling, using an assistant, model work, nothing), an empty string when AI is not mentioned.

## Blocking unknowns

List only the codes that apply, and leave the array empty when none do. Each of these can hold back an otherwise strong posting.

- `end_client`: the actual employer is not named.
- `b2b`: whether a contractor (B2B) engagement is possible cannot be told from the posting. This candidate prefers employment, so leave it out unless the posting hints at contract work only.
- `timezone`: the required hours or overlap cannot be told, or they look to fall outside the hours in the profile.
- `us_authorization`: the role may require US work authorization and the posting does not settle it.

## Reasoning

At most 60 words, English, plain sentences. Name the evidence that decided the score, including whatever is missing. No compensation figures, no class letter, no advice about whether to apply.

## An example in my terms

The scoring instructions show the shape of a returned object. These are the values that example carries for this candidate.

```json
"comp": { "min": 85000, "max": 105000, "currency": "USD", "period": "year" },
"timezone_note": "",
"blocking_unknowns": ["timezone"],
"reasoning": "Named product company building its own settlement service on Kotlin and Kafka. Levelled Senior. Remote, but the overlap window is not stated, so the timezone stays open."
```
