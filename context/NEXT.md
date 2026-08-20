# Next steps

1. Review the Track A diff on `cursor/track-a-filesystem-prototype` and rerun
   locked restore, Release build, format, and full tests.
2. When explicitly requested, commit Track A and merge local `main` (M004
   `56b35fc`) plus Track A to the remote.
3. After Track A is the committed baseline, run the OLE spike documented in
   `context/OLE-IDROPTARGET-SPIKE.md`. Do not start M007 or M009 until that
   spike proves a documented no-pointer `IDropTarget` path.
4. Remaining original-queue work after the spike:
   - Full window identity discovery (the rest of M005)
   - M007 private native ABI only if managed COM is insufficient
   - M009 OLE application-drop backend and instrumented target
   - M012 cancellation, crash, and recovery hardening
   - M013 release qualification
   - FINAL integrated-system review

Do not resume Hyper-V, GUI, cloud, networking, or CUA vendoring. Do not add
a mouse or focus fallback.
