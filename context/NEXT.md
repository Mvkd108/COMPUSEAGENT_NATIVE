# Next steps

1. Independent review of Track A plus M012 (async backend, failed-Advise
   abort, `FILE_ADD_FILE` probe) on `cursor/track-a-filesystem-prototype`.
   Work is uncommitted.
2. When explicitly requested, commit this branch and merge local `main`
   (M004 `56b35fc`) plus Track A to the remote.
3. After Track A/M012 is the committed baseline, prepare M013 Track A
   qualification (`eng/verify-track-a.ps1` and CLI subprocess tests). Do not
   change product behavior in M013.
4. After that checkpoint, run the OLE spike documented in
   `context/OLE-IDROPTARGET-SPIKE.md` (roadmap M014). Do not start M007 or
   M009 until that spike proves a documented no-pointer `IDropTarget` path.
5. Remaining original-queue work after the spike:
   - Full window identity discovery (the rest of M005)
   - M015-N filesystem-only freeze if M014 is `NO_GO`, or M015-G–M017-G if
     `GO`
   - M018 reliability/security hardening
   - M019 release qualification
   - FINAL integrated-system review

Do not resume Hyper-V, GUI, cloud, networking, or CUA vendoring. Do not add
a mouse or focus fallback.
