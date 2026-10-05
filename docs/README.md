# Research notes

Working notes from reverse-engineering TotK's renderer: uniform-block layouts recovered from the
game binary (Ghidra) and GPU captures (RenderDoc), the sky/cloud/postfx decode, and open research
questions.

They were written while this renderer lived inside the **Marrow** viewer, before it was split out
into WildRenderingSharp, so "Marrow" in them means this renderer (and, where a UI is mentioned,
that viewer). Code paths have been updated to this repository's layout.
