# Guidelines

## Size

- Classes are about 256 lines at most.
- Methods are about 64 lines at most.

## Comments

- Each class gets one 1-2 sentence summary at the top that says what it is.
- Every other comment is a regular `//` comment of 1-2 sentences, and only where the code cannot say it.
- No giant comments, no summaries on every member, no remarks or history.
- A comment describes what something is, never what it used to do or what changed.

## Code

- Keep it clean. The same functionality is never defined in more than one class.
- Always abstract the shared part.
- Nothing is temporary: no stopgaps, no "for now".
