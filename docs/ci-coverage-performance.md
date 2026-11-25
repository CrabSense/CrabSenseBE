# CI coverage performance

Baseline PR #84 run 45656281946: unit job 251s, database job 213s, aggregate gate 2s. Test step 171s; xUnit reported 16.52s, with approximately 88s before the adapter started and 66s after test completion. These intervals are not proven to be entirely Coverlet overhead.

Restrict OpenCover instrumentation to all four first-party production assemblies. Exclude generated EF migrations, which are excluded by Sonar and independently exercised by the PostgreSQL clean/legacy migration suite. Do not filter test execution. Upload TRX and OpenCover, and require both reports with all four assemblies. Keep the Sonar token condition unchanged.

Separate runtime-specific NuGet cache from unit cache to avoid concurrent jobs competing for a single cache key. Reuse the current-run EF build for historical migration tests only if both Release assemblies exist; standalone PowerShell execution still builds by default. All clean/legacy/rollback and bundle validations remain.

Compare elapsed wall-clock duration, test counters, coverage assembly completeness, and database job result against the baseline; cache hit and runner variability matter. No new guarantee of a particular CI runtime. Deployment gate remains unchanged.
