# Next steps

1. Commit the reviewed M002 working tree as a clean follow-up to
   `42e5d0ca368939d436f5d0a8cc25d82316dc1bb7` if a commit is wanted. The M002
   implementer did not commit. Do not include `bin/`, `obj/`, or generated
   protocol C#.
2. Paste the prompt in `codex-prepare-m003.txt` into the COMPUSEAGENT_NATIVE
   Codex architect session. Do not invent M003 in Cursor.
3. After Codex returns `PREPARE MODULE: M003`, implement only that module.
4. Keep later slices toward reliable `drop_files` behind that architect
   process. Likely later areas, not yet specified:
   - Runtime host, routing, and policy that emit M001/M002 results
   - Diagnostics / CLI that cannot claim `committed` from an OS return
   - Private C ABI and narrow C++/WinRT OLE/Shell
   - Real `drop_files` with external side-effect evidence
   - Optional later pinned CUA backend through its public interface only

Do not resume Hyper-V, GUI, cloud, networking, or CUA vendoring.
