# Next steps

1. Independently review the M003 working tree against the actual diff and
   rerun verification. Do not treat the implementer report as approval.
2. After `APPROVED`, commit M003 as a clean follow-up to
   `0d8e66d538d019237268549c80bcdec753b9b9dd`. Do not include `bin/`, `obj/`,
   or generated protocol C#.
3. Then prepare the next bounded module:
   `PREPARE MODULE: M004 — managed operation lifecycle and handler runtime`
4. Keep later slices toward reliable `drop_files` behind that architect
   process. Planned later modules, not yet prompted:
   - M004 managed operation lifecycle and handler runtime
   - M005 Windows target identity and capability discovery
   - M006 deterministic policy and mechanism router
   - M007 private native ABI v1
   - M008/M009 transfer backends with real verification
   - M010 integrated `drop_files` orchestration
   - M011 CLI and structured diagnostics

Do not resume Hyper-V, GUI, cloud, networking, or CUA vendoring. Do not
implement file transfer in M004.
