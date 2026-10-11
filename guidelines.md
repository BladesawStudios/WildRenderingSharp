# Guidelines

## Size

- Classes are about 256 lines at most.
- Methods are about 64 lines at most.

## Comments

- Each class gets a 1-2 sentence summary at the top that says what it is.
- A class may also have remarks of 3-4 sentences under the summary, for what a reader needs to know before using it.
- Every other comment is a regular `//` comment of 1-2 sentences, and only where the code cannot say it.
- No giant comments, no summaries on every member, no history.
- A comment describes what something is, never what it used to do or what changed.

## Code

- Keep it clean. The same functionality is never defined in more than one class.
- Always abstract the shared part.
- Nothing is temporary: no stopgaps, no "for now".
