# Cover letter template

How a cover letter written in my name reads: its header, its length and shape, its tone, and the paragraphs it is built from. Everything in this file is guidance for the letter except the Header section. The app prints that section above the letter itself, filled from Settings, and the model never sees it.

This is the example template that ships with JobHunter. The candidate is fictional: every fact below is a placeholder, taken from the example profile. Copy this file to `profile/cover-letter.md` under your data root and replace the rules and the paragraphs with your own, keeping the Header heading.

The Header section holds the lines printed at the top of the letter. They may use the placeholders `{name}`, `{email}`, `{phone}`, `{location}` and `{linkedin}`, which the app fills from the contact details in Settings. Segments of a line are separated by a middle dot with a space on each side; a segment whose placeholders are all empty is left out, and so is a line with nothing left on it. `**bold**` is honoured, and no other Markdown. Keep anything else out of that section, since every line in it is printed.

## Header

**{name}**
{location} · {email} · {phone} · {linkedin}

## Length and shape

- One page: 250 to 400 words of body text.
- A salutation, four paragraphs, a closing. Three paragraphs when the posting is short, five when it asks for a lot.
- One idea per paragraph, in the order of the base paragraphs below.
- Salutation: "Dear <name>," when the posting names the hiring manager or the recruiter, otherwise "Dear <company> team,".
- Closing: "Kind regards,". The app prints my name below it.

## Tone

- Mature, flowing prose: full paragraphs of joined sentences, no bullet points, no headings, no bold.
- Warmer and fuller than a short message, still direct and concrete. A letter reads like a considered note from a senior engineer, not like a form answer and not like a sales pitch.
- Every claim of motivation, working style or fit is backed by one concrete example from the profile or the resume.
- Not a resume recap. The resume travels with the letter; the letter says why this role, how I work and why I fit.
- Plain words. No marketing tone, no flattery of the company, no exclamation marks, no gratitude inflation.

## Base paragraphs

Each paragraph below is the base the letter is built from. Keep its substance and its facts, and replace every `[Tailor: ...]` slot with text written for this posting, as the slot asks. Nothing in brackets reaches the letter.

1. Why this role. I am writing about the [Tailor: the role title as the posting names it] role at [Tailor: the company]. For the last six years I have worked on payment and ledger services, and [Tailor: the one thing in the posting that connects to that work, named concretely, in one sentence].

2. What I have done that matters here. At my current company I own the ledger service that settles 40 million transactions a day. [Tailor: the example from the profile that answers the posting's main requirement, told as a short story with its outcome: the move from a nightly batch to an event-driven pipeline, which cut settlement lag from hours to minutes, or running the on-call rotation for a service other teams depend on.]

3. How I work. I own a service end to end: design, delivery, on-call and the roadmap for it. [Tailor: one example of that working style that fits what the posting says about the team, for instance mentoring two engineers or making a change safe to ship on a system other teams depend on.]

4. Why this company. The ledger work is where I do my best engineering, and I am looking for a team where payments are the product rather than a side system. [Tailor: one sentence tying that to something concrete the posting says about the company, its product or its engineering, and one sentence inviting a conversation about it.]
