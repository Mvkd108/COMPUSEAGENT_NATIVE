# Next steps

1. Independently review the M004 working tree against the actual diff and
   rerun verification. Do not treat the implementer report as approval.
2. After `APPROVED`, commit M004 as a clean follow-up to
   `13e64f4b94adb47c13f131f4441911be4d1b40ab`. Do not include `bin/`, `obj/`,
   or generated protocol C#.
3. Then prepare the next bounded module:
   `PREPARE MODULE: M005 — Windows target identity and capability discovery`
4. Keep later slices toward reliable `drop_files` behind that architect
   process. Planned later modules, not yet prompted:
   - M005 Windows target identity and capability discovery
   - M006 deterministic policy and mechanism router
   - M007 private native ABI v1
   - M008/M009 transfer backends with real verification
   - M010 integrated `drop_files` orchestration
   - M011 CLI and structured diagnostics

Do not resume Hyper-V, GUI, cloud, networking, or CUA vendoring. Do not
implement file transfer, Win32 discovery, or routing in M004.
